using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ComputeSchedule;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;
using UtilityMethods;

namespace ExecuteCreateFlex;

internal static class ExecuteCreateFlexApiDemo
{
    public static async Task RunAsync(int? resourceCountOverride = null)
    {
        LogStep(1, "Loading configuration.");
        var config = FlexCreateConfig.Load();
        var resourceCount = resourceCountOverride ?? FlexRequestBuilder.TotalRequestedVmCount;

        // ---- Inputs ----
        var subscriptionId = config.SubscriptionId;
        var location = config.Location;
        var resourceGroupName = config.ResourceGroupName;

        LogStep(2, "Creating ARM clients and resolving the resource group.");
        TokenCredential credential = new DefaultAzureCredential();
        var armClient = new ArmClient(credential, subscriptionId);
        var subscription = armClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(subscriptionId));
        var resourceGroup = await subscription.GetResourceGroupAsync(config.ResourceGroupName);

        LogStep(3, "Creating or updating the virtual network and resolving the subnet.");
        var vnetClient = ArmClientFactory.CreateVNetClient(credential, config.SubscriptionId);
        var vnet = await HelperMethods.CreateVirtualNetwork(resourceGroup, config.SubnetName, config.VnetName, config.Location, vnetClient);
        var subnetId = HelperMethods.GetSubnetId(vnet).ToString();
 
        // 1) Build Flex properties (VM size priority order)
        LogStep(4, "Building flex properties.");
        var flexProperties = new ComputeScheduleFlexProperties(
            new[]
            {
                new ComputeScheduleVmSizeProfile(name: "Standard_D2ads_v5", rank: 0),
                new ComputeScheduleVmSizeProfile(name: "Standard_E4as_v5", rank: 1),
                // we can add more
            },
            ComputeScheduleOSType.Windows,
            new ComputeSchedulePriorityProfile
            {
                Type = ComputeSchedulePriorityType.Regular,
                AllocationStrategy = ComputeScheduleAllocationStrategy.Prioritized,
            });

        // 2) Build payload (focus on this more)
        LogStep(5, "Building the flex payload.");
        var payload = new ResourceProvisionFlexPayload(resourceCount: resourceCount, flexProperties)
        {
            ResourcePrefix = "demo-flex-"
        };

        payload.BaseProfile["resourceGroupName"] = BinaryData.FromString($"\"{resourceGroupName}\"");
        payload.BaseProfile["computeApiVersion"] = BinaryData.FromString("\"2023-09-01\"");
        payload.BaseProfile["location"] = BinaryData.FromString($"\"{location}\"");
        payload.BaseProfile["properties"] = BinaryData.FromObjectAsJson(new
        {
            hardwareProfile = new { vmSize = "Standard_D2ads_v5" },
            osProfile = new
            {
                computerName = "demovm01",
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
                     // When VM gets deleted disk can be detached and used later.
                     // You can also use "Delete" to automatically delete disks when VM gets deleted.
                    deleteOption = "Detach",
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
                        name = "demonic",
                        properties = new
                        {
                            primary = true,
                            enableIPForwarding = true,
                            ipConfigurations = new[]
                            {
                                new
                                {
                                    name = "demonic",
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

        // // Per-resource override
        payload.ResourceOverrides.Add(new Dictionary<string, BinaryData>
        {
            ["location"] = BinaryData.FromString($"\"{location}\""),
            ["properties"] = BinaryData.FromObjectAsJson(new
            {
                osProfile = new
                {
                    computerName = "demovm01",
                    adminUsername = config.VmAdminUsername,
                    adminPassword = config.VmAdminPassword
                }
            })
        });

        // 3) Build request wrapper
        LogStep(6, "Building the ExecuteCreateFlex request.");
        var request = new ExecuteCreateFlexContent(payload, new ScheduledActionExecutionParameterDetail())
        {
            CorrelationId = Guid.NewGuid().ToString()
        };
        Console.WriteLine($"CorrelationId: {request.CorrelationId}");

        // 4) Execute API
        LogStep(7, "Submitting ExecuteCreateFlex to Azure.");
        CreateFlexResourceOperationResult result =
            (await subscription.ExecuteVirtualMachineCreateFlexOperationAsync(location, request)).Value;
        Console.WriteLine($"ExecuteCreateFlex returned {result.Results.Count} operation result(s).");

        // 5) Poll operation status via shared helper
        LogStep(8, "Preparing operations for polling.");
        var validOps = HelperMethods.ExcludeResourcesNotProcessed(result.Results);
        var completedOperations = new Dictionary<string, ResourceOperationDetails>();
        Console.WriteLine($"Valid operations to poll: {validOps.Count}.");

        if (validOps.Count == 0)
        {
            Console.WriteLine("No valid operations to poll");
            return;
        }

        LogStep(9, "Polling operation status until terminal states are reached.");
        await HelperMethods.PollOperationStatus([.. validOps.Keys], completedOperations, location, subscription);

        LogStep(10, "Summarizing final operation results.");
        var completedCount = completedOperations.Count;
        var succeededCount = completedOperations.Values.Count(op => op.State == ScheduledActionOperationState.Succeeded);
        var failedCount = completedOperations.Values.Count(op => op.State == ScheduledActionOperationState.Failed);
        var cancelledCount = completedOperations.Values.Count(op => op.State == ScheduledActionOperationState.Cancelled);

        WriteFinalStatus(
            $"Final status: valid={validOps.Count}, completed={completedCount}, succeeded={succeededCount}, failed={failedCount}, cancelled={cancelledCount}.",
            validOps.Count,
            completedCount,
            failedCount,
            cancelledCount);
    }

    private static void LogStep(int stepNumber, string message)
    {
        Console.WriteLine($"Step {stepNumber}: {message}");
    }

    private static void WriteFinalStatus(string message, int validCount, int completedCount, int failedCount, int cancelledCount)
    {
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(message);
            return;
        }

        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = GetFinalStatusColor(validCount, completedCount, failedCount, cancelledCount);
        Console.WriteLine(message);
        Console.ForegroundColor = originalColor;
    }

    private static ConsoleColor GetFinalStatusColor(int validCount, int completedCount, int failedCount, int cancelledCount)
    {
        if (failedCount > 0)
        {
            return ConsoleColor.Red;
        }

        if (cancelledCount > 0 || completedCount < validCount)
        {
            return ConsoleColor.Yellow;
        }

        return ConsoleColor.Green;
    }
}
