using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute.BulkActions.Models;
using UtilityMethods;

namespace ExecuteCreate
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var blockedOperationsException = new HashSet<string> { "SchedulingOperationsBlockedException", "NonSchedulingOperationsBlockedException" };

            // Location: The location of the virtual machines
            const string location = "southcentralusstg";

            // ArmLocation: The ARM location of the virtual machines, in this case, we are using a dummy ARM location
            var armLocation = "brazilus";

            // SubscriptionId: The subscription id under which the virtual machines are located, in this case, we are using a dummy subscriptionId
            const string subscriptionId = "eb9fa9eb-7e35-486f-8d93-0240680bdd66";

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
            var createOptions = HelperMethods.GetGeneralOptions(armLocation);
            createOptions.AddPolicy(new SetHeaderPolicy(customHeaders), HttpPipelinePosition.PerCall);

            // Client: The Azure Resource Manager client used to interact with the Azure Resource Manager API
            ArmClient client = new(cred, subscriptionId, createOptions);
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

            /*
             * Before creating a virtual machine, a virtual network and subnet must be created in the resource group
             * This is what will be used by the virtual machine
             */
            var options = new ArmClientOptions();
            options.SetApiVersion(new ResourceType("Microsoft.Network/virtualNetworks"), "2025-03-01");
            var vnetClient = new ArmClient(cred, subscriptionId, options);
            var vnet = await HelperMethods.CreateVirtualNetwork(resourceGroupResource, "tester-subnet", "tester-vnet", location, vnetClient);
            var subnet = HelperMethods.GetSubnetId(vnet);
            var dataDisk = await HelperMethods.CreateDataDisk(resourceGroupResource, subscriptionId, "dotnet-sdk", location, vnetClient);

            // resource overrides generation for the create operation
            var resourceOverrideOne = HelperMethods.GenerateResourceOverrideItem(
                "vmnameOne",
                location,
                "Standard_D2ads_v5",
                "YourStr0ngP@ssword123!",
                "testUserName",
                dataDisk?.Id);

            var resourceOverrideTwo = HelperMethods.GenerateResourceOverrideItem(
                "vmnameTwo",
                location,
                "Standard_D2ads_v5",
                "YourStr0ngP@ssword123!",
                "testUserName",
                $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Compute/disks/dotnet-sdk-two");

            // Create type operation: Create operation on virtual machines
            await ComputeBulkActionsOperations.ExecuteCreateOperation(
                completedOperations,
                executionParams,
                resourceGroupResource,
                blockedOperationsException, 
                [resourceOverrideOne, resourceOverrideTwo],
                2,
                true,
                location,
                resourceGroupName,
                subscriptionId,
                vnet.Id.Name,
                subnet.Name);
        }
    }
}
