using Azure.Identity;
using UtilityMethods;

namespace ExecuteVDICreateFlex;

internal static class ExecuteVDICreateFlexBatchDemo
{
    private static readonly HashSet<string> s_blockedOperationErrors =
        ["SchedulingOperationsBlockedException", "NonSchedulingOperationsBlockedException"];

    public static async Task RunAsync(int? totalRequestedVmCountOverride = null)
    {
        // ---- Step 1: Load configuration from .env ----
        LogStep(1, "Loading configuration.");
        var config = FlexCreateConfig.Load();
        var totalRequestedVmCount = totalRequestedVmCountOverride ?? FlexRequestBuilder.TotalRequestedVmCount;
        Console.WriteLine($"Using subscription '{config.SubscriptionId}', resource group '{config.ResourceGroupName}', location '{config.Location}', resourceCount={totalRequestedVmCount}.");

        // ---- Step 2: Provision network ----
        LogStep(2, "Creating clients and resolving the resource group.");
        var credential = new DefaultAzureCredential();
        var standardClient = ArmClientFactory.CreateStandardClient(credential);
        var subscriptionResource = HelperMethods.GetSubscriptionResource(standardClient, config.SubscriptionId);
        var resourceGroup = await subscriptionResource.GetResourceGroupAsync(config.ResourceGroupName);

        LogStep(3, "Creating or updating the virtual network and resolving the subnet.");
        var vnetClient = ArmClientFactory.CreateVNetClient(credential, config.SubscriptionId);
        var vnet = await HelperMethods.CreateVirtualNetwork(resourceGroup, config.SubnetName, config.VnetName, config.Location, vnetClient);
        var subnetId = HelperMethods.GetSubnetId(vnet).ToString();
        Console.WriteLine($"Subnet resolved: {subnetId}");

        // ---- Step 3: Execute batched ExecuteCreateFlex requests ----
        LogStep(4, "Creating the ComputeSchedule client.");
        var scheduleClient = ArmClientFactory.CreateScheduleClient(credential, config.SubscriptionId);
        var scheduleSubscriptionResource = HelperMethods.GetSubscriptionResource(scheduleClient, config.SubscriptionId);

        LogStep(5, "Submitting batched ExecuteCreateFlex requests.");
        await FlexBatchExecutor.ExecuteAsync(
            config,
            subnetId,
            scheduleSubscriptionResource,
            s_blockedOperationErrors,
            totalRequestedVmCountOverride);

        LogStep(6, "Batch demo completed.");
    }

    private static void LogStep(int stepNumber, string message)
    {
        Console.WriteLine($"Step {stepNumber}: {message}");
    }
}
