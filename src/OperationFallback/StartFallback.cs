using Azure.Core;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;
using UtilityMethods;

namespace OperationFallback;

/// <summary>
/// Start with clean-boot fallback.
///
/// When a hibernated VM fails to resume after all retries, setting
/// OnFailureAction to "Start" tells the system to discard the hibernated
/// session state and perform a fresh boot — maximizing the chance of the
/// VM coming back online.
///
/// ⚠️ The fallback discards the hibernated session state.
/// </summary>
public static class StartFallback
{
    public static async Task RunAsync(
        SubscriptionResource subscriptionResource,
        List<ResourceIdentifier> resourceIds,
        string location)
    {
        Console.WriteLine("=== Start with clean-boot fallback ===\n");

        var executionParams = new ScheduledActionExecutionParameterDetail()
        {
            RetryPolicy = new UserRequestRetryPolicy()
            {
                RetryCount = 3,
                RetryWindowInMinutes = 30,
                OnFailureAction = "Start"
            }
        };

        Dictionary<string, ResourceOperationDetails> completedOperations = [];
        var blockedOperationsException = new HashSet<string> { "SchedulingOperationsBlockedException", "NonSchedulingOperationsBlockedException" };

        await ComputescheduleOperations.ExecuteStartOperation(
            completedOperations,
            executionParams,
            subscriptionResource,
            blockedOperationsException,
            resourceIds,
            location);

        FallbackResultPrinter.Print(completedOperations);
    }
}
