using UtilityMethods;
using Azure.Core;
using Azure.ResourceManager.ComputeSchedule;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;

namespace OperationFallback;

/// <summary>
/// Hibernate with Deallocate fallback, no retry window.
///
/// When retryWindowInMinutes is omitted (or set to 0), the operation is
/// attempted once. If it fails with a retriable error, the system skips
/// retries and goes directly to the fallback action.
/// </summary>
public static class HibernateFallbackOnlyNoRetry
{
    public static async Task RunAsync(
        SubscriptionResource subscriptionResource,
        List<ResourceIdentifier> resourceIds,
        string location)
    {
        Console.WriteLine("[Scenario] Hibernate with Deallocate fallback (no retries)\n");

        var executionParams = new ScheduledActionExecutionParameterDetail()
        {
            RetryPolicy = new UserRequestRetryPolicy()
            {
                OnFailureAction = "Deallocate"
            }
        };

        var request = new ExecuteHibernateContent(
            executionParams,
            new UserRequestResources(resourceIds),
            Guid.NewGuid().ToString());

        var result = await subscriptionResource.ExecuteVirtualMachineHibernateAsync(location, request);

        var operationIds = UtilityMethods.HelperMethods.ExcludeResourcesNotProcessed(result.Value.Results).Keys.ToHashSet();

        if (operationIds.Count == 0)
        {
            Console.WriteLine("[Submit] No operations were accepted. Check resource IDs and try again.");
            return;
        }

        Console.WriteLine($"[Submit] {operationIds.Count} operation(s) submitted. Polling for results...\n");
        var completedOperations = new Dictionary<string, ResourceOperationDetails>();
        await UtilityMethods.HelperMethods.PollOperationStatus(operationIds, completedOperations, location, subscriptionResource);

        foreach (var (opId, details) in completedOperations)
        {
            Console.WriteLine($"[Result] Operation {opId}: State = {details.State}");

            if (details.State == ScheduledActionOperationState.Succeeded)
            {
                Console.WriteLine("[OK] Hibernate succeeded — no fallback needed.");
            }
            else if (details.State == ScheduledActionOperationState.Failed)
            {
                if (details.ResourceOperationError is not null)
                {
                    Console.WriteLine($"[Error] Primary: {details.ResourceOperationError.ErrorCode} — {details.ResourceOperationError.ErrorDetails}");
                }

                if (details.FallbackOperationInfo is not null)
                {
                    var fallback = details.FallbackOperationInfo;
                    Console.WriteLine($"[Fallback] {fallback.LastOpType}: Status = {fallback.Status}");

                    if (fallback.Status == ScheduledActionOperationState.Succeeded)
                    {
                        Console.WriteLine("[Fallback] [OK] Succeeded — VM was deallocated (no retries attempted).");
                    }
                    else
                    {
                        Console.WriteLine("[Fallback] [FAIL] Failed. Manual intervention may be needed.");
                        if (fallback.Error is not null)
                        {
                            Console.WriteLine($"[Fallback] Error: {fallback.Error.ErrorCode} — {fallback.Error.ErrorDetails}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("[Fallback] Not executed (may indicate a non-retriable error).");
                }
            }
        }
    }
}
