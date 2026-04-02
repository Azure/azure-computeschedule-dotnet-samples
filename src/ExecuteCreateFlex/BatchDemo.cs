using Azure.Identity;
using UtilityMethods;

namespace ExecuteCreateFlex;

internal static class ExecuteCreateFlexBatchDemo
{
    private static readonly HashSet<string> s_blockedOperationErrors =
        ["SchedulingOperationsBlockedException", "NonSchedulingOperationsBlockedException"];

    public static async Task RunAsync(int? totalRequestedVmCountOverride = null)
    {
        // ---- Step 1: Load configuration from .env ----
        var config = FlexCreateConfig.Load();

        // ---- Step 2: Provision network ----
        var credential = new DefaultAzureCredential();
        var standardClient = ArmClientFactory.CreateStandardClient(credential);
        var subscriptionResource = HelperMethods.GetSubscriptionResource(standardClient, config.SubscriptionId);
        var resourceGroup = await subscriptionResource.GetResourceGroupAsync(config.ResourceGroupName);

        var vnetClient = ArmClientFactory.CreateVNetClient(credential, config.SubscriptionId);
        var vnet = await HelperMethods.CreateVirtualNetwork(resourceGroup, config.SubnetName, config.VnetName, config.Location, vnetClient);
        var subnetId = HelperMethods.GetSubnetId(vnet).ToString();

        // ---- Step 3: Execute batched ExecuteCreateFlex requests ----
        var scheduleClient = ArmClientFactory.CreateScheduleClient(credential, config.SubscriptionId, config.Location);
        var scheduleSubscriptionResource = HelperMethods.GetSubscriptionResource(scheduleClient, config.SubscriptionId);
        await FlexBatchExecutor.ExecuteAsync(
            config,
            subnetId,
            scheduleSubscriptionResource,
            s_blockedOperationErrors,
            totalRequestedVmCountOverride);
    }
}
