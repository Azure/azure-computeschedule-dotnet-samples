using System.ClientModel.Primitives;
using System.Text.Json;
using Azure.Core;
using Azure.ResourceManager.Compute.BulkActions.Models;

namespace BulkCreateCustom;

internal sealed class DeleteReceipt(string path, ResourceIdentifier source, ResourceIdentifier[] targets) : IDisposable
{
    private readonly FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    public string FullPath => Path.GetFullPath(path);

    public void Write(string state, Dictionary<string, string> tracked,
        Dictionary<string, ComputeBulkOperationResult> observed, DeleteResourceOperationResult? submission)
    {
        var receipt = new
        {
            sourceOperation = source.ToString(), targetIds = targets.Select(id => id.ToString()), state,
            deletionOperations = tracked, updatedUtc = DateTimeOffset.UtcNow,
            submission = submission is null ? null : ModelReaderWriter.Write(submission).ToString(),
            outcomes = observed.Values.Select(r => new
            {
                resourceId = r.ResourceId?.ToString(), operationId = r.Operation?.OperationId,
                state = r.Operation?.State?.ToString(), errorCode = DeleteExecution.ErrorCode(r)
            })
        };
        // Append checkpoints: a write interruption must not destroy the accepted-operation receipt.
        JsonSerializer.Serialize(file, receipt);
        file.WriteByte((byte)'\n');
        file.Flush(flushToDisk: true);
    }

    public void Dispose() => file.Dispose();
}
