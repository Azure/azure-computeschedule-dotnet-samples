using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;
using UtilityMethods;

namespace OperationFallback
{
    /// <summary>
    /// Demonstrates the operation fallback (OnFailureAction) feature.
    ///
    /// Uncomment the scenario you want to run below. Each scenario shows
    /// a different fallback configuration:
    ///   1. Hibernate → Deallocate fallback (with retries)
    ///   2. Start → clean-boot fallback (with retries)
    ///   3. Hibernate → Deallocate fallback (no retries, fallback only)
    /// </summary>
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            const string location = "eastus2euap";
            const string subscriptionId = "a4f8220e-84cb-47a6-b2c0-c1900805f616";
            const string resourceGroupName = "demo-rg";

            TokenCredential cred = new DefaultAzureCredential();
            ArmClient client = new(cred);
            var subscriptionResource = HelperMethods.GetSubscriptionResource(client, subscriptionId);

            var resourceIds = new List<ResourceIdentifier>()
            {
                new($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Compute/virtualMachines/dummy-vm-600"),
            };

            // Scenario 1: Hibernate with Deallocate fallback
            await RunHibernateFallbackAsync(subscriptionResource, resourceIds, location);

            // Scenario 2: Start with clean-boot fallback
            // await RunStartFallbackAsync(subscriptionResource, resourceIds, location);

            // Scenario 3: Hibernate with Deallocate fallback (no retry window)
            // await RunHibernateFallbackNoRetryAsync(subscriptionResource, resourceIds, location);
        }

        /// <summary>
        /// Hibernate with Deallocate fallback.
        /// If hibernate fails after retries, the system deallocates the VM.
        /// </summary>
        private static async Task RunHibernateFallbackAsync(
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

            PrintFallbackResults(completedOperations);
        }

        /// <summary>
        /// Start with clean-boot fallback.
        /// If resume from hibernate fails after retries, the system performs
        /// a clean boot, discarding the hibernated session state.
        /// </summary>
        private static async Task RunStartFallbackAsync(
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

            PrintFallbackResults(completedOperations);
        }

        /// <summary>
        /// Hibernate with Deallocate fallback, no retry window.
        /// If the single attempt fails, the fallback executes immediately.
        /// </summary>
        private static async Task RunHibernateFallbackNoRetryAsync(
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

            PrintFallbackResults(completedOperations);
        }

        /// <summary>
        /// Prints operation results with fallback interpretation.
        ///
        /// When a fallback is configured, the top-level state reflects the
        /// primary operation outcome. Check FallbackOperationInfo to determine
        /// the actual VM state:
        ///   - state=Failed + fallback.Status=Succeeded → fallback recovered
        ///   - state=Failed + fallback.Status=Failed → both failed
        ///   - state=Failed + no fallback → non-retriable error (fallback skipped)
        /// </summary>
        private static void PrintFallbackResults(Dictionary<string, ResourceOperationDetails> completedOperations)
        {
            foreach (var (opId, details) in completedOperations)
            {
                Console.WriteLine($"\nOperation {opId}: State = {details.State}");

                if (details.State == ScheduledActionOperationState.Failed)
                {
                    if (details.ResourceOperationError is not null)
                    {
                        Console.WriteLine($"  Primary error: {details.ResourceOperationError.ErrorCode} — {details.ResourceOperationError.ErrorDetails}");
                    }

                    if (details.FallbackOperationInfo is not null)
                    {
                        var fallback = details.FallbackOperationInfo;
                        Console.WriteLine($"  Fallback ({fallback.LastOpType}): Status = {fallback.Status}");

                        if (fallback.Status == ScheduledActionOperationState.Succeeded)
                        {
                            Console.WriteLine("  ✅ Fallback succeeded — VM is in a safe state.");
                        }
                        else
                        {
                            Console.WriteLine("  ❌ Fallback also failed. Manual intervention may be needed.");
                            Console.WriteLine($"     Check lastOpType ({fallback.LastOpType}) to see the last operation attempted.");
                            if (fallback.Error is not null)
                            {
                                Console.WriteLine($"     Error: {fallback.Error.ErrorCode} — {fallback.Error.ErrorDetails}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("  ❌ No fallback executed (may indicate a non-retriable error).");
                    }
                }
                else if (details.State == ScheduledActionOperationState.Succeeded)
                {
                    Console.WriteLine("  ✅ Operation succeeded — no fallback needed.");
                }
            }
        }
    }
}
