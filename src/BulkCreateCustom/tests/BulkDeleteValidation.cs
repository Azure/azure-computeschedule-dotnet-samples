using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute.BulkActions;
using Azure.ResourceManager.Compute.BulkActions.Models;

namespace BulkCreateCustom;

internal static class BulkDeleteValidation
{
    private const string OperationName = "22222222-2222-2222-2222-222222222222";
    private const string RunId = "33333333333333333333333333333333";
    private static readonly DemoConfig Config = new()
    {
        SubscriptionId = "11111111-1111-1111-1111-111111111111",
        ResourceGroup = "offline-rg", Region = "eastus"
    };
    private static readonly string LocationPath =
        $"/subscriptions/{Config.SubscriptionId}/resourceGroups/{Config.ResourceGroup}/providers/Microsoft.Compute/locations/{Config.Region}";
    private static readonly string SourcePath = LocationPath + "/bulkCreateCustom/" + OperationName;
    private static readonly string[] Targets = Enumerable.Range(0, 50).Select(i =>
        $"/subscriptions/{Config.SubscriptionId}/resourceGroups/{Config.ResourceGroup}/providers/Microsoft.Compute/virtualMachines/offline-{RunId}-b-{i:000}").ToArray();
    private static readonly string[] DeleteIds = Enumerable.Range(1, 50)
        .Select(i => $"44444444-4444-4444-4444-{i:000000000000}").ToArray();

    public static async Task RunAsync()
    {
        ValidateArguments();
        ValidateFixtures();
        await ScenarioAsync("preview", execute: false, expectedExit: 0);
        await ScenarioAsync("resolved-only", execute: false, expectedExit: 0);
        foreach (var scenario in new[] { "success", "immediate-success", "pending", "transient-poll", "resolved-only", "resolved-with-overrides" })
            await ScenarioAsync(scenario, execute: true, expectedExit: 0);
        foreach (var scenario in new[]
        {
            "wrong-batch", "source-failed", "source-changed", "source-resolved-partial", "source-resolved-conflict",
            "source-resolved-duplicate", "source-resolved-invalid", "source-no-identities",
            "creation-missing", "creation-duplicate", "creation-failed", "creation-wrong-batch",
            "creation-missing-id", "creation-malformed-id", "creation-wrong-kind",
            "partial-accept", "delete-failed", "delete-cancelled", "delete-missing-id",
            "delete-missing-target", "delete-malformed-target", "delete-duplicate-id",
            "delete-duplicate-target", "delete-wrong-kind", "delete-missing-kind", "success-missing-id", "poll-wrong-id", "poll-wrong-target",
            "poll-wrong-kind", "cancellation"
        })
            await ScenarioAsync(scenario, execute: true, expectedExit: 1);
        Console.WriteLine("Offline Bulk Delete validation passed: preview, exact 50-VM scope, pageable Create discovery, " +
            "delete payload/receipts, partial results, polling, failures and cancellation. No Azure calls.");
    }

    private static void ValidateArguments()
    {
        var preview = DeleteCommand.Parse(["--delete-batch-b", OperationName]);
        Require(preview.OperationName == OperationName && !preview.Execute, "delete defaults to preview");
        var execute = DeleteCommand.Parse(["--execute", "--config", "offline-config.json", "--delete-batch-b", OperationName]);
        Require(execute.Execute && execute.ConfigPath == Path.GetFullPath("offline-config.json"), "explicit execute/config");
        Require(DeleteCommand.Parse(["--delete-batch-b", OperationName, "--verbose"]).Verbose,
            "delete verbose output is explicit");
        string[][] invalid =
        [
            [], ["--execute"], ["--delete-batch-b"], ["--delete-batch-b", Guid.Empty.ToString()],
            ["--delete-batch-b", "not-a-uuid"], ["--delete-batch-b", SourcePath],
            ["--delete-batch-b", OperationName, "--validate"],
            ["--delete-batch-b", OperationName, "--batch-a-only"],
            ["--delete-batch-b", OperationName, "--unknown"],
            ["--delete-batch-b", OperationName, "extra"],
            ["--delete-batch-b", OperationName, "--execute", "--execute"],
            ["--delete-batch-b", OperationName, "--verbose", "--verbose"],
            ["--delete-batch-b", OperationName, "--delete-batch-b", OperationName],
            ["--delete-batch-b", OperationName, "--config"],
            ["--delete-batch-b", OperationName, "--config", "--execute"],
            ["--delete-batch-b", OperationName, "--config", " "],
            ["--delete-batch-b", OperationName, "--config", "one.json", "--config", "two.json"]
        ];
        foreach (var args in invalid)
            Reject<ArgumentException>(() => DeleteCommand.Parse(args), "mixed or malformed delete arguments");
    }

    private static void ValidateFixtures()
    {
        var source = Read<LocationBasedBulkCreateCustomData>(SourceFixture());
        var expected = BulkDeleteDemo.ValidateSource(Config, new ResourceIdentifier(SourcePath), source);
        Require(expected.SetEquals(Targets), "deserialized tagged 50-VM source");
        var creations = Enumerable.Range(0, 50).Select(i => Read<ComputeBulkOperationResult>(Result(i, "Create", "Succeeded"))).ToArray();
        Require(creations.All(r => r.Operation.OperationKind == ComputeBulkOperationKind.Create),
            "SDK 1.2.0-beta.2 deserializes operation.opType as Create");
        Require(BulkDeleteDemo.ValidateCreations(expected, creations).Select(id => id.ToString()).ToHashSet().SetEquals(Targets),
            "creation results resolve exact fixture identities");
        foreach (var change in new Action<JsonObject>[]
        {
            node => node["tags"]!["sample"] = "another-sample",
            node => node["tags"]!["batch"] = "a",
            node => node["properties"]!["capacity"] = 49,
            node => node["properties"]!["capacityType"] = "vCPU",
            node => node["properties"]!["provisioningState"] = "Failed",
            node => node["properties"]!["overridesProfile"]!["overrides"]!.AsArray().RemoveAt(0),
            node => node["properties"]!["overridesProfile"]!["overrides"]![0]!["virtualMachineName"] = "invalid/name",
            node => node["properties"]!["overridesProfile"]!["overrides"]![0]!["virtualMachineName"] = Targets[1].Split('/')[^1],
            node => node["id"] = SourcePath.Replace(Config.ResourceGroup, "another-rg", StringComparison.Ordinal)
        })
        {
            var fixture = SourceFixture();
            change(fixture);
            Reject<InvalidDataException>(() => BulkDeleteDemo.ValidateSource(Config,
                new ResourceIdentifier(SourcePath), Read<LocationBasedBulkCreateCustomData>(fixture)), "unsafe source fixture");
        }
    }

    private static async Task ScenarioAsync(string scenario, bool execute, int expectedExit)
    {
        var receiptPath = Path.Combine(Path.GetTempPath(),
            $"offline-delete-validation-{Guid.NewGuid():N}.receipt.jsonl");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new OfflineHandler(scenario, receiptPath, cancellation);
        using var http = new HttpClient(handler);
        var options = new ArmClientOptions { Transport = new HttpClientTransport(http) };
        options.Retry.MaxRetries = 0;
        options.Diagnostics.IsLoggingContentEnabled = false;
        var client = new ArmClient(new OfflineCredential(), Config.SubscriptionId, options);
        using var output = new StringWriter();
        try
        {
            var result = await BulkDeleteDemo.RunAsync(client, Config, OperationName, execute,
                new DemoLog(output, null), receiptPath, TimeSpan.FromMilliseconds(1), cancellation.Token);
            Require(result == expectedExit, $"{scenario} exit (actual {result}): {output}");
            var unsafeDiscovery = scenario.StartsWith("creation-", StringComparison.Ordinal)
                || scenario == "wrong-batch" || scenario.StartsWith("source-", StringComparison.Ordinal);
            var submits = execute && !unsafeDiscovery;
            Require(handler.DeleteCount == (submits ? 1 : 0), $"{scenario}: exactly one submission or none");
            Require(File.Exists(receiptPath) == submits, $"{scenario}: receipt only for execute after validation");
            if (!unsafeDiscovery)
            {
                Require(handler.SourceReads == 2 && handler.CreationPages == 2, $"{scenario}: source reread and full pagination");
                Require(Targets.All(id => output.ToString().Contains($"Delete target: {id}")), $"{scenario}: exact targets printed");
            }
            if (submits)
            {
                var lines = File.ReadAllLines(receiptPath);
                Require(lines.Length >= 2, $"{scenario}: durable NDJSON checkpoints");
                foreach (var line in lines)
                {
                    using var json = JsonDocument.Parse(line);
                    Require(json.RootElement.GetProperty("sourceOperation").GetString() == SourcePath, "receipt source");
                    Require(json.RootElement.GetProperty("targetIds").EnumerateArray().Select(x => x.GetString()!)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(Targets), "receipt exact scope");
                }
                using var first = JsonDocument.Parse(lines[0]);
                Require(first.RootElement.GetProperty("state").GetString() == "Prepared", "receipt prepared before mutation");
                using var last = JsonDocument.Parse(lines[^1]);
                Require((last.RootElement.GetProperty("state").GetString() == "Succeeded") == (expectedExit == 0),
                    $"{scenario}: receipt cannot claim false success");
                Require(scenario == "immediate-success" ? handler.PollCount == 0 : handler.PollCount > 0,
                    $"{scenario}: only nonterminal deletion operations are polled");
            }
            if (scenario == "pending")
                Require(handler.PollCount == 2 && output.ToString().Contains("pending=50"), "pending observations become success");
            if (scenario == "transient-poll")
                Require(handler.PollCount == 2 && output.ToString().Contains("HTTP=503"), "transient polling retries observation only");
            if (scenario == "cancellation")
                Require(output.ToString().Contains("does not cancel Azure deletion"), "local timeout does not cancel remote deletion");
        }
        finally { File.Delete(receiptPath); }
    }

    private static JsonObject SourceFixture() => JsonSerializer.SerializeToNode(new
    {
        id = SourcePath, name = OperationName, type = "Microsoft.Compute/locations/bulkCreateCustom",
        location = Config.Region, tags = new { sample = "BulkCreateCustom", batch = "b" },
        properties = new
        {
            capacity = 50, capacityType = "VM", provisioningState = "Succeeded",
            overridesProfile = new
            {
                overrides = Targets.Select(id => new { virtualMachineName = id.Split('/')[^1] }).ToArray()
            }
        }
    })!.AsObject();

    private static JsonObject Result(int index, string kind, string state) => new()
    {
        ["resourceId"] = Targets[index],
        ["operation"] = new JsonObject
        {
            ["operationId"] = kind == "Create" ? $"create-{index}" : DeleteIds[index],
            ["resourceId"] = Targets[index], ["opType"] = kind, ["state"] = state
        }
    };

    private static T Read<T>(JsonNode fixture) where T : class =>
        ModelReaderWriter.Read<T>(BinaryData.FromString(fixture.ToJsonString()))!;

    private static void Reject<T>(Action action, string label) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Offline Bulk Delete validation failed: {label} was accepted.");
    }

    private static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException($"Offline Bulk Delete validation failed: {label}");
    }

    private sealed class OfflineCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("offline-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    // Routes and wire fields verified against the pinned package's repository commit
    // 470fcf36991d2b32caa7ee434c54d1a518b9c3fd, Generated/RestOperations/{BulkCreateCustom,VirtualMachineBulkOperations}RestOperations.cs.
    // This terminal handler has no inner handler: even an unexpected request cannot reach Azure.
    private sealed class OfflineHandler(string scenario, string receiptPath, CancellationTokenSource cancellation) : HttpMessageHandler
    {
        public int SourceReads { get; private set; }
        public int CreationPages { get; private set; }
        public int DeleteCount { get; private set; }
        public int PollCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing offline URI.");
            Require(uri.Host == "management.azure.com" && uri.Query.Contains("api-version=2026-08-06-preview"),
                "pinned SDK endpoint/version");
            Require(request.Method != HttpMethod.Put && request.Method != HttpMethod.Delete,
                "no create PUT, VM/group/network/custom-record DELETE, or other cleanup");
            if (request.Method == HttpMethod.Get && uri.AbsolutePath == SourcePath)
            {
                SourceReads++;
                var source = SourceFixture();
                if (scenario == "wrong-batch" || scenario == "source-changed" && SourceReads == 2)
                    source["tags"]!["batch"] = "a";
                if (scenario == "source-failed") source["properties"]!["provisioningState"] = "Failed";
                if (scenario.StartsWith("source-resolved-", StringComparison.Ordinal) ||
                    scenario is "resolved-only" or "resolved-with-overrides")
                {
                    var resolved = new JsonArray(Targets.Select(target => (JsonNode)new JsonObject
                    {
                        ["virtualMachineInfo"] = new JsonObject { ["name"] = target.Split('/')[^1] }
                    }).ToArray());
                    source["properties"]!["resources"] = resolved;
                    if (scenario == "resolved-only") source["properties"]!.AsObject().Remove("overridesProfile");
                    if (scenario == "source-resolved-partial") resolved.RemoveAt(0);
                    if (scenario == "source-resolved-conflict") resolved[0]!["virtualMachineInfo"]!["name"] = $"offline-{RunId}-b-999";
                    if (scenario == "source-resolved-duplicate") resolved[0]!["virtualMachineInfo"]!["name"] = Targets[1].Split('/')[^1];
                    if (scenario == "source-resolved-invalid") resolved[0]!["virtualMachineInfo"]!["name"] = "invalid/name";
                }
                if (scenario == "source-no-identities") source["properties"]!.AsObject().Remove("overridesProfile");
                return Reply(source);
            }
            Require(request.Method == HttpMethod.Post, "only source GET and allowlisted action POST");
            if (uri.AbsolutePath == SourcePath + "/virtualMachinesGetOperationStatus")
            {
                CreationPages++;
                var pageTwo = uri.Query.Contains("page=2");
                var results = Enumerable.Range(pageTwo ? 25 : 0, 25).Select(i => Result(i, "Create", "Succeeded")).ToList();
                if (pageTwo)
                {
                    switch (scenario)
                    {
                        case "creation-missing": results.RemoveAt(0); break;
                        case "creation-duplicate": results[0] = Result(0, "Create", "Succeeded"); break;
                        case "creation-failed": results[0]["operation"]!["state"] = "Failed"; break;
                        case "creation-wrong-batch": results[0]["resourceId"] = Targets[25].Replace("-b-", "-a-"); break;
                        case "creation-missing-id": results[0].Remove("resourceId"); break;
                        case "creation-malformed-id": results[0]["resourceId"] = "/subscriptions/invalid"; break;
                        case "creation-wrong-kind": results[0]["operation"]!["opType"] = "Delete"; break;
                    }
                }
                return Reply(new
                {
                    results,
                    nextLink = pageTwo ? null : $"https://management.azure.com{uri.AbsolutePath}?page=2&api-version=2026-08-06-preview"
                });
            }
            if (uri.AbsolutePath == LocationPath + "/virtualMachinesBulkDelete")
            {
                DeleteCount++;
                Require(DeleteCount == 1 && SourceReads == 2 && CreationPages == 2, "validated discovery before single delete");
                Require(File.Exists(receiptPath) && new FileInfo(receiptPath).Length > 0, "receipt flushed before delete POST");
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var payload = json.RootElement;
                var ids = payload.GetProperty("resources").GetProperty("ids").EnumerateArray().Select(x => x.GetString()!).ToArray();
                Require(ids.Length == 50 && ids.Distinct().Count() == 50 && ids.ToHashSet().SetEquals(Targets),
                    "ExecuteDeleteContent exact 50 Batch B VM IDs, no other targets");
                Require(!payload.GetProperty("forceDeletion").GetBoolean(), "force deletion explicitly false");
                var retry = payload.GetProperty("executionParameters").GetProperty("retryPolicy");
                Require(retry.GetProperty("retryWindowInMinutes").GetInt32() == 5
                    && !retry.TryGetProperty("retryCount", out _), "five-minute retry window without retry count");
                var results = Enumerable.Range(0, scenario == "partial-accept" ? 49 : 50)
                    .Select(i => Result(i, "Delete", scenario == "immediate-success" ? "Succeeded" : "Executing")).ToList();
                switch (scenario)
                {
                    case "delete-failed": results[0]["operation"]!["state"] = "Failed"; break;
                    case "delete-cancelled": results[0]["operation"]!["state"] = "Cancelled"; break;
                    case "delete-missing-id": results[0]["operation"]!.AsObject().Remove("operationId"); break;
                    case "delete-missing-target": results[0].Remove("resourceId"); break;
                    case "delete-malformed-target": results[0]["resourceId"] = "/subscriptions/invalid"; break;
                    case "delete-duplicate-id": results[0]["operation"]!["operationId"] = DeleteIds[1]; break;
                    case "delete-duplicate-target": results[0] = Result(1, "Delete", "Executing"); break;
                    case "delete-wrong-kind": results[0]["operation"]!["opType"] = "Create"; break;
                    case "delete-missing-kind": results[0]["operation"]!.AsObject().Remove("opType"); break;
                    case "success-missing-id":
                        results[0]["operation"]!["state"] = "Succeeded";
                        results[0]["operation"]!.AsObject().Remove("operationId");
                        break;
                }
                return Reply(new { results });
            }
            Require(uri.AbsolutePath == LocationPath + "/virtualMachinesBulkGetOperationStatus",
                "no group deletion, source deletion, unrelated actions, or other resource operations");
            PollCount++;
            Require(DeleteCount == 1, "poll only after delete acceptance");
            using var pollJson = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var requested = pollJson.RootElement.GetProperty("operationIds").EnumerateArray().Select(x => x.GetString()!).ToArray();
            Require(requested.Length > 0 && requested.Distinct().Count() == requested.Length
                && requested.All(DeleteIds.Contains), "poll returned deletion IDs, never source/Create operation IDs");
            if (scenario == "cancellation")
            {
                cancellation.CancelAfter(TimeSpan.FromMilliseconds(25));
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            if (scenario == "transient-poll" && PollCount == 1)
                return Reply(new { error = new { code = "ServiceUnavailable" } }, HttpStatusCode.ServiceUnavailable);
            var statuses = requested.Select(id => Result(Array.IndexOf(DeleteIds, id), "Delete",
                scenario == "pending" && PollCount == 1 ? "Executing" : "Succeeded")).ToList();
            if (PollCount == 1)
            {
                if (scenario == "poll-wrong-id") statuses[0]["operation"]!["operationId"] = OperationName;
                if (scenario == "poll-wrong-target") statuses[0]["resourceId"] = Targets[1];
                if (scenario == "poll-wrong-kind") statuses[0]["operation"]!["opType"] = "Create";
            }
            return Reply(new { results = statuses });
        }

        private static HttpResponseMessage Reply(object payload, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
    }
}
