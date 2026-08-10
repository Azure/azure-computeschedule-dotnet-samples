using UtilityMethods;
using Azure.Core;
using Azure.ResourceManager.Compute.BulkActions;
using Azure.ResourceManager.Compute.BulkActions.Models;
using Azure.ResourceManager.Resources;

namespace OperationFallback;

/// <summary>
/// Start with clean-boot fallback.
///
/// When a hibernated VM fails to resume after all retries, setting
/// OnFailureAction to "Start" tells the system to discard the hibernated
/// session state and perform a fresh boot — maximizing the chance of the
/// VM coming back online.
///
/// [WARN] The fallback discards the hibernated session state.
/// </summary>
public static class StartWithCleanBootFallback
{
    public static async Task RunAsync(
        ResourceGroupResource resourceGroupResource,
        List<ResourceIdentifier> resourceIds,
        string location)
    {
        Console.WriteLine("[Scenario] Start with clean-boot fallback\n");

        var executionParams = new BulkActionExecutionParameterDetail()
        {
            RetryPolicy = new BulkOperationRetryPolicy()
            {
                RetryWindowInMinutes = 30,
                OnFailureAction = ComputeBulkOperationKind.Start
            }
        };

        var resourcesWithContext = resourceIds
            .Select((id, index) => new ResourceWithContext(id, $"start-fallback-{index}"))
            .ToList();

        var request = new ExecuteStartContent(executionParams)
        {
            ResourcesWithContext = new ResourcesWithContext(resourcesWithContext)
        };

        var result = (await resourceGroupResource.BulkStartOperationAsync(location, request)).Value;

        var operationIds = UtilityMethods.HelperMethods.ExcludeResourcesNotProcessed(result.Results).Keys.ToHashSet();

        if (operationIds.Count == 0)
        {
            Console.WriteLine("[Submit] No operations were accepted. Check resource IDs and try again.");
            return;
        }

        Console.WriteLine($"[Submit] {operationIds.Count} operation(s) submitted. Polling for results...\n");
        var completedOperations = new Dictionary<string, ComputeBulkOperationDetails>();
        await UtilityMethods.HelperMethods.PollOperationStatus(operationIds, completedOperations, location, resourceGroupResource);

        foreach (var (opId, details) in completedOperations)
        {
            Console.WriteLine($"[Result] Operation {opId}: State = {details.State}");

            if (details.State == BulkActionOperationState.Succeeded)
            {
                Console.WriteLine("[OK] Start (resume) succeeded — no fallback needed.");
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
                        Console.WriteLine("[Fallback] [OK] Succeeded — VM was clean-booted (hibernated state discarded).");
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
