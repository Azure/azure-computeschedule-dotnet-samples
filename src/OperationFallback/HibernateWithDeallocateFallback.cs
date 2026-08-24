using UtilityMethods;
using Azure.Core;
using Azure.ResourceManager.Compute.BulkActions;
using Azure.ResourceManager.Compute.BulkActions.Models;
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
        ResourceGroupResource resourceGroupResource,
        List<ResourceIdentifier> resourceIds,
        string location)
    {
        Console.WriteLine("[Scenario] Hibernate with Deallocate fallback\n");

        var executionParams = new BulkActionExecutionParameterDetail()
        {
            RetryPolicy = new BulkOperationRetryPolicy()
            {
                RetryWindowInMinutes = 30,
                OnFailureAction = ComputeBulkOperationKind.Deallocate
            }
        };

        var resourcesWithContext = resourceIds
            .Select((id, index) => new ResourceWithContext(id, $"hibernate-fallback-{index}"))
            .ToList();

        var request = new ExecuteHibernateContent(executionParams)
        {
            ResourcesWithContext = new ResourcesWithContext(resourcesWithContext)
        };

        // Submit the hibernate operation
        var result = (await resourceGroupResource.BulkHibernateOperationAsync(location, request)).Value;

        // Exclude resources not processed and collect valid operation IDs
        var operationIds = UtilityMethods.HelperMethods.ExcludeResourcesNotProcessed(result.Results).Keys.ToHashSet();

        if (operationIds.Count == 0)
        {
            Console.WriteLine("[Submit] No operations were accepted. Check resource IDs and try again.");
            return;
        }

        Console.WriteLine($"[Submit] {operationIds.Count} operation(s) submitted. Polling for results...\n");
        var completedOperations = new Dictionary<string, ComputeBulkOperationDetails>();
        await UtilityMethods.HelperMethods.PollOperationStatus(operationIds, completedOperations, location, resourceGroupResource);

        // Interpret results — check FallbackOperationInfo when state is Failed
        foreach (var (opId, details) in completedOperations)
        {
            Console.WriteLine($"[Result] Operation {opId}: State = {details.State}");

            if (details.State == BulkActionOperationState.Succeeded)
            {
                Console.WriteLine("[OK] Hibernate succeeded — no fallback needed.");
            }
            else if (details.State == BulkActionOperationState.Failed)
            {
                if (details.Error is not null)
                {
                    Console.WriteLine($"[Error] Primary: {details.Error.ErrorCode} — {details.Error.ErrorDetails}");
                }

                if (details.FallbackOperationInfo is not null)
                {
                    var fallback = details.FallbackOperationInfo;
                    Console.WriteLine($"[Fallback] {fallback.LastOperationKind}: Status = {fallback.Status}");

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
