using Azure.Core;
using Azure.ResourceManager.ComputeSchedule.Models;

namespace ExecuteVDICreateFlex;

/// <summary>
/// Builds the complete ExecuteCreateFlex request in one place so customers can
/// see the full SDK model shape without jumping through several helper methods.
/// </summary>
internal static class FlexRequestBuilder
{
    public const int TotalRequestedVmCount = 1;

    private const string PrimaryVmSizeName = "Standard_D2ads_v5";
    private const string SecondaryVmSizeName = "Standard_E2ads_v5";
    private const string TertiaryVmSizeName = "Standard_D2ds_v5";

    private const int RetryCount = 1;
    private const int RetryWindowInMinutes = 45;

    private const string ComputeApiVersion = "2023-09-01";
    private const string WindowsImagePublisher = "MicrosoftWindowsServer";
    private const string WindowsImageOffer = "WindowsServer";
    private const string WindowsImageSku = "2025-datacenter-azure-edition";
    private const string WindowsImageVersion = "latest";
    private const int WindowsOsDiskSizeGB = 127;

    private const string NetworkConfigurationName = "samplenic";
    private static readonly string[] s_zones = ["1", "2", "3"];

    public static ExecuteCreateFlexContent BuildRequest(
        FlexCreateConfig config,
        string subnetId,
        int resourceCount) =>
        BuildRequest(config, subnetId, resourceCount, includeZones: false);

    public static ExecuteCreateFlexContent BuildRequestWithZones(
        FlexCreateConfig config,
        string subnetId,
        int resourceCount) =>
        BuildRequest(config, subnetId, resourceCount, includeZones: true);

    private static ExecuteCreateFlexContent BuildRequest(
        FlexCreateConfig config,
        string subnetId,
        int resourceCount,
        bool includeZones)
    {
        var resourcePrefix = BuildResourcePrefix(config.VmPrefix);
        var payload = new ResourceProvisionFlexPayload(
            resourceCount: resourceCount,
            flexProperties: BuildFlexProperties(includeZones))
        {
            ResourcePrefix = resourcePrefix,
            VirtualMachineBaseProfile = BuildBaseProfile(config, subnetId, includeZones)
        };

        for (var i = 0; i < resourceCount; i++)
        {
            var virtualMachineName = BuildWindowsComputerName($"{resourcePrefix}vm{i}");
            payload.VirtualMachineOverrides.Add(new BulkVmConfiguration
            {
                Name = virtualMachineName,
                ResourceGroupName = config.ResourceGroupName,
                Properties = new BulkActionVirtualMachineProperties
                {
                    HardwareProfile = new VirtualMachineHardwareProfile
                    {
                        VmSize = PrimaryVmSizeName
                    },
                    OsProfile = new VirtualMachineOSProfile
                    {
                        ComputerName = virtualMachineName,
                        AdminUsername = config.VmAdminUsername,
                        AdminPassword = config.VmAdminPassword,
                        WindowsConfiguration = new WindowsConfiguration
                        {
                            ProvisionVmAgent = true,
                            IsAutomaticUpdatesEnabled = true
                        }
                    }
                }
            });
        }

        return new ExecuteCreateFlexContent(payload, BuildExecutionParameters())
        {
            CorrelationId = Guid.NewGuid().ToString()
        };
    }

    private static ComputeScheduleFlexProperties BuildFlexProperties(bool includeZones)
    {
        var flexProperties = new ComputeScheduleFlexProperties(
            new ComputeScheduleVmSizeProfile[]
            {
                // if strategy is set to Prioritized, then rank is required.
                // example: new ComputeScheduleVmSizeProfile(name: PrimaryVmSizeName) { Rank = 1 },
                new ComputeScheduleVmSizeProfile(name: PrimaryVmSizeName),
                new ComputeScheduleVmSizeProfile(name: SecondaryVmSizeName),
                new ComputeScheduleVmSizeProfile(name: TertiaryVmSizeName),
            },
            ComputeScheduleOSType.Windows,
            new ComputeSchedulePriorityProfile
            {
                Type = ComputeSchedulePriorityType.Regular,
                AllocationStrategy = ComputeScheduleAllocationStrategy.LowestPrice
            });

        if (includeZones)
        {
            flexProperties.ZoneAllocationPolicy = BuildZoneAllocationPolicy();
        }

        return flexProperties;
    }

    private static BulkVmConfiguration BuildBaseProfile(FlexCreateConfig config, string subnetId, bool includeZones)
    {
        var baseProfile = new BulkVmConfiguration
        {
            ComputeApiVersion = ComputeApiVersion,
            ResourceGroupName = config.ResourceGroupName,
            Properties = new BulkActionVirtualMachineProperties
            {
                HardwareProfile = new VirtualMachineHardwareProfile
                {
                    VmSize = PrimaryVmSizeName
                },
                StorageProfile = new VirtualMachineStorageProfile
                {
                    ImageReference = new ImageReference
                    {
                        Publisher = WindowsImagePublisher,
                        Offer = WindowsImageOffer,
                        Sku = WindowsImageSku,
                        Version = WindowsImageVersion
                    },
                    OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
                    {
                        OSType = OperatingSystemType.Windows,
                        Caching = CachingType.ReadWrite,
                        ManagedDisk = new ComputeScheduleManagedDiskConfig
                        {
                            StorageAccountType = StorageAccountType.StandardLRS
                        },
                        DeleteOption = DiskDeleteOptionType.Delete,
                        DiskSizeGB = WindowsOsDiskSizeGB
                    },
                    DiskControllerType = DiskControllerType.SCSI
                },
                NetworkProfile = new VirtualMachineNetworkProfile
                {
                    NetworkInterfaceConfigurations =
                    {
                        new VirtualMachineNetworkInterfaceConfiguration(NetworkConfigurationName)
                        {
                            Properties = new VirtualMachineNetworkInterfaceConfigurationProperties(
                                new[]
                                {
                                    new VirtualMachineNetworkInterfaceIPConfiguration(NetworkConfigurationName)
                                    {
                                        Properties = new VirtualMachineNetworkInterfaceIPConfigurationProperties
                                        {
                                            SubnetId = new ResourceIdentifier(subnetId),
                                            Primary = true
                                        }
                                    }
                                })
                            {
                                Primary = true,
                                EnableIPForwarding = true
                            }
                        }
                    },
                    NetworkApiVersion = NetworkApiVersion._20201101
                }
            }
        };

        if (includeZones)
        {
            foreach (var zone in s_zones)
            {
                baseProfile.Zones.Add(zone);
            }
        }

        return baseProfile;
    }

    private static ComputeScheduleZoneAllocationPolicy BuildZoneAllocationPolicy()
    {
        var zoneAllocationPolicy = new ComputeScheduleZoneAllocationPolicy(ComputeScheduleDistributionStrategy.Prioritized);
        for (var i = 0; i < s_zones.Length; i++)
        {
            zoneAllocationPolicy.ZonePreferences.Add(new ComputeScheduleZonePreference(s_zones[i])
            {
                Rank = i
            });
        }

        return zoneAllocationPolicy;
    }

    private static ScheduledActionExecutionParameterDetail BuildExecutionParameters() =>
        new()
        {
            RetryPolicy = new UserRequestRetryPolicy
            {
                // Number of times ScheduledActions retries on failure: range 0-7.
                RetryCount = RetryCount,
                // Time window in minutes for retries: range 5-120.
                RetryWindowInMinutes = RetryWindowInMinutes
            }
        };

    private static string BuildWindowsComputerName(string prefix)
    {
        var filtered = new string(prefix.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());

        if (string.IsNullOrWhiteSpace(filtered))
        {
            filtered = "vm";
        }

        var candidate = (filtered + "vm").Trim('-');

        if (candidate.Length > 15)
        {
            candidate = candidate[..15].Trim('-');
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = "vmhost";
        }

        if (candidate.All(char.IsDigit))
        {
            candidate = "vm" + candidate;
            if (candidate.Length > 15)
            {
                candidate = candidate[..15];
            }
        }

        return candidate;
    }

    private static string BuildResourcePrefix(string vmPrefix)
    {
        var sanitizedPrefix = new string(vmPrefix.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
        if (string.IsNullOrWhiteSpace(sanitizedPrefix))
        {
            sanitizedPrefix = "vm";
        }

        return $"{sanitizedPrefix}-";
    }
}
