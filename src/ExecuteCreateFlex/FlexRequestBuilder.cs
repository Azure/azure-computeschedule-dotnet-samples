using Azure.ResourceManager.ComputeSchedule.Models;
using UtilityMethods;

namespace ExecuteCreateFlex;

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
    /// Returns <see cref="FlexProperties"/> describing the prioritized VM size
    /// profiles and allocation strategy. ComputeSchedule will attempt each size
    /// in priority order when the preferred SKU is unavailable.
    /// </summary>
    public static FlexProperties BuildFlexProperties() =>
        new(
            new[]
            {
                new VmSizeProfile(name: "Standard_D2ads_v5", rank: 0),
                new VmSizeProfile(name: "Standard_E4as_v5", rank: 1),
            },
            OsType.Windows,
            new PriorityProfile
            {
                Type = PriorityType.Regular,
                AllocationStrategy = AllocationStrategy.Prioritized,
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
        var computerName = BuildWindowsComputerName(batchPrefix);

        var payload = new ResourceProvisionFlexPayload(resourceCount: resourceCount, flexProperties: BuildFlexProperties())
        {
            ResourcePrefix = batchPrefix,
        };

        payload.BaseProfile["resourceGroupName"] = BinaryData.FromString($"\"{config.ResourceGroupName}\"");
        payload.BaseProfile["computeApiVersion"] = BinaryData.FromString("\"2023-09-01\"");
        payload.BaseProfile["location"] = BinaryData.FromString($"\"{config.Location}\"");
        payload.BaseProfile["properties"] = BinaryData.FromObjectAsJson(new
        {
            hardwareProfile = new { vmSize = "Standard_D2ads_v5" },
            osProfile = new
            {
                computerName = computerName,
                adminUsername = config.VmAdminUsername,
                adminPassword = config.VmAdminPassword
            },
            storageProfile = new
            {
                imageReference = new
                {
                    publisher = "MicrosoftWindowsServer",
                    offer = "WindowsServer",
                    sku = "2022-datacenter-azure-edition",
                    version = "latest"
                },
                osDisk = new
                {
                    osType = "Windows",
                    createOption = "FromImage",
                    caching = "ReadWrite",
                    managedDisk = new { storageAccountType = "Standard_LRS" },
                    deleteOption = "Delete",
                    diskSizeGB = 127
                },
                diskControllerType = "SCSI"
            },
            networkProfile = new
            {
                networkInterfaceConfigurations = new[]
                {
                    new
                    {
                        name = "samplenic",
                        properties = new
                        {
                            primary = true,
                            enableIPForwarding = true,
                            ipConfigurations = new[]
                            {
                                new
                                {
                                    name = "samplenic",
                                    properties = new
                                    {
                                        subnet = new
                                        {
                                            id = subnetId,
                                            properties = new
                                            {
                                                defaultOutboundAccess = false
                                            }
                                        },
                                        primary = true,
                                        applicationGatewayBackendAddressPools = Array.Empty<object>(),
                                        loadBalancerBackendAddressPools = Array.Empty<object>()
                                    }
                                }
                            }
                        }
                    }
                },
                networkApiVersion = "2022-07-01"
            }
        });

        // Per-VM override: name and admin credentials
        var overrideName = BuildWindowsComputerName($"{batchPrefix}vm0");
        var vmOverride = HelperMethods.GenerateResourceOverrideItem(
            overrideName,
            config.Location,
            "Standard_D2ads_v5",
            config.VmAdminPassword,
            config.VmAdminUsername);
        payload.ResourceOverrides.Add(vmOverride);

        return payload;
    }

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
