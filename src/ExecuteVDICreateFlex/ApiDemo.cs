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
    public static async Task RunAsync(int? resourceCountOverride = null, FlexRunLogger? logger = null)
    {
        logger ??= FlexRunLogger.Disabled;
        var config = FlexCreateConfig.Load();
        var resourceCount = resourceCountOverride ?? FlexRequestBuilder.TotalRequestedVmCount;

        // ---- Inputs ----
        var subscriptionId = config.SubscriptionId;
        var location = config.Location;
        logger.Info($"Config: subscriptionId={subscriptionId}, resourceGroup={config.ResourceGroupName}, location={location}, vnet={config.VnetName}, subnet={config.SubnetName}, resourceCount={resourceCount}.");
        TokenCredential credential = new DefaultAzureCredential();
        var armClient = new ArmClient(credential, subscriptionId);
        var subscription = armClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(subscriptionId));
        var resourceGroup = await subscription.GetResourceGroupAsync(config.ResourceGroupName);

        logger.Info("Creating or updating virtual network.");
        var vnetClient = ArmClientFactory.CreateVNetClient(credential, config.SubscriptionId);
        var vnet = await HelperMethods.CreateVirtualNetwork(resourceGroup, config.SubnetName, config.VnetName, config.Location, vnetClient);
        var subnetId = HelperMethods.GetSubnetId(vnet).ToString();
        logger.Info($"Subnet resolved: {subnetId}");
  
        var payload = FlexRequestBuilder.BuildFlexPayload(config, subnetId, resourceCount, batchIndex: 0);
        var request = FlexRequestBuilder.BuildRequest(payload, FlexRequestBuilder.BuildExecutionParams());
        Console.WriteLine($"CorrelationId: {request.CorrelationId}");
        Console.WriteLine("Request body:");
        var requestBody = ModelReaderWriter.Write(request, ModelReaderWriterOptions.Json).ToString();
        Console.WriteLine(requestBody);
        logger.Info($"CorrelationId: {request.CorrelationId}");
        logger.SanitizedJson("Sanitized ExecuteCreateFlex request body:", requestBody);

        logger.Info("Submitting ExecuteCreateFlex request.");
        ScheduledActionCreateFlexResult result =
            (await subscription.ExecuteVirtualMachineCreateFlexOperationAsync(location, request)).Value;

        Console.WriteLine($"ExecuteCreateFlex returned {result.Results.Count} operation result(s).");
        logger.Info($"ExecuteCreateFlex returned {result.Results.Count} operation result(s).");

        // Poll operation status via shared helper
        var validOps = HelperMethods.ExcludeResourcesNotProcessed(result.Results);
        var completedOperations = new Dictionary<string, ResourceOperationDetails>();
        Console.WriteLine($"Valid operations to poll: {validOps.Count}.");
        logger.Info($"Valid operations to poll: {validOps.Count}.");

        if (validOps.Count == 0)
        {
            Console.WriteLine("No valid operations to poll");
            logger.Warning("No valid operations to poll.");
            return;
        }

        logger.Info($"Polling operation IDs: {string.Join(", ", validOps.Keys)}");
        await HelperMethods.PollOperationStatus([.. validOps.Keys], completedOperations, location, subscription);

        var completedCount = completedOperations.Count;
        var succeededCount = completedOperations.Values.Count(op => op.State == ScheduledActionOperationState.Succeeded);
        var failedCount = completedOperations.Values.Count(op => op.State == ScheduledActionOperationState.Failed);
        var cancelledCount = completedOperations.Values.Count(op => op.State == ScheduledActionOperationState.Cancelled);
        var finalStatus = $"Final status: valid={validOps.Count}, completed={completedCount}, succeeded={succeededCount}, failed={failedCount}, cancelled={cancelledCount}.";
        logger.Info(finalStatus);

        FinalStatusConsoleWriter.WriteApiStatus(
            finalStatus,
            validOps.Count,
            completedCount,
            failedCount,
            cancelledCount);
    }
}
