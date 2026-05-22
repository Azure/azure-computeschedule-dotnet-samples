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
        await RunAsync("API sample", FlexRequestBuilder.BuildRequest, resourceCountOverride, logger);
    }

    internal static async Task RunAsync(
        string sampleName,
        Func<FlexCreateConfig, string, int, ExecuteCreateFlexContent> requestBuilder,
        int? resourceCountOverride = null,
        FlexRunLogger? logger = null)
    {
        logger ??= FlexRunLogger.Disabled;
        logger.Info($"Running {sampleName}.");
        var sampleContext = LoadSampleContext(resourceCountOverride, logger);
        var (subscriptionResource, resourceGroupResource) = await CreateAzureResourcesAsync(sampleContext);
        var subnetId = await PrepareSubnetAsync(sampleContext, resourceGroupResource, logger);
        var createFlexRequest = BuildCreateFlexRequest(sampleContext, subnetId, requestBuilder, logger);

        var createFlexResult = await SubmitCreateFlexRequestAsync(sampleContext, subscriptionResource, createFlexRequest, logger);
        await PollAndReportAsync(sampleContext, subscriptionResource, createFlexResult, logger);
    }

    private sealed record ApiSampleContext(
        FlexCreateConfig Config,
        int ResourceCount,
        TokenCredential Credential,
        string SubscriptionId,
        string Location);

    private static ApiSampleContext LoadSampleContext(int? resourceCountOverride, FlexRunLogger logger)
    {
        var config = FlexCreateConfig.Load();
        var resourceCount = resourceCountOverride ?? FlexRequestBuilder.TotalRequestedVmCount;
        var subscriptionId = config.SubscriptionId;
        var location = config.Location;
        logger.Info($"Config: subscriptionId={subscriptionId}, resourceGroup={config.ResourceGroupName}, location={location}, vnet={config.VnetName}, subnet={config.SubnetName}, resourceCount={resourceCount}.");

        return new ApiSampleContext(
            config,
            resourceCount,
            new DefaultAzureCredential(),
            subscriptionId,
            location);
    }

    private static async Task<(SubscriptionResource SubscriptionResource, ResourceGroupResource ResourceGroupResource)> CreateAzureResourcesAsync(ApiSampleContext context)
    {
        var armClient = new ArmClient(context.Credential, context.SubscriptionId);
        var subscriptionResource = armClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(context.SubscriptionId));
        var resourceGroupResource = await subscriptionResource.GetResourceGroupAsync(context.Config.ResourceGroupName);

        return (subscriptionResource, resourceGroupResource);
    }

    private static async Task<string> PrepareSubnetAsync(
        ApiSampleContext context,
        ResourceGroupResource resourceGroupResource,
        FlexRunLogger logger)
    {
        logger.Info("Creating or updating virtual network.");
        var vnetClient = ArmClientFactory.CreateVNetClient(context.Credential, context.Config.SubscriptionId);
        var vnet = await HelperMethods.CreateVirtualNetwork(resourceGroupResource, context.Config.SubnetName, context.Config.VnetName, context.Config.Location, vnetClient);
        var subnetId = HelperMethods.GetSubnetId(vnet).ToString();
        logger.Info($"Subnet resolved: {subnetId}");

        return subnetId;
    }

    private static ExecuteCreateFlexContent BuildCreateFlexRequest(
        ApiSampleContext context,
        string subnetId,
        Func<FlexCreateConfig, string, int, ExecuteCreateFlexContent> requestBuilder,
        FlexRunLogger logger)
    {
        var createFlexRequest = requestBuilder(
            context.Config,
            subnetId,
            context.ResourceCount);
        Console.WriteLine($"CorrelationId: {createFlexRequest.CorrelationId}");
        logger.Info($"CorrelationId: {createFlexRequest.CorrelationId}");

        if (logger.IsEnabled)
        {
            var requestBody = ModelReaderWriter.Write(createFlexRequest, ModelReaderWriterOptions.Json).ToString();
            Console.WriteLine("Request body was written to the log file with sensitive fields redacted.");
            logger.SanitizedJson("Sanitized ExecuteCreateFlex request body:", requestBody);
        }
        else
        {
            Console.WriteLine("Request body was not printed because it contains sensitive fields.");
        }

        return createFlexRequest;
    }

    private static async Task<ScheduledActionCreateFlexResult> SubmitCreateFlexRequestAsync(
        ApiSampleContext context,
        SubscriptionResource subscriptionResource,
        ExecuteCreateFlexContent createFlexRequest,
        FlexRunLogger logger)
    {
        logger.Info("Submitting ExecuteCreateFlex request.");
        var createFlexResult =
            (await subscriptionResource.ExecuteVirtualMachineCreateFlexOperationAsync(context.Location, createFlexRequest)).Value;

        Console.WriteLine($"ExecuteCreateFlex returned {createFlexResult.Results.Count} operation result(s).");
        logger.Info($"ExecuteCreateFlex returned {createFlexResult.Results.Count} operation result(s).");

        return createFlexResult;
    }

    private static async Task PollAndReportAsync(
        ApiSampleContext context,
        SubscriptionResource subscriptionResource,
        ScheduledActionCreateFlexResult createFlexResult,
        FlexRunLogger logger)
    {
        var validOps = HelperMethods.ExcludeResourcesNotProcessed(createFlexResult.Results);
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
        await HelperMethods.PollOperationStatus([.. validOps.Keys], completedOperations, context.Location, subscriptionResource);

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
