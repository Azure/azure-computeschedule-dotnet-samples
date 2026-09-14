using Azure.Core;
using Azure.ResourceManager.Compute.BulkActions;
using Azure.ResourceManager.Compute.BulkActions.Models;

namespace BulkCreateCustom;

internal static class DeleteTargets
{
    public static async Task<ResourceIdentifier[]> DiscoverAndValidateAsync(LocationBasedBulkCreateCustomResource source,
        DemoConfig config, ResourceIdentifier id, CancellationToken cancellationToken)
    {
        var data = (await source.GetAsync(cancellationToken)).Value.Data;
        var expected = ValidateSource(config, id, data);
        var creations = new List<ComputeBulkOperationResult>();
        await foreach (var item in source.VirtualMachinesGetOperationStatusAsync(cancellationToken))
            creations.Add(item);
        var targets = ValidateCreations(expected, creations);
        // Re-read the source after enumeration; do not delete if the operation changed meanwhile.
        var latest = (await source.GetAsync(cancellationToken)).Value.Data;
        if (!expected.SetEquals(ValidateSource(config, id, latest)))
            throw new InvalidDataException("Batch B targets changed during discovery.");
        return targets;
    }

    public static void PrintPreview(DemoLog log, ResourceIdentifier[] targets)
    {
        log.Write("Deleting VMs only. Batch A, the resource group/network and the source operation record are not deletion targets.");
        log.Write("Disk/NIC deletion follows existing VM delete options; retained dependencies are not explicitly cleaned up.");
        // Keep destructive-operation targets visible even when verbose logging is disabled.
        foreach (var target in targets) log.Write($"Delete target: {target}");
    }

    internal static HashSet<string> ValidateSource(DemoConfig config, ResourceIdentifier id, LocationBasedBulkCreateCustomData data)
    {
        var p = data.Properties;
        if (data.Id != id || !data.Tags.TryGetValue("sample", out var sample) || sample != "BulkCreateCustom" ||
            !data.Tags.TryGetValue("batch", out var batch) || batch != "b" || p is null ||
            p.Capacity != 50 || p.CapacityType != CapacityType.VM || p.ProvisioningState != BulkInstancesOperationProvisioningState.Succeeded)
            throw new InvalidDataException("Select a succeeded 50-VM Batch B operation in the configured scope.");
        var prefix = $"/subscriptions/{config.SubscriptionId}/resourceGroups/{config.ResourceGroup}/providers/Microsoft.Compute/virtualMachines/";
        HashSet<string> ValidateNames(string?[]? names)
        {
            if (names is not { Length: 50 } || names.Any(name => string.IsNullOrWhiteSpace(name) ||
                !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z][a-z0-9-]{0,11}-[0-9a-f]{32}-b-\d{3}$")))
                throw new InvalidDataException("Batch B must contain exactly 50 explicit demo VM names.");
            var ids = names.Select(name => prefix + name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (ids.Count != 50) throw new InvalidDataException("Batch B VM names are duplicated.");
            return ids;
        }

        // Overrides are create/update-only in the contract. GET exposes resolved resources instead.
        var overrides = p.OverridesProfile is null ? null
            : ValidateNames(p.OverridesProfile.Overrides.Select(item => item.VirtualMachineName).ToArray());
        if (p.Resources.Count > 0)
        {
            var resolved = ValidateNames(p.Resources.Select(item => item.VirtualMachineInfo?.Name).ToArray());
            if (overrides is not null && !resolved.SetEquals(overrides))
                throw new InvalidDataException("Batch B resolved resources disagree with the expected VM names.");
            return resolved;
        }
        return overrides ?? throw new InvalidDataException("Batch B returned no complete VM identity set.");
    }

    internal static ResourceIdentifier[] ValidateCreations(HashSet<string> expected, IReadOnlyList<ComputeBulkOperationResult> results)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (results.Count != 50) throw new InvalidDataException("All 50 Batch B creation results are required; retry preview after completion.");
        foreach (var result in results)
        {
            var id = result.ResourceId?.ToString();
            if (id is null || !expected.Contains(id) || !seen.Add(id) || !DeleteExecution.IsSucceeded(result) ||
                result.Operation?.OperationKind != ComputeBulkOperationKind.Create ||
                (result.Operation.ResourceId is { } inner && !string.Equals(inner.ToString(), id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Creation results must be unique, in-scope, successful Create operations with no errors.");
        }
        return results.Select(result => result.ResourceId).ToArray();
    }
}
