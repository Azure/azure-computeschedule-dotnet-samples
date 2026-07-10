using UtilityMethods;
using Azure.Core;
using Azure.ResourceManager.ComputeBulkActions;
using Azure.ResourceManager.ComputeBulkActions.Models;
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

        var executionParams = new BulkActionExecutionConfig()
        {
            RetryPolicy = new BulkActionRetryPolicy()
            {
                RetryWindowInMinutes = 30,
                OnFailureAction = ResourceOperationType.Deallocate
            }
        };

        var resourcesWithContext = resourceIds
            .Select((id, index) => new ResourceWithContext(id, $"hibernate-fallback-{index}"))
            .ToList();

        var request = new ExecuteHibernateContent(executionParams, Guid.NewGuid().ToString())
        {
            ResourcesWithContextItems = resourcesWithContext
        };

        // Submit the hibernate operation
        var result = await subscriptionResource.VirtualMachinesExecuteHibernateBulkActionAsync(location, request);

        // Exclude resources not processed and collect valid operation IDs
        var operationIds = UtilityMethods.HelperMethods.ExcludeResourcesNotProcessed(result.Value.Results).Keys.ToHashSet();

        if (operationIds.Count == 0)
        {
            Console.WriteLine("[Submit] No operations were accepted. Check resource IDs and try again.");
            return;
        }

        Console.WriteLine($"[Submit] {operationIds.Count} operation(s) submitted. Polling for results...\n");
        var completedOperations = new Dictionary<string, ResourceOperationDetails>();
        await UtilityMethods.HelperMethods.PollOperationStatus(operationIds, completedOperations, location, subscriptionResource);

        // Interpret results — check FallbackOperation when state is Failed
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
                        Console.WriteLine("[Fallback] [OK] Succeeded — VM was deallocated.");
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
