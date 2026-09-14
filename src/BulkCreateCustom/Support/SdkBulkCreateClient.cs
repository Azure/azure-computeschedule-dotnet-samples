using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute.BulkActions;
using Azure.ResourceManager.Compute.BulkActions.Models;
using Azure.ResourceManager.Resources;

namespace BulkCreateCustom;

internal interface IBulkCreateClient
{
    string ResourceId(BatchRequest batch);
    Task<ISubmittedBatch> SubmitAsync(BatchRequest batch, CancellationToken cancellationToken);
}

internal interface ISubmittedBatch
{
    string OperationId { get; }
    int SubmissionStatus { get; }
    Task<BatchSnapshot> RefreshAsync(CancellationToken cancellationToken);
}

internal sealed record BatchSnapshot(bool OperationCompleted, bool OperationFailed, string ProvisioningState,
    int? FulfilledCapacity, IReadOnlyList<ComputeBulkOperationResult> Results,
    bool StatusAvailable = true, string? ErrorCode = null, string? Observation = null);

internal sealed class SdkBulkCreateClient(ArmClient client, DemoConfig config) : IBulkCreateClient
{
    public string ResourceId(BatchRequest batch) =>
        LocationBasedBulkCreateCustomResource.CreateResourceIdentifier(
            config.SubscriptionId, config.ResourceGroup, config.Region, batch.OperationName).ToString();

    public async Task<ISubmittedBatch> SubmitAsync(BatchRequest batch, CancellationToken cancellationToken)
    {
        var group = client.GetResourceGroupResource(
            ResourceGroupResource.CreateResourceIdentifier(config.SubscriptionId, config.ResourceGroup));
        var operation = await group.GetLocationBasedBulkCreateCustoms(config.Region).CreateOrUpdateAsync(
            WaitUntil.Started, batch.OperationName, batch.Data, cancellationToken);
        var resource = client.GetLocationBasedBulkCreateCustomResource(new ResourceIdentifier(ResourceId(batch)));
        return new SubmittedBatch(operation.Id, operation.GetRawResponse().Status, resource);
    }

    private sealed class SubmittedBatch(
        string operationId,
        int submissionStatus,
        LocationBasedBulkCreateCustomResource resource) : ISubmittedBatch
    {
        public string OperationId => operationId;
        public int SubmissionStatus => submissionStatus;

        public async Task<BatchSnapshot> RefreshAsync(CancellationToken cancellationToken)
        {
            // Poll the custom endpoint's pageable VM results directly, not the async-operation store.
            var results = new List<ComputeBulkOperationResult>();
            string? statusError = null;
            try
            {
                await foreach (var item in resource.VirtualMachinesGetOperationStatusAsync(cancellationToken))
                    results.Add(item);
            }
            catch (RequestFailedException ex) when (IsNotYetVisible(ex))
            {
                statusError = ex.ErrorCode;
            }
            BulkCreateCustomProperties? data = null;
            try
            {
                data = (await resource.GetAsync(cancellationToken)).Value.Data.Properties;
            }
            catch (RequestFailedException ex) when (IsNotYetVisible(ex))
            {
                statusError ??= ex.ErrorCode;
            }
            var state = data?.ProvisioningState?.ToString() ?? "Unknown";
            var completed = state is "Succeeded" or "Failed" or "Canceled";
            var failed = state is "Failed" or "Canceled";
            return new BatchSnapshot(completed, failed, state, data?.PartialFulfillmentPolicy?.FulfilledCapacity, results,
                StatusAvailable: statusError is null,
                ErrorCode: results.Select(item => item.ErrorCode ?? item.Operation?.Error?.ErrorCode).FirstOrDefault(code => code is not null)
                    ?? statusError,
                Observation: statusError is null ? null
                    : "Operation resource or per-VM status is not yet visible (HTTP 404); retrying observation only within the polling deadline.");
        }

        private static bool IsNotYetVisible(RequestFailedException ex) =>
            ex.Status == 404 && ex.ErrorCode is "BulkActionNotFoundException" or "ResourceNotFound";
    }
}
