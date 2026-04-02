using Azure.Core;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;
using UtilityMethods;

namespace OperationFallback;

/// <summary>
/// Hibernate with Deallocate fallback.
///
/// If the Hibernate operation fails after all retries, the system automatically
/// deallocates the VM instead — ensuring resources are released even when
/// hibernation is not possible.
/// </summary>
public static class HibernateFallback
{
    public static async Task RunAsync(
        SubscriptionResource subscriptionResource,
        List<ResourceIdentifier> resourceIds,
        string location)
    {
        Console.WriteLine("=== Hibernate with Deallocate fallback ===\n");

        var executionParams = new ScheduledActionExecutionParameterDetail()
        {
            RetryPolicy = new UserRequestRetryPolicy()
            {
                RetryCount = 3,
                RetryWindowInMinutes = 30,
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
