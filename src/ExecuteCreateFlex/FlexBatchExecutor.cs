using Azure;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;
using System.Diagnostics;
using UtilityMethods;

namespace ExecuteCreateFlex;

internal static class FlexBatchExecutor
{
    public static async Task ExecuteAsync(
        FlexCreateConfig config,
        string subnetId,
        SubscriptionResource scheduleSubscriptionResource,
        HashSet<string> blockedOperationErrors,
        int? totalRequestedVmCountOverride = null)
    {
        var totalRequestedVmCount = totalRequestedVmCountOverride ?? FlexRequestBuilder.TotalRequestedVmCount;
        var batchSizes = BuildBatchSizes(totalRequestedVmCount, FlexRequestBuilder.MaxResourceCountPerRequest);

        var failedOperations = new List<HelperMethods.FailedVmOperation>();
        var failedOperationsLock = new object();
        int totalValid = 0;
        int totalCompleted = 0;
        int totalSucceeded = 0;
        int totalFailed = 0;
        int totalCancelled = 0;
        int batchRequestFailures = 0;
        var batchProgress = new Dictionary<int, HelperMethods.FlexPollingProgress>();
        var aggregateProgressLock = new object();
        var aggregateStopwatch = Stopwatch.StartNew();
        var aggregateProgressLength = 0;

        Console.WriteLine($"Submitting {totalRequestedVmCount} VMs as {batchSizes.Count} batch request(s) with max {FlexRequestBuilder.MaxParallelBatches} parallel batches.");

        using var concurrencyGate = new SemaphoreSlim(FlexRequestBuilder.MaxParallelBatches);
        var batchTasks = batchSizes.Select((batchSize, batchIndex) => Task.Run(async () =>
        {
            await concurrencyGate.WaitAsync();
            try
            {
                Console.WriteLine($"Starting batch {batchIndex + 1}/{batchSizes.Count} (resourceCount={batchSize}).");

                var executionParams = FlexRequestBuilder.BuildExecutionParams();
                var payload = FlexRequestBuilder.BuildFlexPayload(config, subnetId, batchSize, batchIndex);
                var request = FlexRequestBuilder.BuildRequest(payload, executionParams);

                Dictionary<string, ResourceOperationDetails> completedOperations = [];
                var (_, summary) = await ComputescheduleOperations.ExecuteCreateFlexOperation(
                    completedOperations,
                    executionParams,
                    scheduleSubscriptionResource,
                    blockedOperationErrors,
                    request,
                    config.Location,
                    renderPollingProgress: false,
                    onPollingProgress: progress =>
                    {
                        lock (aggregateProgressLock)
                        {
                            batchProgress[batchIndex] = progress;

                            var knownValid = batchProgress.Values.Sum(p => p.ValidCount);
                            var completed = batchProgress.Values.Sum(p => p.CompletedCount);
                            var succeeded = batchProgress.Values.Sum(p => p.SucceededCount);
                            var failed = batchProgress.Values.Sum(p => p.FailedCount);
                            var cancelled = batchProgress.Values.Sum(p => p.CancelledCount);
                            var inProgress = Math.Max(knownValid - completed, 0);
                            var elapsed = aggregateStopwatch.Elapsed;

                            var aggregateProgressText =
                                $"Batch-demo polling [{elapsed:mm\\:ss}] (polling every 15 seconds): {completed}/{totalRequestedVmCount} completed (known-valid: {knownValid}, succeeded: {succeeded}, failed: {failed}, cancelled: {cancelled}, in-progress: {inProgress}).";
                            RenderAggregateProgressLine(aggregateProgressText, ref aggregateProgressLength);
                        }
                    });

                lock (failedOperationsLock)
                {
                    failedOperations.AddRange(summary.FailedOperations);
                }

                Interlocked.Add(ref totalValid, summary.ValidCount);
                Interlocked.Add(ref totalCompleted, summary.CompletedCount);
                Interlocked.Add(ref totalSucceeded, summary.SucceededCount);
                Interlocked.Add(ref totalFailed, summary.FailedCount);
                Interlocked.Add(ref totalCancelled, summary.CancelledCount);
            }
            catch (RequestFailedException ex)
            {
                Interlocked.Increment(ref batchRequestFailures);
                lock (failedOperationsLock)
                {
                    failedOperations.Add(new HelperMethods.FailedVmOperation(
                        OperationId: $"batch-{batchIndex}",
                        ResourceId: "batch-request",
                        State: "RequestFailed",
                        ErrorCode: ex.ErrorCode ?? "Unknown",
                        ErrorDetails: ex.Message));
                }

                Console.WriteLine($"Batch {batchIndex + 1}/{batchSizes.Count} request failed with ErrorCode:{ex.ErrorCode} and ErrorMessage:{ex.Message}");
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref batchRequestFailures);
                lock (failedOperationsLock)
                {
                    failedOperations.Add(new HelperMethods.FailedVmOperation(
                        OperationId: $"batch-{batchIndex}",
                        ResourceId: "batch-request",
                        State: "Exception",
                        ErrorCode: "UnhandledException",
                        ErrorDetails: ex.Message));
                }

                Console.WriteLine($"Batch {batchIndex + 1}/{batchSizes.Count} failed with Exception:{ex.Message}");
            }
            finally
            {
                concurrencyGate.Release();
            }
        })).ToList();

        await Task.WhenAll(batchTasks);
        CompleteAggregateProgressLine(aggregateProgressLength);

        Console.WriteLine(
            $"Combined final status: requested={totalRequestedVmCount}, valid={totalValid}, completed={totalCompleted}, succeeded={totalSucceeded}, failed={totalFailed}, cancelled={totalCancelled}, batchRequestFailures={batchRequestFailures}.");

        if (failedOperations.Count > 0)
        {
            Console.WriteLine("Failed VM operations across all batches:");
            foreach (var failedOperation in failedOperations)
            {
                Console.WriteLine(
                    $"- resourceId={failedOperation.ResourceId}, state={failedOperation.State}, errorCode={failedOperation.ErrorCode}, errorDetails={failedOperation.ErrorDetails}");
            }
        }
        else
        {
            Console.WriteLine("All batch requests completed without VM operation failures.");
        }
    }

    private static void RenderAggregateProgressLine(string message, ref int lastLength)
    {
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(message);
            return;
        }

        var paddedMessage = message.PadRight(Math.Max(message.Length, lastLength));
        Console.Write($"\r{paddedMessage}");
        lastLength = paddedMessage.Length;
    }

    private static void CompleteAggregateProgressLine(int lastLength)
    {
        if (lastLength > 0 && !Console.IsOutputRedirected)
        {
            Console.WriteLine();
        }
    }

    private static List<int> BuildBatchSizes(int totalVmCount, int maxPerRequest)
    {
        var batchSizes = new List<int>();
        var remaining = totalVmCount;

        while (remaining > 0)
        {
            var currentBatchSize = Math.Min(remaining, maxPerRequest);
            batchSizes.Add(currentBatchSize);
            remaining -= currentBatchSize;
        }

        return batchSizes;
    }
}
