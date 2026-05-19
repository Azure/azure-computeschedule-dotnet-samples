using System.ClientModel.Primitives;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.ResourceManager.ComputeSchedule.Models;

namespace ExecuteVDICreateFlex.Scenarios;

internal static class CreateFlexScenarioExamples
{
    public static ExecuteCreateFlexContent BuildRequest(
        CreateFlexScenarioDefinition scenario,
        FlexCreateConfig config,
        string subnetId,
        int? resourceCountOverride = null)
    {
        var resourceCount = resourceCountOverride ?? 1;
        var resourcePrefix = BuildResourcePrefix(config.VmPrefix, scenario.Number);
        var baseProfile = BuildBaseProfile(scenario, config, subnetId, resourcePrefix);
        var flexProperties = BuildFlexProperties(scenario);

        var payload = new ResourceProvisionFlexPayload(resourceCount, flexProperties)
        {
            ResourcePrefix = resourcePrefix,
            VirtualMachineBaseProfile = baseProfile
        };

        return new ExecuteCreateFlexContent(payload, FlexRequestBuilder.BuildExecutionParams())
        {
            CorrelationId = Guid.NewGuid().ToString()
        };
    }

    private static ComputeScheduleFlexProperties BuildFlexProperties(CreateFlexScenarioDefinition scenario)
    {
        var flexProperties = new ComputeScheduleFlexProperties(
            BuildVmSizeProfiles(scenario),
            scenario.OsType,
            BuildPriorityProfile(scenario));

        if (scenario.HasZoneAllocationPolicy)
        {
            flexProperties.ZoneAllocationPolicy = BuildZoneAllocationPolicy(scenario);
        }

        return flexProperties;
    }

    private static IEnumerable<ComputeScheduleVmSizeProfile> BuildVmSizeProfiles(CreateFlexScenarioDefinition scenario)
    {
        for (var i = 0; i < scenario.VmSizeNames.Count; i++)
        {
            var profile = new ComputeScheduleVmSizeProfile(name: scenario.VmSizeNames[i]);
            if (scenario.IncludeVmSizeRanks)
            {
                profile.Rank = i;
            }

            yield return profile;
        }
    }

    private static ComputeSchedulePriorityProfile BuildPriorityProfile(CreateFlexScenarioDefinition scenario)
    {
        if (scenario.SpotEvictionPolicy is null && scenario.SpotMaxPricePerVm is null)
        {
            return new ComputeSchedulePriorityProfile
            {
                Type = scenario.PriorityType,
                AllocationStrategy = scenario.AllocationStrategy
            };
        }

        var priorityProfileJson = new JsonObject
        {
            ["type"] = scenario.PriorityType.ToString(),
            ["allocationStrategy"] = scenario.AllocationStrategy.ToString()
        };

        if (scenario.SpotEvictionPolicy is not null)
        {
            priorityProfileJson["evictionPolicy"] = scenario.SpotEvictionPolicy;
        }

        if (scenario.SpotMaxPricePerVm.HasValue)
        {
            priorityProfileJson["maxPricePerVM"] = scenario.SpotMaxPricePerVm.Value;
        }

        return ModelReaderWriter.Read<ComputeSchedulePriorityProfile>(
            BinaryData.FromString(priorityProfileJson.ToJsonString()),
            ModelReaderWriterOptions.Json)!;
    }

    private static ComputeScheduleZoneAllocationPolicy BuildZoneAllocationPolicy(CreateFlexScenarioDefinition scenario)
    {
        var policy = new ComputeScheduleZoneAllocationPolicy(scenario.ZoneDistributionStrategy!.Value);

        foreach (var (zone, rank) in scenario.ZonePreferences ?? [])
        {
            policy.ZonePreferences.Add(new ComputeScheduleZonePreference(zone) { Rank = rank });
        }

        return policy;
    }

    private static BulkVmConfiguration BuildBaseProfile(
        CreateFlexScenarioDefinition scenario,
        FlexCreateConfig config,
        string subnetId,
        string resourcePrefix)
    {
        var profile = new BulkVmConfiguration
        {
            Name = BuildComputerName(resourcePrefix, scenario.OsType),
            ResourceGroupName = config.ResourceGroupName,
            ComputeApiVersion = "2023-09-01",
            Properties = new BulkActionVirtualMachineProperties
            {
                HardwareProfile = new VirtualMachineHardwareProfile { VmSize = scenario.VmSizeNames[0] },
                StorageProfile = BuildStorageProfile(scenario.OsType),
                OsProfile = BuildOsProfile(scenario, config, resourcePrefix),
                NetworkProfile = BuildNetworkProfile(subnetId)
            }
        };

        profile.Tags["azsecpack"] = "nonprod";
        profile.Tags["platformsettings.host_environment.service.platform_optedin_for_rootcerts"] = "true";

        foreach (var zone in scenario.Zones)
        {
            profile.Zones.Add(zone);
        }

        return profile;
    }

    private static VirtualMachineStorageProfile BuildStorageProfile(ComputeScheduleOSType osType)
    {
        var isLinux = osType == ComputeScheduleOSType.Linux;

        return new VirtualMachineStorageProfile
        {
            ImageReference = isLinux
                ? new ImageReference
                {
                    Publisher = "Canonical",
                    Offer = "0001-com-ubuntu-server-jammy",
                    Sku = "22_04-lts",
                    Version = "latest"
                }
                : new ImageReference
                {
                    Publisher = "MicrosoftWindowsServer",
                    Offer = "WindowsServer",
                    Sku = "2022-datacenter-azure-edition",
                    Version = "latest"
                },
            OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
            {
                OSType = isLinux ? OperatingSystemType.Linux : OperatingSystemType.Windows,
                Caching = CachingType.ReadWrite,
                ManagedDisk = new ComputeScheduleManagedDiskConfig
                {
                    StorageAccountType = StorageAccountType.StandardLRS
                },
                DeleteOption = DiskDeleteOptionType.Delete,
                DiskSizeGB = isLinux ? 30 : 127
            },
            DiskControllerType = DiskControllerType.SCSI
        };
    }

    private static VirtualMachineOSProfile BuildOsProfile(
        CreateFlexScenarioDefinition scenario,
        FlexCreateConfig config,
        string resourcePrefix)
    {
        var osProfile = new VirtualMachineOSProfile
        {
            ComputerName = BuildComputerName(resourcePrefix, scenario.OsType),
            AdminUsername = config.VmAdminUsername,
            AdminPassword = config.VmAdminPassword
        };

        if (scenario.OsType == ComputeScheduleOSType.Linux)
        {
            osProfile.LinuxConfiguration = new LinuxConfiguration
            {
                IsPasswordAuthenticationDisabled = false,
                ProvisionVmAgent = true
            };
        }
        else
        {
            osProfile.WindowsConfiguration = new WindowsConfiguration
            {
                ProvisionVmAgent = true,
                IsAutomaticUpdatesEnabled = true
            };
        }

        return osProfile;
    }

    private static VirtualMachineNetworkProfile BuildNetworkProfile(string subnetId) =>
        new()
        {
            NetworkInterfaceConfigurations =
            {
                new VirtualMachineNetworkInterfaceConfiguration("scenario-nic")
                {
                    Properties = new VirtualMachineNetworkInterfaceConfigurationProperties(
                        new[]
                        {
                            new VirtualMachineNetworkInterfaceIPConfiguration("scenario-ipconfig")
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
            NetworkApiVersion = NetworkApiVersion._20221101
        };

    private static string BuildResourcePrefix(string vmPrefix, int scenarioNumber)
    {
        var sanitizedPrefix = new string(vmPrefix.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
        if (string.IsNullOrWhiteSpace(sanitizedPrefix))
        {
            sanitizedPrefix = "vm";
        }

        return $"{sanitizedPrefix}-scenario{scenarioNumber}-";
    }

    private static string BuildComputerName(string resourcePrefix, ComputeScheduleOSType osType)
    {
        var filtered = new string(resourcePrefix.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(filtered))
        {
            filtered = "saflexvm";
        }

        var maxLength = osType == ComputeScheduleOSType.Linux ? 64 : 15;
        if (filtered.Length > maxLength)
        {
            filtered = filtered[..maxLength].Trim('-');
        }

        if (string.IsNullOrWhiteSpace(filtered))
        {
            return "saflexvm";
        }

        return filtered.All(char.IsDigit) ? $"vm{filtered}" : filtered;
    }
}
