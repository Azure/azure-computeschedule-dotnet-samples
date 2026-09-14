using System.Security.Cryptography;
using Azure.ResourceManager.Compute.BulkActions;
using Azure.ResourceManager.Compute.BulkActions.Models;

namespace BulkCreateCustom;

internal sealed record BatchRequest(string Label, string OperationName, string CorrelationId,
    LocationBasedBulkCreateCustomData Data, string VmResourceIdPrefix, IReadOnlyDictionary<string, string> ComputerNames,
    IReadOnlyDictionary<string, int> ExpectedDisks);

internal static class BulkCreateRequestBuilder
{
    public static BatchRequest BuildPerVmRequest(DemoConfig config, string password, string runId) =>
        BuildBatch(config, password, runId, "a", 100);

    public static BatchRequest BuildPerSizeRequest(DemoConfig config, string password, string runId)
    {
        var batch = BuildBatch(config, password, runId, "b", 50);
        foreach (var size in batch.Data.Properties.VmSizesProfile)
        {
            size.Override = new BulkCreateCustomOverrideBase
            {
                VirtualMachineProfile = new BulkActionVMProperties
                {
                    StorageProfile = new StorageProfile
                    {
                        OSDisk = new OSDisk(DiskCreateOptionTypes.FromImage)
                        {
                            DiskSizeGB = config.Sizes.Single(candidate => candidate.Name == size.Name).OSDiskGB
                        }
                    }
                }
            };
        }
        return batch with
        {
            ExpectedDisks = config.Sizes.ToDictionary(size => size.Name, size => size.OSDiskGB, StringComparer.OrdinalIgnoreCase)
        };
    }

    public static BatchRequest[] Build(DemoConfig config, string password, bool includePerSizeBatch = true)
    {
        var runId = Guid.NewGuid().ToString("N");
        var first = BuildPerVmRequest(config, password, runId);
        return includePerSizeBatch
            ? [first, BuildPerSizeRequest(config, password, runId)]
            : [first];
    }

    private static BatchRequest BuildBatch(DemoConfig config, string password, string runId, string label, int count)
    {
        config.Validate(password);
        if (!Guid.TryParseExact(runId, "N", out _))
            throw new ArgumentException("Run ID must be a 32-character GUID.");
        // Reserve batch and index suffixes so every Windows computer name is exactly 15 characters.
        var computerRun = RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz012345", 10);
        var properties = new BulkCreateCustomProperties(count,
            new BulkCreateCustomPriorityProfile
            {
                Type = PriorityType.Regular,
                AllocationStrategy = BulkCreateCustomAllocationStrategy.Prioritized
            },
            new ComputeProfile(BuildBaseProfile(config, password)) { ComputeApiVersion = "2024-11-01" })
        {
            CapacityType = CapacityType.VM,
            PartialFulfillmentPolicy = new PartialFulfillmentPolicy { Mode = PartialFulfillmentMode.Disabled },
            OverridesProfile = new BulkCreateCustomOverridesProfile(),
            ZoneAllocationPolicy = new BulkCreateCustomZoneAllocationPolicy
            {
                DistributionStrategy = BulkCreateCustomDistributionStrategy.BestEffortBalanced
            },
            ExecutionParameters = new BulkActionExecutionParameterDetail
            {
                RetryPolicy = new BulkOperationRetryPolicy { RetryWindowInMinutes = 5 }
            }
        };
        foreach (var size in config.Sizes.OrderBy(s => s.Rank))
        {
            properties.VmSizesProfile.Add(new BulkCreateCustomVmSizeProfile(size.Name, size.Rank));
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var name = $"{config.RunPrefix}-{runId}-{label}-{i:D3}";
            var computerName = $"{config.RunPrefix[0]}{computerRun}{label}{i:D3}";
            names.Add(name, computerName);
            // Both examples supply per-VM names. Disk settings are absent here
            // so these entries cannot mask Batch B's native per-size override.
            properties.OverridesProfile.Overrides.Add(new BulkCreateCustomOverride
            {
                VirtualMachineName = name,
                VirtualMachineProfile = new BulkActionVMProperties
                {
                    OSProfile = new OSProfile { ComputerName = computerName }
                }
            });
        }
        var correlation = Guid.NewGuid().ToString();
        var data = new LocationBasedBulkCreateCustomData { Properties = properties };
        foreach (var zone in config.Zones)
            data.Zones.Add(zone);
        data.Tags.Add("sample", "BulkCreateCustom");
        data.Tags.Add("batch", label);
        data.Tags.Add("correlationId", correlation);
        return new BatchRequest(label, Guid.NewGuid().ToString(), correlation, data,
            $"/subscriptions/{config.SubscriptionId}/resourceGroups/{config.ResourceGroup}/providers/Microsoft.Compute/virtualMachines/", names,
            config.Sizes.ToDictionary(s => s.Name, s => config.ImageMinimumOSDiskGB,
                StringComparer.OrdinalIgnoreCase));
    }

    private static BulkActionVMProperties BuildBaseProfile(DemoConfig config, string password) => new()
    {
        OSProfile = new OSProfile
        {
            AdminUsername = config.AdminUsername,
            AdminPassword = password,
            WindowsConfiguration = new WindowsConfiguration
            {
                IsProvisionVMAgent = true,
                EnableAutomaticUpdates = true,
                PatchSettings = new PatchSettings
                {
                    PatchMode = WindowsVMGuestPatchMode.AutomaticByOS,
                    AssessmentMode = WindowsPatchAssessmentMode.AutomaticByPlatform
                }
            }
        },
        StorageProfile = new StorageProfile
        {
            ImageReference = new ImageReference
            {
                Publisher = "MicrosoftWindowsServer", Offer = "WindowsServer",
                Sku = "2022-datacenter-azure-edition", Version = config.ImageVersion
            },
            OSDisk = new OSDisk(DiskCreateOptionTypes.FromImage)
            {
                OSType = OperatingSystemTypes.Windows,
                DiskSizeGB = config.ImageMinimumOSDiskGB,
                Caching = CachingTypes.ReadWrite,
                ManagedDisk = new ManagedDiskParametersContent { StorageAccountType = StorageAccountTypes.StandardSSDLRS }
            }
        },
        NetworkProfile = new NetworkProfile
        {
            NetworkApiVersion = NetworkApiVersion._20221101,
            NetworkInterfaceConfigurations =
            {
                new VirtualMachineNetworkInterfaceConfiguration("nic")
                {
                    Properties = new VirtualMachineNetworkInterfaceConfigurationProperties(
                    [
                        new VirtualMachineNetworkInterfaceIPConfiguration("ipconfig")
                        {
                            Properties = new VirtualMachineNetworkInterfaceIPConfigurationProperties
                            {
                                SubnetId = config.SubnetId, IsPrimary = true
                            }
                        }
                    ]) { IsPrimary = true }
                }
            }
        }
    };
}
