using Azure.Core;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;
using UtilityMethods;

namespace OperationFallback;

/// <summary>
/// Hibernate with Deallocate fallback, no retry window.
///
/// When retryWindowInMinutes is omitted (or set to 0), the operation is
/// attempted once. If it fails with a retriable error, the system skips
/// retries and goes directly to the fallback action.
/// </summary>
public static class HibernateFallbackNoRetry
{
    public static async Task RunAsync(
        SubscriptionResource subscriptionResource,
        List<ResourceIdentifier> resourceIds,
        string location)
    {
        Console.WriteLine("=== Hibernate with Deallocate fallback (no retries) ===\n");

        var executionParams = new ScheduledActionExecutionParameterDetail()
        {
            RetryPolicy = new UserRequestRetryPolicy()
            {
                OnFailureAction = "Deallocate"
            }
        };

        Dictionary<string, ResourceOperationDetails> completedOperations = [];
        var blockedOperationsException = new HashSet<string> { "SchedulingOperationsBlockedException", "NonSchedulingOperationsBlockedException" };

        await ComputescheduleOperations.ExecuteHibernateOperation(
            completedOperations,
            executionParams,
            subscriptionResource,
            blockedOperationsException,
            resourceIds,
            location);

        FallbackResultPrinter.Print(completedOperations);
    }
}
