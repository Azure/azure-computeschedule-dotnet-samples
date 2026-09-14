using Azure.Identity;
using Azure.ResourceManager;

namespace BulkCreateCustom;

internal static class BulkCreateDemo
{
    public static async Task<int> RunCommandAsync(CreateCommand command)
    {
        var config = DemoConfig.Load(command.ConfigPath);
        var password = config.GetAdminPassword();
        var runId = Guid.NewGuid().ToString("N");

        var batchA = BulkCreateRequestBuilder.BuildPerVmRequest(config, password, runId);
        var batchB = BulkCreateRequestBuilder.BuildPerSizeRequest(config, password, runId);

        using var output = DemoOutput.Create("bulk-create", password, command.Verbose);
        output.Log.Write("Full request JSON and per-VM details are saved in the log. Use --verbose to also display them here.");
        output.Log.Write($"Configuration file: {command.ConfigPath}");
        output.Log.Write("Creating Batch A: 100 VMs (per-VM names); Batch B: 50 VMs (per-VM names + per-size disks). No automatic cleanup.");
        var options = new ArmClientOptions();
        options.Diagnostics.IsLoggingContentEnabled = false;
        options.Retry.MaxRetries = 0; // Never replay an ambiguous create submission automatically.
        var client = new SdkBulkCreateClient(new ArmClient(new DefaultAzureCredential(), config.SubscriptionId, options), config);

        using var cancellation = new ConsoleCancellation();
        return await RunAsync(client, [batchA, batchB], output.Log,
            TimeSpan.FromMinutes(config.PollTimeoutMinutes), TimeSpan.FromSeconds(10), cancellation.Token);
    }

    public static async Task<int> RunAsync(IBulkCreateClient client, BatchRequest[] batches,
        DemoLog log, TimeSpan timeout, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        foreach (var batch in batches)
            log.WriteRequest(batch);

        // Start both submissions before awaiting completion. Each independently tracks its accepted work.
        var tasks = batches.Select(batch => CreateExecution.RunBatchAsync(
            client, batch, log, timeout, pollInterval, cancellationToken)).ToArray();
        var outcomes = await Task.WhenAll(tasks);

        log.Write($"Overall: requested={batches.Sum(batch => batch.Data.Properties.Capacity)}; " +
            $"fully successful batches={outcomes.Count(success => success)}/{batches.Length}.");
        return outcomes.All(success => success) ? 0 : 1;
    }
}
