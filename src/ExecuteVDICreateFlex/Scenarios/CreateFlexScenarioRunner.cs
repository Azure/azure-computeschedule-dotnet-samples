using System.ClientModel.Primitives;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ComputeSchedule;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;
using UtilityMethods;

namespace ExecuteVDICreateFlex.Scenarios;

internal static class CreateFlexScenarioRunner
{
    public static async Task RunAsync(int scenarioNumber, int? resourceCountOverride, bool execute, FlexRunLogger? logger = null)
    {
        logger ??= FlexRunLogger.Disabled;
        if (!CreateFlexScenarioCatalog.TryGet(scenarioNumber, out var scenario))
        {
            Console.WriteLine($"Unknown scenario '{scenarioNumber}'. Valid scenarios: {string.Join(", ", CreateFlexScenarioCatalog.All.Select(item => item.Number))}.");
            logger.Warning($"Unknown scenario '{scenarioNumber}'.");
            return;
        }

        var config = FlexCreateConfig.Load();
        logger.Info($"Scenario {scenario.Number}: {scenario.Name}");
        logger.Info($"Scenario mode: {(execute ? "execute" : "preview")}; resourceCountOverride={resourceCountOverride?.ToString() ?? "<none>"}.");
        logger.Info($"Config: subscriptionId={config.SubscriptionId}, resourceGroup={config.ResourceGroupName}, location={config.Location}, vnet={config.VnetName}, subnet={config.SubnetName}.");
        TokenCredential? credential = null;
        SubscriptionResource? subscription = null;
        var subnetId = BuildSubnetId(config);

        if (execute)
        {
            logger.Info("Creating Azure clients and resolving subnet for scenario execution.");
            credential = new DefaultAzureCredential();
            var armClient = new ArmClient(credential, config.SubscriptionId);
            subscription = armClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(config.SubscriptionId));
            var resourceGroup = await subscription.GetResourceGroupAsync(config.ResourceGroupName);

            var vnetClient = ArmClientFactory.CreateVNetClient(credential, config.SubscriptionId);
            var vnet = await HelperMethods.CreateVirtualNetwork(resourceGroup, config.SubnetName, config.VnetName, config.Location, vnetClient);
            subnetId = HelperMethods.GetSubnetId(vnet).ToString();
            logger.Info($"Subnet resolved: {subnetId}");
        }

        var request = CreateFlexScenarioExamples.BuildRequest(scenario, config, subnetId, resourceCountOverride);

        Console.WriteLine($"Scenario {scenario.Number}: {scenario.Name}");
        Console.WriteLine($"CorrelationId: {request.CorrelationId}");
        Console.WriteLine("Request body:");
        var requestBody = ModelReaderWriter.Write(request, ModelReaderWriterOptions.Json).ToString();
        Console.WriteLine(requestBody);
        logger.Info($"CorrelationId: {request.CorrelationId}");
        logger.SanitizedJson("Sanitized scenario ExecuteCreateFlex request body:", requestBody);

        if (!execute)
        {
            Console.WriteLine("Scenario request was not submitted. Add --execute to submit it.");
            logger.Info("Scenario request was not submitted. Add --execute to submit it.");
            return;
        }

        var executeSubscription = subscription ?? throw new InvalidOperationException("Subscription client was not initialized for scenario execution.");
        logger.Info("Submitting scenario ExecuteCreateFlex request.");
        ScheduledActionCreateFlexResult result =
            (await executeSubscription.ExecuteVirtualMachineCreateFlexOperationAsync(config.Location, request)).Value;

        Console.WriteLine($"ExecuteCreateFlex returned {result.Results.Count} operation result(s).");
        logger.Info($"ExecuteCreateFlex returned {result.Results.Count} operation result(s).");

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
        await HelperMethods.PollOperationStatus([.. validOps.Keys], completedOperations, config.Location, executeSubscription);

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

    private static string BuildSubnetId(FlexCreateConfig config) =>
        $"/subscriptions/{config.SubscriptionId}/resourceGroups/{config.ResourceGroupName}/providers/Microsoft.Network/virtualNetworks/{config.VnetName}/subnets/{config.SubnetName}";
}
