namespace ExecuteVDICreateFlex;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Starting ExecuteVDICreateFlex sample.");

        var options = ParseOptions(args);
        var logger = CreateLogger(options);

        if (logger.IsEnabled)
        {
            Console.WriteLine($"Log file: {logger.Path}");
            logger.Info("Starting ExecuteVDICreateFlex sample.");
            logger.Info($"Arguments: {string.Join(" ", args)}");
        }

        if (options.ResourceCountOverride.HasValue)
        {
            Console.WriteLine($"Requested resource count override: {options.ResourceCountOverride.Value}.");
            logger.Info($"Requested resource count override: {options.ResourceCountOverride.Value}.");
        }

        try
        {
            ValidateSampleMode(options);
            if (!options.HasSampleMode)
            {
                PrintUsage();
                logger.Warning("No sample mode selected.");
                return;
            }

            if (options.RunApiSampleWithZones)
            {
                Console.WriteLine("Running API sample with zones.");
                logger.Info("Running API sample with zones.");
                await ApiDemoWithZones.RunAsync(options.ResourceCountOverride, logger);
                return;
            }

            Console.WriteLine("Running API sample.");
            logger.Info("Running API sample.");
            await ExecuteVDICreateFlexApiDemo.RunAsync(options.ResourceCountOverride, logger);
        }
        catch (Exception ex)
        {
            logger.Exception(ex, "Unhandled ExecuteVDICreateFlex exception");
            throw;
        }
    }
    private sealed record SampleOptions(
        bool RunApiSample,
        bool RunApiSampleWithZones,
        int? ResourceCountOverride,
        string? LogFilePath,
        bool DisableLogFile)
    {
        public bool HasSampleMode => RunApiSample || RunApiSampleWithZones;
    }

    private static SampleOptions ParseOptions(string[] args) =>
        new(
            RunApiSample: args.Contains("--api-demo", StringComparer.OrdinalIgnoreCase),
            RunApiSampleWithZones: args.Contains("--api-demo-with-zones", StringComparer.OrdinalIgnoreCase),
            ResourceCountOverride: TryParseResourceCount(args),
            LogFilePath: TryParseStringOption(args, "--log-file"),
            DisableLogFile: args.Contains("--no-log-file", StringComparer.OrdinalIgnoreCase));

    private static void PrintUsage()
    {
        Console.WriteLine("Please choose one API sample mode: --api-demo or --api-demo-with-zones.");
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- --api-demo --resource-count 5");
        Console.WriteLine("  dotnet run -- --api-demo-with-zones --resource-count 5");
        Console.WriteLine("  dotnet run -- --api-demo --log-file .\\logs\\api-demo.log");
        Console.WriteLine("  dotnet run -- --api-demo --no-log-file");
    }

    private static void ValidateSampleMode(SampleOptions options)
    {
        if (options.RunApiSample && options.RunApiSampleWithZones)
        {
            throw new ArgumentException("Choose only one sample mode: --api-demo or --api-demo-with-zones.");
        }
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

    private static FlexRunLogger CreateLogger(SampleOptions options)
    {
        if (options.DisableLogFile)
        {
            return FlexRunLogger.Disabled;
        }

        return options.LogFilePath is null ? FlexRunLogger.CreateDefault() : FlexRunLogger.Create(options.LogFilePath);
    }

    private static string? TryParseStringOption(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {optionName}");
            }

            return args[i + 1];
        }

        return null;
    }
}
