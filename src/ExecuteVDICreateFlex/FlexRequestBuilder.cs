using Azure.Core;
using Azure.ResourceManager.ComputeSchedule.Models;
using UtilityMethods;

namespace ExecuteVDICreateFlex;

/// <summary>
/// Builds the request objects needed to call the ExecuteCreateFlex API.
/// </summary>
internal static class FlexRequestBuilder
{
    public const int TotalRequestedVmCount = 1;
    public const int MaxResourceCountPerRequest = 100;
    public const int MaxParallelBatches = 20;

    /// <summary>
    /// Returns the execution parameters with the retry policy for the operation.
    /// </summary>
    public static ScheduledActionExecutionParameterDetail BuildExecutionParams() =>
        new()
        {
            RetryPolicy = new UserRequestRetryPolicy()
            {
                // Number of times ScheduledActions retries on failure: range 0-7
                RetryCount = 1,
                // Time window in minutes for retries: range 5-120
                RetryWindowInMinutes = 45
            }
        };

    /// <summary>
    /// Returns <see cref="FlexProperties"/> describing the VM size profiles and
    /// lowest-price allocation strategy.
    /// </summary>
    public static ComputeScheduleFlexProperties BuildFlexProperties() =>
        new(
            new[]
            {
                new ComputeScheduleVmSizeProfile(name: "Standard_D2ads_v5"),
                new ComputeScheduleVmSizeProfile(name: "Standard_E2ads_v5"),
                new ComputeScheduleVmSizeProfile(name: "Standard_D2ds_v5"),
            },
            ComputeScheduleOSType.Windows,
            new ComputeSchedulePriorityProfile
            {
                Type = ComputeSchedulePriorityType.Regular,
                AllocationStrategy = ComputeScheduleAllocationStrategy.LowestPrice,
            });

    /// <summary>
    /// Builds the <see cref="ResourceProvisionFlexPayload"/> with the base profile
    /// (OS image, disk, network) and a per-VM resource override.
    /// </summary>
    /// <param name="config">Configuration values loaded from the .env file.</param>
    /// <param name="subnetId">The fully-qualified resource ID of the subnet to attach VMs to.</param>
    public static ResourceProvisionFlexPayload BuildFlexPayload(FlexCreateConfig config, string subnetId, int resourceCount, int batchIndex)
    {
        var batchPrefix = BuildBatchPrefix(config.VmPrefix, batchIndex);
        var virtualMachineBaseProfile = BuildBaseProfile(subnetId);
        var virtualMachineOverrides = new List<BulkVmConfiguration>();

        for (var i = 0; i < resourceCount; i++)
        {
            var overrideName = BuildWindowsComputerName($"{batchPrefix}vm{i}");
            virtualMachineOverrides.Add(BuildVmOverride(overrideName, config));
        }

        var payload = new ResourceProvisionFlexPayload(resourceCount: resourceCount, flexProperties: BuildFlexProperties())
        {
            ResourcePrefix = batchPrefix,
            VirtualMachineBaseProfile = virtualMachineBaseProfile
        };

        foreach (var virtualMachineOverride in virtualMachineOverrides)
        {
            payload.VirtualMachineOverrides.Add(virtualMachineOverride);
        }

        return payload;
    }

    private static BulkVmConfiguration BuildBaseProfile(string subnetId) =>
        new()
        {
            ComputeApiVersion = "2023-09-01",
            Zones = { "1", "2", "3" },
            Properties = new BulkActionVirtualMachineProperties
            {
                HardwareProfile = new VirtualMachineHardwareProfile { VmSize = "Standard_D2ads_v5" },
                StorageProfile = new VirtualMachineStorageProfile
                {
                    ImageReference = new ImageReference
                    {
                        Publisher = "MicrosoftWindowsServer",
                        Offer = "WindowsServer",
                        Sku = "2025-datacenter-azure-edition",
                        Version = "latest"
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
                        DiskSizeGB = 127
                    },
                    DiskControllerType = DiskControllerType.SCSI
                },
                NetworkProfile = new VirtualMachineNetworkProfile
                {
                    NetworkInterfaceConfigurations =
                    {
                        new VirtualMachineNetworkInterfaceConfiguration("samplenic")
                        {
                            Properties = new VirtualMachineNetworkInterfaceConfigurationProperties(
                                new[]
                                {
                                    new VirtualMachineNetworkInterfaceIPConfiguration("samplenic")
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

    private static BulkVmConfiguration BuildVmOverride(string name, FlexCreateConfig config) =>
        new()
        {
            Name = name,
            ResourceGroupName = config.ResourceGroupName,
            Properties = new BulkActionVirtualMachineProperties
            {
                HardwareProfile = new VirtualMachineHardwareProfile { VmSize = "Standard_D2ads_v5" },
                OsProfile = new VirtualMachineOSProfile
                {
                    ComputerName = name,
                    AdminUsername = config.VmAdminUsername,
                    AdminPassword = config.VmAdminPassword,
                    WindowsConfiguration = new WindowsConfiguration
                    {
                        ProvisionVmAgent = true,
                        IsAutomaticUpdatesEnabled = true
                    }
                }
            }
        };

    /// <summary>
    /// Wraps the payload and execution params into the final
    /// <see cref="ExecuteCreateFlexContent"/> ready to send to the API.
    /// </summary>
    public static ExecuteCreateFlexContent BuildRequest(
        ResourceProvisionFlexPayload payload,
        ScheduledActionExecutionParameterDetail executionParams) =>
        new(payload, executionParams)
        {
            CorrelationId = Guid.NewGuid().ToString()
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

    private static string BuildBatchPrefix(string vmPrefix, int batchIndex)
    {
        var sanitizedPrefix = new string(vmPrefix.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
        if (string.IsNullOrWhiteSpace(sanitizedPrefix))
        {
            sanitizedPrefix = "vm";
        }

        return $"{sanitizedPrefix}b{batchIndex}-";
    }
}
