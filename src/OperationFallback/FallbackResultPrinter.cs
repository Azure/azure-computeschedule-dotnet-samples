using Azure.ResourceManager.ComputeSchedule.Models;

namespace OperationFallback;

/// <summary>
/// Prints operation results with fallback interpretation.
///
/// When a fallback is configured, the top-level state reflects the
/// primary operation outcome. Check FallbackOperationInfo to determine
/// the actual VM state:
///   - state=Failed + fallback.Status=Succeeded → fallback recovered
///   - state=Failed + fallback.Status=Failed → both failed
///   - state=Failed + no fallback → non-retriable error (fallback skipped)
/// </summary>
public static class FallbackResultPrinter
{
    public static void Print(Dictionary<string, ResourceOperationDetails> completedOperations)
    {
        foreach (var (opId, details) in completedOperations)
        {
            Console.WriteLine($"\nOperation {opId}: State = {details.State}");

            if (details.State == ScheduledActionOperationState.Failed)
            {
                if (details.ResourceOperationError is not null)
                {
                    Console.WriteLine($"  Primary error: {details.ResourceOperationError.ErrorCode} — {details.ResourceOperationError.ErrorDetails}");
                }

                if (details.FallbackOperationInfo is not null)
                {
                    var fallback = details.FallbackOperationInfo;
                    Console.WriteLine($"  Fallback ({fallback.LastOpType}): Status = {fallback.Status}");

                    if (fallback.Status == ScheduledActionOperationState.Succeeded)
                    {
                        Console.WriteLine("  ✅ Fallback succeeded — VM is in a safe state.");
                    }
                    else
                    {
                        Console.WriteLine("  ❌ Fallback also failed. Manual intervention may be needed.");
                        Console.WriteLine($"     Check lastOpType ({fallback.LastOpType}) to see the last operation attempted.");
                        if (fallback.Error is not null)
                        {
                            Console.WriteLine($"     Error: {fallback.Error.ErrorCode} — {fallback.Error.ErrorDetails}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("  ❌ No fallback executed (may indicate a non-retriable error).");
                }
            }
            else if (details.State == ScheduledActionOperationState.Succeeded)
            {
                Console.WriteLine("  ✅ Operation succeeded — no fallback needed.");
            }
        }
    }
}
