using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute.BulkActions.Models;
using UtilityMethods;

namespace AllScenarios
{
    public static class Program
    {
        /// <summary>
        /// This project shows a sample use case for the ComputeBulkActions SDK
        /// </summary>
        public static async Task Main(string[] args)
        {
            var blockedOperationsException = new HashSet<string> { "SchedulingOperationsBlockedException", "NonSchedulingOperationsBlockedException" };

            // Location: The location of the virtual machines
            const string location = "eastus2euap";

            // ArmLocation: The ARM location of the virtual machines, in this case, we are using a dummy ARM location
            var armLocation = "brazilus";

            // SubscriptionId: The subscription id under which the virtual machines are located, in this case, we are using a dummy subscriptionId
            const string subscriptionId = "5f10bcec-dd19-47e0-b1ef-95266fdd23ca";

            // ResourceGroupName: The resource group name under which the virtual machines are located, in this case, we are using a dummy resource group name
            const string resourceGroupName = "computebulkactions-azcliext-resources";

            Dictionary<string, ComputeBulkOperationDetails> completedOperations = [];
            // Credential: The Azure credential used to authenticate the request
            TokenCredential cred = new DefaultAzureCredential();

            // Adding custom headers to the ARM client (optional)
            var customHeaders = new Dictionary<string, string>
            {
                ["x-ms-sa-completion-notification"] = "true",
            };
            var generalOptions = HelperMethods.GetGeneralOptions(armLocation);
            generalOptions.AddPolicy(new SetHeaderPolicy(customHeaders), HttpPipelinePosition.PerCall);

            // Client: The Azure Resource Manager client used to interact with the Azure Resource Manager API
            ArmClient client = new(cred, subscriptionId, generalOptions);
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

            // Execute type operation: Start operation on virtual machines
            //var resourcesToStart = HelperMethods.GenerateResourcesWithContext(subscriptionId, resourceGroupName, "arm-multivm-test", "arm-on", 10);
            //var executeStartRequest = new ExecuteStartContent(executionParams, Guid.NewGuid().ToString())
            //{
            //    ResourcesWithContextItems = resourcesToStart
            //};
            //await ComputeBulkActionsOperations.ExecuteStartOperation(
            //    completedOperations,
            //    executionParams,
            //    resourceGroupResource,
            //    blockedOperationsException,
            //    executeStartRequest,
            //    location);

            /*
             * Before creating a virtual machine, a virtual network and subnet must be created in the resource group
             * This is what will be used by the virtual machine
             */
            var vnetOptions = new ArmClientOptions();
            vnetOptions.SetApiVersion(new ResourceType("Microsoft.Network/virtualNetworks"), "2025-03-01");
            var vnetClient = new ArmClient(cred, subscriptionId, vnetOptions);
            var vnet = await HelperMethods.CreateVirtualNetwork(resourceGroupResource, "default-subnet", "default-vnet", location, vnetClient);
            var subnet = HelperMethods.GetSubnetId(vnet);

            // resource overrides generation for the create operation
            var resourceOverrideOne = HelperMethods.GenerateResourceOverrideItem(
                "override-vm-name",
                location,
                "Standard_D2ads_v5",
                "YourStr0ngP@ssword123!",
                "testUserName");

            var resourceOverrideTwo = HelperMethods.GenerateResourceOverrideItem(
                "override-vm-name-two",
                location,
                "Standard_D2ads_v5",
                "YourStr0ngP@ssword123!",
                "testUserName");

            // Create type operation: Create operation on virtual machines
            await ComputeBulkActionsOperations.ExecuteCreateOperation(
                completedOperations,
                executionParams,
                resourceGroupResource,
                blockedOperationsException,
                [resourceOverrideOne, resourceOverrideTwo],
                3,
                true,
                location,
                resourceGroupName,
                subscriptionId,
                vnet.Id.Name,
                subnet.Name);

            // Delete type operation: Delete operation on virtual machines
            //var resourcesToDelete = HelperMethods.GenerateResourcesWithContext(subscriptionId, resourceGroupName, "arm-multivm-test", "arm-on", 10);
            //var executeDeleteRequest = new ExecuteDeleteContent(executionParams, Guid.NewGuid().ToString())
            //{
            //    ResourcesWithContextItems = resourcesToDelete
            //};
            //await ComputeBulkActionsOperations.ExecuteDeleteOperation(
            //    executeDeleteRequest,
            //    executionParams,
            //    resourceGroupResource,
            //    blockedOperationsException,
            //    location,
            //    isForceDeletion: true);
        }
    }
}
