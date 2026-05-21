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

    public static ExecuteCreateFlexContent BuildRequest(
        FlexCreateConfig config,
        string subnetId,
        int resourceCount)
    {
        var resourcePrefix = BuildResourcePrefix(config.VmPrefix);
        var executionParameters = new ScheduledActionExecutionParameterDetail
        {
            RetryPolicy = new UserRequestRetryPolicy
            {
                // Number of times ScheduledActions retries on failure: range 0-7.
                RetryCount = RetryCount,
                // Time window in minutes for retries: range 5-120.
                RetryWindowInMinutes = RetryWindowInMinutes
            }
        };

        var payload = new ResourceProvisionFlexPayload(
            resourceCount: resourceCount,
            flexProperties: new ComputeScheduleFlexProperties(
                new[]
                {
                    new ComputeScheduleVmSizeProfile(name: PrimaryVmSizeName),
                    new ComputeScheduleVmSizeProfile(name: SecondaryVmSizeName),
                    new ComputeScheduleVmSizeProfile(name: TertiaryVmSizeName),
                },
                ComputeScheduleOSType.Windows,
                new ComputeSchedulePriorityProfile
                {
                    Type = ComputeSchedulePriorityType.Regular,
                    AllocationStrategy = ComputeScheduleAllocationStrategy.LowestPrice
                }))
        {
            ResourcePrefix = resourcePrefix,
            VirtualMachineBaseProfile = new BulkVmConfiguration
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
            }
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

        return new ExecuteCreateFlexContent(payload, executionParameters)
        {
            CorrelationId = Guid.NewGuid().ToString()
        };
    }

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
