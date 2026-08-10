using UtilityMethods;
using Azure.Core;
using Azure.ResourceManager.Compute.BulkActions;
using Azure.ResourceManager.Compute.BulkActions.Models;
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
        ResourceGroupResource resourceGroupResource,
        List<ResourceIdentifier> resourceIds,
        string location)
    {
        Console.WriteLine("[Scenario] Hibernate with Deallocate fallback (no retries)\n");

        var executionParams = new BulkActionExecutionParameterDetail()
        {
            RetryPolicy = new BulkOperationRetryPolicy()
            {
                OnFailureAction = ComputeBulkOperationKind.Deallocate
            }
        };

        var resourcesWithContext = resourceIds
            .Select((id, index) => new ResourceWithContext(id, $"hibernate-fallback-noretry-{index}"))
            .ToList();

        var request = new ExecuteHibernateContent(executionParams)
        {
            ResourcesWithContext = new ResourcesWithContext(resourcesWithContext)
        };

        var result = (await resourceGroupResource.BulkHibernateOperationAsync(location, request)).Value;

        var operationIds = HelperMethods.ExcludeResourcesNotProcessed(result.Results).Keys.ToHashSet();

        if (operationIds.Count == 0)
        {
            Console.WriteLine("[Submit] No operations were accepted. Check resource IDs and try again.");
            return;
        }

        Console.WriteLine($"[Submit] {operationIds.Count} operation(s) submitted. Polling for results...\n");
        var completedOperations = new Dictionary<string, ComputeBulkOperationDetails>();
        await HelperMethods.PollOperationStatus(operationIds, completedOperations, location, resourceGroupResource);

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
