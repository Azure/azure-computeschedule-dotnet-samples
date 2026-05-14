using System.ClientModel.Primitives;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ComputeSchedule;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;
using UtilityMethods;

namespace ExecuteVDICreateFlex;

internal static class ExecuteVDICreateFlexApiDemo
{
    public static async Task RunAsync(int? resourceCountOverride = null)
    {
        var config = FlexCreateConfig.Load();
        var resourceCount = resourceCountOverride ?? FlexRequestBuilder.TotalRequestedVmCount;

        // ---- Inputs ----
        var subscriptionId = config.SubscriptionId;
        var location = config.Location;
        TokenCredential credential = new DefaultAzureCredential();
        var armClient = new ArmClient(credential, subscriptionId);
        var subscription = armClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(subscriptionId));
        var resourceGroup = await subscription.GetResourceGroupAsync(config.ResourceGroupName);

        var vnetClient = ArmClientFactory.CreateVNetClient(credential, config.SubscriptionId);
        var vnet = await HelperMethods.CreateVirtualNetwork(resourceGroup, config.SubnetName, config.VnetName, config.Location, vnetClient);
        var subnetId = HelperMethods.GetSubnetId(vnet).ToString();
 
        var payload = FlexRequestBuilder.BuildFlexPayload(config, subnetId, resourceCount, batchIndex: 0);
        var request = FlexRequestBuilder.BuildRequest(payload, FlexRequestBuilder.BuildExecutionParams());
        Console.WriteLine($"CorrelationId: {request.CorrelationId}");
        Console.WriteLine("Request body:");
        Console.WriteLine(ModelReaderWriter.Write(request, ModelReaderWriterOptions.Json).ToString());

        ScheduledActionCreateFlexResult result =
            (await subscription.ExecuteVirtualMachineCreateFlexOperationAsync(location, request)).Value;

        Console.WriteLine($"ExecuteCreateFlex returned {result.Results.Count} operation result(s).");

        // Poll operation status via shared helper
        var validOps = HelperMethods.ExcludeResourcesNotProcessed(result.Results);
        var completedOperations = new Dictionary<string, ResourceOperationDetails>();
        Console.WriteLine($"Valid operations to poll: {validOps.Count}.");

        if (validOps.Count == 0)
        {
            Console.WriteLine("No valid operations to poll");
            return;
        }

        await HelperMethods.PollOperationStatus([.. validOps.Keys], completedOperations, location, subscription);

        var completedCount = completedOperations.Count;
        var succeededCount = completedOperations.Values.Count(op => op.State == ScheduledActionOperationState.Succeeded);
        var failedCount = completedOperations.Values.Count(op => op.State == ScheduledActionOperationState.Failed);
        var cancelledCount = completedOperations.Values.Count(op => op.State == ScheduledActionOperationState.Cancelled);

        FinalStatusConsoleWriter.WriteApiStatus(
            $"Final status: valid={validOps.Count}, completed={completedCount}, succeeded={succeededCount}, failed={failedCount}, cancelled={cancelledCount}.",
            validOps.Count,
            completedCount,
            failedCount,
            cancelledCount);
    }
}
