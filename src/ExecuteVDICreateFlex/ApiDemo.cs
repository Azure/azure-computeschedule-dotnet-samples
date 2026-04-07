using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ComputeSchedule;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;
using UtilityMethods;

namespace ExecuteVDICreateFlex;

internal static class ExecuteVDICreateFlexApiDemo
{
    public static async Task RunAsync(int? resourceCountOverride = null)
    {
        var config = FlexCreateConfig.Load();
        var resourceCount = resourceCountOverride ?? FlexRequestBuilder.TotalRequestedVmCount;

        // ---- Inputs ----
        var subscriptionId = config.SubscriptionId;
        var location = config.Location;
        var resourceGroupName = config.ResourceGroupName;

        TokenCredential credential = new DefaultAzureCredential();
        var armClient = new ArmClient(credential, subscriptionId);
        var subscription = armClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(subscriptionId));
        var resourceGroup = await subscription.GetResourceGroupAsync(config.ResourceGroupName);

        var vnetClient = ArmClientFactory.CreateVNetClient(credential, config.SubscriptionId);
        var vnet = await HelperMethods.CreateVirtualNetwork(resourceGroup, config.SubnetName, config.VnetName, config.Location, vnetClient);
        var subnetId = HelperMethods.GetSubnetId(vnet).ToString();
 
        // Build Flex properties (VM size priority order)
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

        // Build flex payload
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

        // Build request wrapper
        var request = new ExecuteCreateFlexContent(payload, new ScheduledActionExecutionParameterDetail())
        {
            CorrelationId = Guid.NewGuid().ToString()
        };
        Console.WriteLine($"CorrelationId: {request.CorrelationId}");

        // Execute API
        CreateFlexResourceOperationResult result =
            (await subscription.ExecuteVirtualMachineCreateFlexOperationAsync(location, request)).Value;
        Console.WriteLine($"ExecuteCreateFlex returned {result.Results.Count} operation result(s).");

        // Poll operation status via shared helper
        var validOps = HelperMethods.ExcludeResourcesNotProcessed(result.Results);
        var completedOperations = new Dictionary<string, ResourceOperationDetails>();
        Console.WriteLine($"Valid operations to poll: {validOps.Count}.");

        if (validOps.Count == 0)
        {
            Console.WriteLine("No valid operations to poll");
            return;
        }

        await HelperMethods.PollOperationStatus([.. validOps.Keys], completedOperations, location, subscription);

        var completedCount = completedOperations.Count;
        var succeededCount = completedOperations.Values.Count(op => op.State == ScheduledActionOperationState.Succeeded);
        var failedCount = completedOperations.Values.Count(op => op.State == ScheduledActionOperationState.Failed);
        var cancelledCount = completedOperations.Values.Count(op => op.State == ScheduledActionOperationState.Cancelled);

        FinalStatusConsoleWriter.WriteApiStatus(
            $"Final status: valid={validOps.Count}, completed={completedCount}, succeeded={succeededCount}, failed={failedCount}, cancelled={cancelledCount}.",
            validOps.Count,
            completedCount,
            failedCount,
            cancelledCount);
    }
}
