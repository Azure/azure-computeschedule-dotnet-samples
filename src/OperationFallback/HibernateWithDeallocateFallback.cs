using UtilityMethods;
using Azure.Core;
using Azure.ResourceManager.ComputeSchedule;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;

namespace OperationFallback;

/// <summary>
/// Hibernate with Deallocate fallback.
///
/// If the Hibernate operation fails after all retries, the system automatically
/// deallocates the VM instead — ensuring resources are released even when
/// hibernation is not possible.
/// </summary>
public static class HibernateWithDeallocateFallback
{
    public static async Task RunAsync(
        SubscriptionResource subscriptionResource,
        List<ResourceIdentifier> resourceIds,
        string location)
    {
        Console.WriteLine("[Scenario] Hibernate with Deallocate fallback\n");

        var executionParams = new ScheduledActionExecutionParameterDetail()
        {
            RetryPolicy = new UserRequestRetryPolicy()
            {
                RetryWindowInMinutes = 30,
                OnFailureAction = "Deallocate"
            }
        };

        var request = new ExecuteHibernateContent(
            executionParams,
            new UserRequestResources(resourceIds),
            Guid.NewGuid().ToString());

        // Submit the hibernate operation
        var result = await subscriptionResource.ExecuteVirtualMachineHibernateAsync(location, request);

        // Collect operation IDs and poll until complete
        var operationIds = result.Value.Results
            .Where(r => r.Operation?.OperationId is not null)
            .Select(r => r.Operation!.OperationId)
            .ToHashSet();

        Console.WriteLine($"[Submit] {operationIds.Count} operation(s) submitted. Polling for results...\n");
        var completedOperations = new Dictionary<string, ResourceOperationDetails>();
        await UtilityMethods.HelperMethods.PollOperationStatus(operationIds, completedOperations, location, subscriptionResource);

        // Interpret results — check FallbackOperationInfo when state is Failed
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
                        Console.WriteLine("[Fallback] [OK] Succeeded — VM was deallocated.");
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
