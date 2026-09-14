using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Compute.BulkActions;
using Azure.ResourceManager.Compute.BulkActions.Models;
using Azure.ResourceManager.Resources;

namespace BulkCreateCustom;

internal sealed class DeleteExecution(DemoLog log)
{
    private readonly Dictionary<string, ComputeBulkOperationResult> observed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> tracked = new(StringComparer.Ordinal);
    private bool invalidResponse;
    public bool SubmissionAttempted { get; set; }
    public bool SubmissionAccepted { get; set; }

    public async Task<int> RunAsync(TimeSpan timeout, CancellationToken cancellationToken,
        Func<CancellationToken, Task<int>> run)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            return await run(deadline.Token);
        }
        catch (InvalidDataException ex)
        {
            log.Write($"Delete safety check failed: {ex.Message}");
            return 1;
        }
        catch (RequestFailedException ex)
        {
            log.Write($"Azure request failed: HTTP={ex.Status}; code={ex.ErrorCode ?? "not supplied"}. No delete resubmission.");
            return 1;
        }
        catch (AuthenticationFailedException)
        {
            log.Write("Authentication failed; credential details omitted.");
            return 1;
        }
        catch (HttpRequestException)
        {
            log.Write("Transport failure. If submission started, acceptance may be unknown; inspect the receipt and resource IDs.");
            return 1;
        }
        catch (OperationCanceledException)
        {
            log.Write(cancellationToken.IsCancellationRequested ? "Delete observation cancelled locally." : "Delete observation/request timed out.");
            log.Write("Local cancellation does not cancel Azure deletion. Do not blindly resubmit.");
            return 1;
        }
        finally
        {
            log.Write($"Delete submissionAttempted={SubmissionAttempted}; accepted={SubmissionAccepted}");
            if (SubmissionAttempted) LogCounts();
        }
    }

    public void RecordPrepared(DeleteReceipt receipt)
    {
        receipt.Write("Prepared", tracked, observed, null);
        log.Write($"Delete receipt: {receipt.FullPath}");
    }

    public async Task<int> WaitForResultsAsync(ResourceGroupResource group, string region,
        ResourceIdentifier[] targets, Response<DeleteResourceOperationResult> response, DeleteReceipt receipt,
        TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        log.Write($"Bulk Delete submission HTTP={response.GetRawResponse().Status}; response is not proof of VM deletion.");
        // Persist raw result models (no VM profile/credentials) before inspecting their shape.
        receipt.Write("Accepted", tracked, observed, response.Value);
        TrackAccepted(targets, response.Value);

        while (true)
        {
            var pending = tracked.Where(pair => !observed.TryGetValue(pair.Value, out var result) || !IsTerminal(result))
                .Select(pair => pair.Key).ToArray();
            LogCounts();
            if (pending.Length == 0) break;
            await Task.Delay(pollInterval, cancellationToken);
            try
            {
                var status = await group.BulkGetOperationsStatusAsync(region,
                    new GetBulkOperationStatusContent(pending), cancellationToken);
                TrackStatus(status.Value.Results);
                receipt.Write("Observing", tracked, observed, response.Value);
            }
            catch (RequestFailedException ex) when (ex.Status is 408 or 429 or 500 or 502 or 503 or 504)
            {
                log.Write($"Delete status read HTTP={ex.Status}; retrying observation only.");
            }
        }
        var success = !invalidResponse && observed.Count == 50 && observed.Values.All(IsSucceeded);
        receipt.Write(success ? "Succeeded" : "IncompleteOrFailed", tracked, observed, response.Value);
        foreach (var result in observed.Values)
            log.WriteDetail($"Delete result: resource={result.ResourceId}; operation={result.Operation?.OperationId ?? "not supplied"}; " +
                $"state={result.Operation?.State?.ToString() ?? "Unknown"}; errorCode={ErrorCode(result) ?? "not supplied"}");
        log.Write($"Bulk Delete completeSuccess={success}. No additional cleanup or create request was submitted.");
        return success ? 0 : 1;
    }

    private void TrackAccepted(ResourceIdentifier[] targets, DeleteResourceOperationResult response)
    {
        var expected = targets.Select(target => target.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var responseCounts = response.Results.Where(r => r.ResourceId is not null)
            .GroupBy(r => r.ResourceId.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var result in response.Results)
        {
            var vm = result.ResourceId?.ToString();
            var deleteId = result.Operation?.OperationId;
            var duplicateOperationId = !string.IsNullOrWhiteSpace(deleteId) && tracked.ContainsKey(deleteId);
            if (vm is null || !expected.Contains(vm) || responseCounts[vm] != 1 || duplicateOperationId ||
                (result.Operation is { } operation && operation.OperationKind != ComputeBulkOperationKind.Delete) ||
                (result.Operation?.ResourceId is { } inner && !string.Equals(inner.ToString(), vm, StringComparison.OrdinalIgnoreCase)))
            {
                invalidResponse = true;
                log.Write("Bulk Delete returned an unexpected/duplicate target or operation mapping; overall success is blocked.");
                continue;
            }
            observed[vm] = result;
            if (!string.IsNullOrWhiteSpace(deleteId) && deleteId != Guid.Empty.ToString())
                tracked.Add(deleteId, vm);
            else if (IsSucceeded(result) || !IsTerminal(result))
            {
                invalidResponse = true;
                log.Write($"No usable deletion operation ID for {vm}; outcome is unknown.");
            }
            log.WriteDetail($"Delete accepted result: resource={vm}; operation={deleteId ?? "not supplied"}; " +
                $"state={result.Operation?.State?.ToString() ?? "Unknown"}; errorCode={ErrorCode(result) ?? "not supplied"}");
        }
        if (responseCounts.Count != 50) invalidResponse = true;
    }

    private void TrackStatus(IEnumerable<ComputeBulkOperationResult> results)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            var deleteId = result.Operation?.OperationId;
            if (deleteId is null || !tracked.TryGetValue(deleteId, out var vm) || !seen.Add(deleteId) ||
                !string.Equals(result.ResourceId?.ToString(), vm, StringComparison.OrdinalIgnoreCase) ||
                result.Operation?.OperationKind != ComputeBulkOperationKind.Delete ||
                (result.Operation?.ResourceId is { } inner && !string.Equals(inner.ToString(), vm, StringComparison.OrdinalIgnoreCase)))
            {
                invalidResponse = true;
                log.Write("Unexpected deletion status mapping; not counted as success.");
                continue;
            }
            if (!observed.TryGetValue(vm, out var prior) || !IsTerminal(prior))
                observed[vm] = result;
        }
    }

    internal static string? ErrorCode(ComputeBulkOperationResult result) => result.ErrorCode ?? result.Operation?.Error?.ErrorCode;
    private static bool HasError(ComputeBulkOperationResult result) =>
        result.ErrorCode is not null || result.ErrorDetails is not null || result.Operation?.Error is not null;
    internal static bool IsSucceeded(ComputeBulkOperationResult result) => result.Operation?.State == BulkActionOperationState.Succeeded && !HasError(result);
    private static bool IsTerminal(ComputeBulkOperationResult result) => HasError(result) ||
        result.Operation?.State is { } state && (state == BulkActionOperationState.Succeeded || state == BulkActionOperationState.Failed || state == BulkActionOperationState.Cancelled);

    private void LogCounts()
    {
        var succeeded = observed.Values.Count(IsSucceeded);
        var failed = observed.Values.Count(r => HasError(r) || r.Operation?.State == BulkActionOperationState.Failed);
        var cancelled = observed.Values.Count(r => !HasError(r) && r.Operation?.State == BulkActionOperationState.Cancelled);
        log.Write($"Delete requested=50; succeeded={succeeded}; failed={failed}; cancelled={cancelled}; " +
            $"pending={observed.Count - succeeded - failed - cancelled}; unreported={50 - observed.Count}");
    }
}
