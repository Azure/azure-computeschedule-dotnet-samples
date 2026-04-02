namespace UtilityMethods;

internal static class ConsoleProgressRenderer
{
    private static readonly object s_consoleProgressLock = new();

    internal static void RenderSingleLineProgress(string message, ref int lastProgressLength)
    {
        lock (s_consoleProgressLock)
        {
            if (Console.IsOutputRedirected)
            {
                Console.WriteLine(message);
                return;
            }

            var paddedMessage = message.PadRight(Math.Max(message.Length, lastProgressLength));
            Console.Write($"\r{paddedMessage}");
            lastProgressLength = paddedMessage.Length;
        }
    }

    internal static void CompleteSingleLineProgress(int lastProgressLength)
    {
        if (lastProgressLength <= 0 || Console.IsOutputRedirected)
        {
            return;
        }

        lock (s_consoleProgressLock)
        {
            Console.WriteLine();
        }
    }
}
