namespace ExecuteCreateFlex;

internal static class FinalStatusConsoleWriter
{
    public static void WriteApiStatus(string message, int validCount, int completedCount, int failedCount, int cancelledCount)
    {
        Write(message, GetApiStatusColor(validCount, completedCount, failedCount, cancelledCount));
    }

    public static void WriteBatchStatus(string message, int requestedCount, int completedCount, int failedCount, int cancelledCount, int batchRequestFailures)
    {
        Write(message, GetBatchStatusColor(requestedCount, completedCount, failedCount, cancelledCount, batchRequestFailures));
    }

    private static void Write(string message, ConsoleColor color)
    {
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(message);
            return;
        }

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = originalColor;
    }

    private static ConsoleColor GetApiStatusColor(int validCount, int completedCount, int failedCount, int cancelledCount)
    {
        if (failedCount > 0)
        {
            return ConsoleColor.Red;
        }

        if (cancelledCount > 0 || completedCount < validCount)
        {
            return ConsoleColor.Yellow;
        }

        return ConsoleColor.Green;
    }

    private static ConsoleColor GetBatchStatusColor(int requestedCount, int completedCount, int failedCount, int cancelledCount, int batchRequestFailures)
    {
        if (failedCount > 0 || batchRequestFailures > 0)
        {
            return ConsoleColor.Red;
        }

        if (cancelledCount > 0 || completedCount < requestedCount)
        {
            return ConsoleColor.Yellow;
        }

        return ConsoleColor.Green;
    }
}