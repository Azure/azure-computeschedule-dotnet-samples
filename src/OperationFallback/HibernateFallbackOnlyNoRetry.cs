using UtilityMethods;
using Azure.Core;
using Azure.ResourceManager.ComputeBulkActions;
using Azure.ResourceManager.ComputeBulkActions.Models;
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

        var executionParams = new BulkActionExecutionConfig()
        {
            RetryPolicy = new BulkActionRetryPolicy()
            {
                OnFailureAction = ResourceOperationType.Deallocate
            }
        };

        var resourcesWithContext = resourceIds
            .Select((id, index) => new ResourceWithContext(id, $"hibernate-fallback-noretry-{index}"))
            .ToList();

        var request = new ExecuteHibernateContent(executionParams, Guid.NewGuid().ToString())
        {
            ResourcesWithContextItems = resourcesWithContext
        };

        var result = await subscriptionResource.VirtualMachinesExecuteHibernateBulkActionAsync(location, request);

        var operationIds = HelperMethods.ExcludeResourcesNotProcessed(result.Value.Results).Keys.ToHashSet();

        if (operationIds.Count == 0)
        {
            Console.WriteLine("[Submit] No operations were accepted. Check resource IDs and try again.");
            return;
        }

        Console.WriteLine($"[Submit] {operationIds.Count} operation(s) submitted. Polling for results...\n");
        var completedOperations = new Dictionary<string, ResourceOperationDetails>();
        await HelperMethods.PollOperationStatus(operationIds, completedOperations, location, subscriptionResource);

        foreach (var (opId, details) in completedOperations)
        {
            Console.WriteLine($"[Result] Operation {opId}: State = {details.State}");

            if (details.State == OperationState.Succeeded)
            {
                Console.WriteLine("[OK] Hibernate succeeded — no fallback needed.");
            }
            else if (details.State == OperationState.Failed)
            {
                if (details.ResourceOperationError is not null)
                {
                    Console.WriteLine($"[Error] Primary: {details.ResourceOperationError.ErrorCode} — {details.ResourceOperationError.ErrorDetails}");
                }

                if (details.FallbackOperation is not null)
                {
                    var fallback = details.FallbackOperation;
                    Console.WriteLine($"[Fallback] {fallback.LastOpType}: Status = {fallback.Status}");

                    if (fallback.Status == "Succeeded")
                    {
                        Console.WriteLine("[Fallback] [OK] Succeeded — VM was deallocated (no retries attempted).");
                    }
                    else
                    {
                        Console.WriteLine("[Fallback] [FAIL] Failed. Manual intervention may be needed.");
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
