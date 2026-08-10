using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute.BulkActions.Models;
using System;
using UtilityMethods;

namespace ExecuteStart
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
            const string subscriptionId = "79587181-e019-480a-a65f-fb525a441cc0";

            // ResourceGroupName: The resource group name under which the virtual machines are located, in this case, we are using a dummy resource group name
            const string resourceGroupName = "computeschedule-azcliext-resources";

            Dictionary<string, ComputeBulkOperationDetails> completedOperations = [];
            // Credential: The Azure credential used to authenticate the request
            TokenCredential cred = new DefaultAzureCredential();

            // Adding custom headers to the ARM client (optional)
            var customHeaders = new Dictionary<string, string>
            {
                ["x-ms-sa-completion-notification"] = "true",
            };
            var deallocOptions = HelperMethods.GetGeneralOptions(armLocation);
            deallocOptions.AddPolicy(new SetHeaderPolicy(customHeaders), HttpPipelinePosition.PerCall);

            // Client: The Azure Resource Manager client used to interact with the Azure Resource Manager API
            ArmClient client = new(cred, subscriptionId, deallocOptions);

            var subscriptionResource = HelperMethods.GetSubscriptionResource(client, subscriptionId);
            var resourceGroupResource = await subscriptionResource.GetResourceGroupAsync(resourceGroupName);

            // Execution parameters for the request including the retry policy used by Scheduledactions to retry the operation in case of failures
            var executionParams = new BulkActionExecutionParameterDetail()
            {
                RetryPolicy = new BulkOperationRetryPolicy()
                {
                    // Number of times ScheduledActions should retry the operation in case of failures: Range 0-7
                    RetryCount = 0,
                    // Time window in minutes within which ScheduledActions should retry the operation in case of failures: Range in minutes 5-120
                    RetryWindowInMinutes = 15
                }
            };

            // List of virtual machines to be deallocated, in this case, we are generating 10 virtual machines with the prefix "arm-on" and context prefix "arm-multivm-test"
            var resourcesWithContext = HelperMethods.GenerateResourcesWithContext(subscriptionId, resourceGroupName, "arm-multivm-test", "arm-on", 10);

            var executeDeallocateRequest = new ExecuteDeallocateContent(executionParams)
            {
                ResourcesWithContext = resourcesWithContext
            };

            await ComputeBulkActionsOperations.ExecuteDeallocateOperation(
                completedOperations,
                executionParams,
                resourceGroupResource,
                blockedOperationsException,
                executeDeallocateRequest,
                location);
        }
    }
}
