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
 
        // 1) Build execution params
        var executionParams = new ScheduledActionExecutionParameterDetail
        {
            RetryPolicy = new UserRequestRetryPolicy
            {
                RetryCount = 1,
                RetryWindowInMinutes = 45
            }
        };

        // 2) Build Flex properties (VM size priority order)
        var flexProperties = new FlexProperties(
            new[]
            {
                new VmSizeProfile("Standard_D2ads_v5", 0),
                new VmSizeProfile("Standard_E4as_v5", 1),
                // we can add more
            },
            OsType.Windows,
            new PriorityProfile
            {
                Type = PriorityType.Regular,
                AllocationStrategy = AllocationStrategy.Prioritized,
            });

        // 3) Build payload (focus on this more)
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

        // Per-resource override
        payload.ResourceOverrides.Add(new Dictionary<string, BinaryData>
        {
            ["name"] = BinaryData.FromString("\"demovm01\""),
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

        // 4) Build request wrapper
        var request = new ExecuteCreateFlexContent(payload, executionParams)
        {
            CorrelationId = Guid.NewGuid().ToString()
        };

        // 5) Execute API
        CreateFlexResourceOperationResult result =
            (await subscription.VirtualMachinesExecuteCreateFlexAsync(location, request)).Value;

        // 6) Poll operation status // just use a wrapper
        var operationIds = result.Results
            .Where(r => r.ErrorCode == null && r.Operation.State != ScheduledActionOperationState.Blocked)
            .Select(r => r.Operation.OperationId)
            .ToHashSet();

        var totalOperationCount = operationIds.Count;
        var terminalOperations = new Dictionary<string, ScheduledActionOperationState>();

        while (operationIds.Count > 0)
        {
            var status = (await subscription.GetVirtualMachineOperationStatusAsync(
                location,
                new GetOperationStatusContent(operationIds, Guid.NewGuid().ToString()))).Value;

            foreach (var operation in status.Results)
            {
                if (operation.Operation.State == ScheduledActionOperationState.Succeeded
                    || operation.Operation.State == ScheduledActionOperationState.Failed
                    || operation.Operation.State == ScheduledActionOperationState.Cancelled)
                {
                    terminalOperations.TryAdd(operation.Operation.OperationId, operation.Operation.State.Value);
                    operationIds.Remove(operation.Operation.OperationId);
                }
            }

            // summarize (cumulative)
            var succeeded = terminalOperations.Values.Count(state => state == ScheduledActionOperationState.Succeeded);
            var failed = terminalOperations.Values.Count(state => state == ScheduledActionOperationState.Failed);
            var cancelled = terminalOperations.Values.Count(state => state == ScheduledActionOperationState.Cancelled);

            Console.WriteLine($"Completed={terminalOperations.Count}/{totalOperationCount}, Succeeded={succeeded}, Failed={failed}, Cancelled={cancelled}");

            if (operationIds.Count > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(15));
            }
        }
    }
}
