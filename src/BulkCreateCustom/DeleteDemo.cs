using System.ClientModel.Primitives;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute.BulkActions;
using Azure.ResourceManager.Compute.BulkActions.Models;
using Azure.ResourceManager.Resources;

namespace BulkCreateCustom;

internal static class BulkDeleteDemo
{
    public static Task<int> RunCommandAsync(string[] args) => DeleteCommand.RunAsync(args);

    public static async Task<int> RunAsync(ArmClient client, DemoConfig config, string operationName,
        bool execute, DemoLog log, string receiptPath, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        config.ValidateScope();
        if (!Guid.TryParseExact(operationName, "D", out var operationGuid) || operationGuid == Guid.Empty)
            throw new ArgumentException("Select an explicit nonempty Batch B bulk-resource UUID.");
        var group = client.GetResourceGroupResource(ResourceGroupResource.CreateResourceIdentifier(config.SubscriptionId, config.ResourceGroup));
        var id = LocationBasedBulkCreateCustomResource.CreateResourceIdentifier(
            config.SubscriptionId, config.ResourceGroup, config.Region, operationName);
        var source = client.GetLocationBasedBulkCreateCustomResource(id);
        var execution = new DeleteExecution(log);

        return await execution.RunAsync(TimeSpan.FromMinutes(config.PollTimeoutMinutes), cancellationToken, async token =>
        {
            log.Write($"Batch B delete source: {id}; mode={(execute ? "EXECUTE" : "PREVIEW")}");
            var targets = await DeleteTargets.DiscoverAndValidateAsync(source, config, id, token);
            var request = new ExecuteDeleteContent(
                new BulkActionExecutionParameterDetail
                {
                    RetryPolicy = new BulkOperationRetryPolicy { RetryWindowInMinutes = 5 }
                }, new UserRequestResources(targets)) { IsForceDeletion = false };
            DeleteTargets.PrintPreview(log, targets);
            log.WriteJson("Bulk Delete request JSON:", ModelReaderWriter.Write(request, new ModelReaderWriterOptions("W")));

            if (!execute)
            {
                log.Write("Preview complete: 50 successful Batch B VM targets. No deletion submitted. Append --execute to delete.");
                return 0;
            }

            // Reserve a durable receipt before mutation; never automatically replay this submission.
            using var receipt = new DeleteReceipt(receiptPath, id, targets);
            execution.RecordPrepared(receipt);
            execution.SubmissionAttempted = true;
            var response = await group.BulkDeleteOperationAsync(config.Region, request, token);
            execution.SubmissionAccepted = true;
            return await execution.WaitForResultsAsync(group, config.Region, targets, response, receipt, pollInterval, token);
        });
    }

    internal static HashSet<string> ValidateSource(DemoConfig config, ResourceIdentifier id, LocationBasedBulkCreateCustomData data) =>
        DeleteTargets.ValidateSource(config, id, data);

    internal static ResourceIdentifier[] ValidateCreations(HashSet<string> expected, IReadOnlyList<ComputeBulkOperationResult> results) =>
        DeleteTargets.ValidateCreations(expected, results);
}
