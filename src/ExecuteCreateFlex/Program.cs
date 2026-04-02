namespace ExecuteCreateFlex;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var resourceCountOverride = TryParseResourceCount(args);
        var runBatchDemo = args.Contains("--batch-demo", StringComparer.OrdinalIgnoreCase)
            || args.Contains("--batch-request-demo", StringComparer.OrdinalIgnoreCase);
        var runApiDemo = args.Contains("--api-demo", StringComparer.OrdinalIgnoreCase);

        if (runBatchDemo && runApiDemo)
        {
            Console.WriteLine("Please choose only one demo mode: --api-demo or --batch-demo.");
            return;
        }

        if (runBatchDemo)
        {
            await ExecuteCreateFlexBatchDemo.RunAsync(resourceCountOverride);
            return;
        }

        if (!runApiDemo)
        {
            Console.WriteLine("Please choose a demo mode: --api-demo or --batch-demo.");
            Console.WriteLine("Examples:");
            Console.WriteLine("  dotnet run -- --api-demo --resource-count 5");
            Console.WriteLine("  dotnet run -- --batch-demo --resource-count 1000");
            return;
        }

        await ExecuteCreateFlexApiDemo.RunAsync(resourceCountOverride);
    }

    private static int? TryParseResourceCount(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--resource-count", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException("Missing value for --resource-count");
            }

            if (!int.TryParse(args[i + 1], out var parsedValue) || parsedValue <= 0)
            {
                throw new ArgumentException("--resource-count must be a positive integer");
            }

            return parsedValue;
        }

        return null;
    }
}

