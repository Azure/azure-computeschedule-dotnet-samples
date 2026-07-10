using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ComputeBulkActions.Models;
using UtilityMethods;

namespace ExecuteDelete
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var blockedOperationsException = new HashSet<string> { "SchedulingOperationsBlockedException", "NonSchedulingOperationsBlockedException" };

            // Location: The location of the virtual machines
            const string location = "eastus2euap";

            // ArmLocation: The ARM location of the virtual machines, in this case, we are using a dummy ARM location
            var armLocation = "brazilus";

            // SubscriptionId: The subscription id under which the virtual machines are located, in this case, we are using a dummy subscriptionId
            const string subscriptionId = "1b5ca71e-0b7c-4848-8771-d42f6136395e";

            // ResourceGroupName: The resource group name under which the virtual machines are located, in this case, we are using a dummy resource group name
            const string resourceGroupName = "computeschedule-azcliext-resources";

            Dictionary<string, ResourceOperationDetails> completedOperations = [];
            // Credential: The Azure credential used to authenticate the request
            TokenCredential cred = new DefaultAzureCredential();

            // Adding custom headers to the ARM client (optional)
            var customHeaders = new Dictionary<string, string>
            {
                ["x-ms-sa-completion-notification"] = "true",
            };
            var deleteOptions = HelperMethods.GetGeneralOptions(armLocation);
            deleteOptions.AddPolicy(new SetHeaderPolicy(customHeaders), HttpPipelinePosition.PerCall);

            // Client: The Azure Resource Manager client used to interact with the Azure Resource Manager API
            ArmClient client = new(cred, subscriptionId, deleteOptions);

            var subscriptionResource = HelperMethods.GetSubscriptionResource(client, subscriptionId);

            // Execution parameters for the request including the retry policy used by Scheduledactions to retry the operation in case of failures
            var executionParams = new BulkActionExecutionConfig()
            {
                RetryPolicy = new BulkActionRetryPolicy()
                {
                    // Number of times ScheduledActions should retry the operation in case of failures: Range 0-7
                    RetryCount = 0,
                    // Time window in minutes within which ScheduledActions should retry the operation in case of failures: Range in minutes 5-120
                    RetryWindowInMinutes = 15
                }
            };

            // List of virtual machines to be deleted, in this case, we are generating 10 virtual machines with the prefix "arm-on" and context prefix "arm-multivm-test"
            var resourcesWithContext = HelperMethods.GenerateResourcesWithContext(subscriptionId, resourceGroupName, "arm-multivm-test", "arm-on", 10);

            var executeDeleteRequest = new ExecuteDeleteContent(executionParams, Guid.NewGuid().ToString())
            {
                ResourcesWithContextItems = resourcesWithContext
            };

            await ComputescheduleOperations.ExecuteDeleteOperation(
                executeDeleteRequest,
                executionParams,
                subscriptionResource,
                blockedOperationsException,
                location,
                isForceDeletion: true);
        }
    }
}
