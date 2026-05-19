using ExecuteVDICreateFlex.Scenarios;

namespace ExecuteVDICreateFlex;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Starting ExecuteVDICreateFlex sample.");

        var resourceCountOverride = TryParseResourceCount(args);
        var runBatchDemo = args.Contains("--batch-demo", StringComparer.OrdinalIgnoreCase)
            || args.Contains("--batch-request-demo", StringComparer.OrdinalIgnoreCase);
        var runApiDemo = args.Contains("--api-demo", StringComparer.OrdinalIgnoreCase);
        var listScenarios = args.Contains("--list-scenarios", StringComparer.OrdinalIgnoreCase);
        var scenarioNumber = TryParseScenario(args);
        var executeScenario = args.Contains("--execute", StringComparer.OrdinalIgnoreCase);
        var logger = CreateLogger(args);

        if (logger.IsEnabled)
        {
            Console.WriteLine($"Log file: {logger.Path}");
            logger.Info("Starting ExecuteVDICreateFlex sample.");
            logger.Info($"Arguments: {string.Join(" ", args)}");
        }

        if (resourceCountOverride.HasValue)
        {
            Console.WriteLine($"Requested resource count override: {resourceCountOverride.Value}.");
            logger.Info($"Requested resource count override: {resourceCountOverride.Value}.");
        }

        try
        {
            var selectedModeCount = Convert.ToInt32(runBatchDemo) + Convert.ToInt32(runApiDemo) + Convert.ToInt32(listScenarios) + Convert.ToInt32(scenarioNumber.HasValue);
            if (selectedModeCount > 1)
            {
                Console.WriteLine("Please choose only one demo mode: --api-demo, --batch-demo, --list-scenarios, or --scenario <n>.");
                logger.Warning("Multiple demo modes were selected.");
                return;
            }

            if (listScenarios)
            {
                PrintScenarioList();
                logger.Info("Listed CreateFlex scenarios.");
                return;
            }

            if (runBatchDemo)
            {
                Console.WriteLine("Running batch demo.");
                logger.Info("Running batch demo.");
                await ExecuteVDICreateFlexBatchDemo.RunAsync(resourceCountOverride, logger);
                return;
            }

            if (scenarioNumber.HasValue)
            {
                Console.WriteLine(executeScenario ? "Running scenario demo." : "Previewing scenario request.");
                logger.Info(executeScenario ? "Running scenario demo." : "Previewing scenario request.");
                await CreateFlexScenarioRunner.RunAsync(scenarioNumber.Value, resourceCountOverride, executeScenario, logger);
                return;
            }

            if (!runApiDemo)
            {
                Console.WriteLine("Please choose a demo mode: --api-demo, --batch-demo, --list-scenarios, or --scenario <n>.");
                Console.WriteLine("Examples:");
                Console.WriteLine("  dotnet run -- --api-demo --resource-count 5");
                Console.WriteLine("  dotnet run -- --batch-demo --resource-count 1000");
                Console.WriteLine("  dotnet run -- --list-scenarios");
                Console.WriteLine("  dotnet run -- --scenario 12");
                Console.WriteLine("  dotnet run -- --scenario 12 --execute");
                Console.WriteLine("  dotnet run -- --scenario 12 --log-file .\\logs\\scenario-12.log");
                Console.WriteLine("  dotnet run -- --scenario 12 --no-log-file");
                logger.Warning("No demo mode selected.");
                return;
            }

            Console.WriteLine("Running API demo.");
            logger.Info("Running API demo.");
            await ExecuteVDICreateFlexApiDemo.RunAsync(resourceCountOverride, logger);
        }
        catch (Exception ex)
        {
            logger.Exception(ex, "Unhandled ExecuteVDICreateFlex exception");
            throw;
        }
    }

    private static void PrintScenarioList()
    {
        Console.WriteLine("CreateFlex scenarios:");
        foreach (var scenario in CreateFlexScenarioCatalog.All)
        {
            Console.WriteLine();
            Console.WriteLine($"Scenario {scenario.Number}: {scenario.Name}");
            Console.WriteLine($"  VM sizes: {string.Join(", ", scenario.VmSizeNames)}");
            Console.WriteLine($"  VM size ranks: {(scenario.IncludeVmSizeRanks ? "included" : "not included")}");
            Console.WriteLine($"  Priority: type={scenario.PriorityType}, allocationStrategy={scenario.AllocationStrategy}");
            Console.WriteLine($"  OS type: {scenario.OsType}");
            Console.WriteLine($"  Zones: {(scenario.Zones.Count == 0 ? "regional" : string.Join(", ", scenario.Zones))}");

            if (scenario.HasZoneAllocationPolicy)
            {
                Console.WriteLine($"  Zone allocation: distributionStrategy={scenario.ZoneDistributionStrategy}");
                Console.WriteLine($"  Zone preferences: {FormatZonePreferences(scenario)}");
            }

            if (scenario.IsSpot)
            {
                Console.WriteLine($"  Spot: evictionPolicy={scenario.SpotEvictionPolicy ?? "<not set>"}, maxPricePerVM={scenario.SpotMaxPricePerVm?.ToString() ?? "<not set>"}");
            }
        }
    }

    private static string FormatZonePreferences(CreateFlexScenarioDefinition scenario)
    {
        if (scenario.ZonePreferences is null || scenario.ZonePreferences.Count == 0)
        {
            return "<none>";
        }

        return string.Join(", ", scenario.ZonePreferences.Select(item => $"zone {item.Zone}: rank {item.Rank}"));
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

    private static int? TryParseScenario(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--scenario", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException("Missing value for --scenario");
            }

            if (!int.TryParse(args[i + 1], out var parsedValue) || parsedValue <= 0)
            {
                throw new ArgumentException("--scenario must be a positive integer");
            }

            return parsedValue;
        }

        return null;
    }

    private static FlexRunLogger CreateLogger(string[] args)
    {
        if (args.Contains("--no-log-file", StringComparer.OrdinalIgnoreCase))
        {
            return FlexRunLogger.Disabled;
        }

        var logFile = TryParseStringOption(args, "--log-file");
        return logFile is null ? FlexRunLogger.CreateDefault() : FlexRunLogger.Create(logFile);
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

