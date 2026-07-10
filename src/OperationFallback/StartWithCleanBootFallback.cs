using UtilityMethods;
using Azure.Core;
using Azure.ResourceManager.ComputeBulkActions;
using Azure.ResourceManager.ComputeBulkActions.Models;
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
        SubscriptionResource subscriptionResource,
        List<ResourceIdentifier> resourceIds,
        string location)
    {
        Console.WriteLine("[Scenario] Start with clean-boot fallback\n");

        var executionParams = new BulkActionExecutionConfig()
        {
            RetryPolicy = new BulkActionRetryPolicy()
            {
                RetryWindowInMinutes = 30,
                OnFailureAction = ResourceOperationType.Start
            }
        };

        var resourcesWithContext = resourceIds
            .Select((id, index) => new ResourceWithContext(id, $"start-fallback-{index}"))
            .ToList();

        var request = new ExecuteStartContent(executionParams, Guid.NewGuid().ToString())
        {
            ResourcesWithContextItems = resourcesWithContext
        };

        var result = await subscriptionResource.VirtualMachinesExecuteStartBulkActionAsync(location, request);

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

            if (details.State == OperationState.Succeeded)
            {
                Console.WriteLine("[OK] Start (resume) succeeded — no fallback needed.");
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
