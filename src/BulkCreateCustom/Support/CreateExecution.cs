using Azure;
using Azure.Identity;
using Azure.ResourceManager.Compute.BulkActions.Models;

namespace BulkCreateCustom;

internal sealed record OutcomeCounts(int Succeeded, int Failed, int Cancelled, int Pending, int Unreported)
{
    public static OutcomeCounts From(BatchRequest batch, BatchSnapshot? snapshot)
    {
        var results = snapshot?.Results ?? [];
        var names = results.Select(r => r.ResourceId?.Name).ToArray();
        if (names.Any(n => n is null || !batch.ComputerNames.ContainsKey(n))
            || results.Any(r => !string.Equals(r.ResourceId.ToString(), batch.VmResourceIdPrefix + r.ResourceId.Name,
                StringComparison.OrdinalIgnoreCase))
            || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new InvalidDataException("Status contains duplicate, missing or unexpected resource IDs.");
        var succeeded = results.Count(r => r.Operation?.State == BulkActionOperationState.Succeeded
            && r.ErrorCode is null && r.Operation.Error is null);
        var failed = results.Count(r => r.Operation?.State == BulkActionOperationState.Failed
            || r.ErrorCode is not null || r.Operation?.Error is not null);
        var cancelled = results.Count(r => r.Operation?.State == BulkActionOperationState.Cancelled
            && r.ErrorCode is null && r.Operation.Error is null);
        return new(succeeded, failed, cancelled, results.Count - succeeded - failed - cancelled,
            batch.Data.Properties.Capacity - results.Count);
    }
}

internal static class CreateExecution
{
    public static async Task<bool> RunBatchAsync(IBulkCreateClient client, BatchRequest batch,
        DemoLog log, TimeSpan timeout, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        var capacity = batch.Data.Properties.Capacity;
        log.Write($"Batch {batch.Label}: requested={capacity}; correlation={batch.CorrelationId}; resource={client.ResourceId(batch)}");
        foreach (var (size, disk) in batch.ExpectedDisks)
            log.WriteDetail($"Batch {batch.Label}: expected OS disk for allocated {size} = {disk} GiB (not observed).");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        BatchSnapshot? snapshot = null;
        var accepted = false;
        var success = false;
        try
        {
            var submitted = await client.SubmitAsync(batch, deadline.Token);
            accepted = true;
            log.Write($"Batch {batch.Label}: submission accepted; HTTP={submitted.SubmissionStatus}; operation={submitted.OperationId}. " +
                "This is the initial submission response, not VM completion.");
            while (true)
            {
                try
                {
                    snapshot = await submitted.RefreshAsync(deadline.Token);
                }
                catch (RequestFailedException ex) when (ex.Status is 408 or 429 or 500 or 502 or 503 or 504)
                {
                    log.Write($"Batch {batch.Label}: status observation temporarily unavailable; HTTP={ex.Status}; " +
                        $"code={ex.ErrorCode ?? "unspecified"}. Retrying observation only; no create resubmission.");
                    await Task.Delay(pollInterval, deadline.Token);
                    continue;
                }
                var counts = OutcomeCounts.From(batch, snapshot);
                log.Write($"Batch {batch.Label}: state={snapshot.ProvisioningState}; succeeded={counts.Succeeded}; failed={counts.Failed}; " +
                    $"cancelled={counts.Cancelled}; pending={counts.Pending}; unreported={counts.Unreported}; " +
                    $"fulfilledCapacity={snapshot.FulfilledCapacity?.ToString() ?? "not supplied"}");
                if (snapshot.Observation is not null)
                    log.Write($"Batch {batch.Label}: {snapshot.Observation}");
                if (snapshot.ErrorCode is not null)
                    log.Write($"Batch {batch.Label}: operation/status errorCode={snapshot.ErrorCode}");
                if (snapshot.OperationCompleted && (snapshot.OperationFailed || snapshot.ProvisioningState is "Failed" or "Canceled"))
                    break;
                if (snapshot.OperationCompleted && snapshot.StatusAvailable && counts.Pending == 0 && counts.Unreported == 0
                    && (snapshot.ProvisioningState == "Succeeded" || counts.Failed > 0 || counts.Cancelled > 0))
                {
                    success = snapshot.ProvisioningState == "Succeeded" && counts.Succeeded == capacity;
                    break;
                }
                await Task.Delay(pollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            log.Write($"Batch {batch.Label}: {(cancellationToken.IsCancellationRequested ? "local cancellation" : "poll timeout")}; " +
                "Azure operations are NOT cancelled and may continue creating billable resources.");
        }
        catch (OperationCanceledException)
        {
            log.Write($"Batch {batch.Label}: transport request timed out; service acceptance may be unknown. No resubmission.");
        }
        catch (RequestFailedException ex)
        {
            log.Write($"Batch {batch.Label}: Azure request failed; HTTP={ex.Status}; code={ex.ErrorCode ?? "unspecified"}. " +
                "No automatic resubmission; inspect the operation resource for any accepted work.");
        }
        catch (AuthenticationFailedException)
        {
            log.Write($"Batch {batch.Label}: authentication failed; details omitted to protect credentials.");
        }
        catch (HttpRequestException)
        {
            log.Write($"Batch {batch.Label}: transport failure; service acceptance may be unknown.");
        }
        catch (InvalidDataException)
        {
            log.Write($"Batch {batch.Label}: invalid status response; cannot account for all requested VMs.");
            snapshot = null;
        }
        finally
        {
            var counts = OutcomeCounts.From(batch, snapshot);
            log.Write($"Batch {batch.Label} final: accepted={accepted}; requested={capacity}; succeeded={counts.Succeeded}; " +
                $"failed={counts.Failed}; cancelled={counts.Cancelled}; pending={counts.Pending}; unreported={counts.Unreported}; " +
                $"completeSuccess={success}. Unreported entries are NOT assumed failed, rejected, or successful.");
            foreach (var item in snapshot?.Results ?? [])
            {
                var name = item.ResourceId.Name;
                var size = item.VirtualMachineInfo?.VmSize;
                var expectedDisk = size is not null && batch.ExpectedDisks.TryGetValue(size, out var disk)
                    ? disk.ToString() : "unknown (allocated size not reported)";
                log.WriteDetail($"Batch {batch.Label}: resource={item.ResourceId}; state={item.Operation?.State?.ToString() ?? "Unknown"}; " +
                    $"errorCode={item.ErrorCode ?? item.Operation?.Error?.ErrorCode ?? "not supplied"}; computerName(expected)={batch.ComputerNames[name]}; " +
                    $"size={size ?? "not reported"}; zone={item.VirtualMachineInfo?.Zone ?? "not reported"}; " +
                    $"osDiskGiB(expected)={expectedDisk}; " +
                    $"errorDetails={item.ErrorDetails ?? item.Operation?.Error?.ErrorDetails ?? "not supplied"}");
            }
        }
        return success;
    }
}
