using System.Text.Json;
using Azure.Identity;
using Azure.ResourceManager;

namespace BulkCreateCustom;

internal sealed record DeleteCommand(string OperationName, string ConfigPath, bool Execute, bool Verbose = false)
{
    public static DeleteCommand Parse(string[] args)
    {
        string? operation = null;
        string? config = null;
        var execute = false;
        var verbose = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--delete-batch-b" when operation is null && i + 1 < args.Length:
                    operation = args[++i];
                    break;
                case "--config" when config is null && i + 1 < args.Length:
                    config = args[++i];
                    if (string.IsNullOrWhiteSpace(config) || config.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException("Supply a file path after --config.");
                    break;
                case "--execute" when !execute:
                    execute = true;
                    break;
                case "--verbose" when !verbose:
                    verbose = true;
                    break;
                default:
                    throw new ArgumentException("Usage: --delete-batch-b <bulk-resource-UUID> [--config <path>] [--execute] [--verbose].");
            }
        }
        if (!Guid.TryParseExact(operation, "D", out var id) || id == Guid.Empty)
            throw new ArgumentException("--delete-batch-b requires an explicit nonempty bulk-resource UUID, not the async-operation ID.");
        return new(id.ToString(), Path.GetFullPath(config ?? DemoConfig.DefaultPath), execute, verbose);
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var command = Parse(args);
            var config = DemoConfig.Load(command.ConfigPath);
            config.ValidateScope();
            using var output = DemoOutput.Create("bulk-delete", null, command.Verbose);
            var log = output.Log;
            log.Write($"Configuration file: {command.ConfigPath}");
            log.Write("Delete targets remain visible. Full JSON and per-VM outcomes are saved in the log; use --verbose to display them here.");
            var options = new ArmClientOptions();
            options.Retry.MaxRetries = 0;
            options.Diagnostics.IsLoggingContentEnabled = false;
            var client = new ArmClient(new DefaultAzureCredential(), config.SubscriptionId, options);
            using var cancellation = new ConsoleCancellation();
            return await BulkDeleteDemo.RunAsync(client, config, command.OperationName, command.Execute, log,
                Path.ChangeExtension(output.LogPath, ".receipt.jsonl"), TimeSpan.FromSeconds(10), cancellation.Token);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (JsonException)
        {
            Console.Error.WriteLine("Invalid configuration or Azure response JSON. No automatic resubmission.");
            return 2;
        }
        catch (IOException)
        {
            Console.Error.WriteLine("Config/log/receipt I/O failed. If deletion was submitted, inspect the existing operation IDs; do not resubmit.");
            return 2;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Config/log/receipt access denied. No automatic resubmission.");
            return 2;
        }
    }
}
