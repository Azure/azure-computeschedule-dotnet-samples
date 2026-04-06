using UtilityMethods;
using Azure.Core;
using Azure.ResourceManager.ComputeSchedule;
using Azure.ResourceManager.ComputeSchedule.Models;
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

        var executionParams = new ScheduledActionExecutionParameterDetail()
        {
            RetryPolicy = new UserRequestRetryPolicy()
            {
                RetryWindowInMinutes = 30,
                OnFailureAction = "Start"
            }
        };

        var request = new ExecuteStartContent(
            executionParams,
            new UserRequestResources(resourceIds),
            Guid.NewGuid().ToString());

        var result = await subscriptionResource.ExecuteVirtualMachineStartAsync(location, request);

        var operationIds = result.Value.Results
            .Where(r => r.Operation?.OperationId is not null)
            .Select(r => r.Operation!.OperationId)
            .ToHashSet();

        Console.WriteLine($"[Submit] {operationIds.Count} operation(s) submitted. Polling for results...\n");
        var completedOperations = new Dictionary<string, ResourceOperationDetails>();
        await UtilityMethods.HelperMethods.PollOperationStatus(operationIds, completedOperations, location, subscriptionResource);

        foreach (var (opId, details) in completedOperations)
        {
            Console.WriteLine($"[Result] Operation {opId}: State = {details.State}");

            if (details.State == ScheduledActionOperationState.Succeeded)
            {
                Console.WriteLine("[OK] Start (resume) succeeded — no fallback needed.");
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
                        Console.WriteLine("[Fallback] [OK] Succeeded — VM was clean-booted (hibernated state discarded).");
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
