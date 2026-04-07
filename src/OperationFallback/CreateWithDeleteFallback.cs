using UtilityMethods;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ComputeSchedule;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;

namespace OperationFallback;

/// <summary>
/// Create with Delete fallback.
///
/// If VM creation fails after all retries, the system automatically
/// deletes the partially-created VM to clean up resources.
///
/// This sample auto-creates a vnet and subnet for the VM.
/// </summary>
public static class CreateWithDeleteFallback
{
    private const string VnetName = "fallback-demo-vnet";
    private const string SubnetName = "default";

    public static async Task RunAsync(
        SubscriptionResource subscriptionResource,
        string location,
        string subscriptionId,
        string resourceGroupName,
        string adminUsername,
        string adminPassword)
    {
        Console.WriteLine("[Scenario] Create with Delete fallback\n");

        // Ensure resource group exists
        Console.WriteLine("[Setup] Ensuring resource group exists...");
        var cred = new Azure.Identity.DefaultAzureCredential();
        var armClient = new ArmClient(cred, subscriptionId);
        var subscriptionResourceId = SubscriptionResource.CreateResourceIdentifier(subscriptionId);
        var sub = armClient.GetSubscriptionResource(subscriptionResourceId);
        var rgCollection = sub.GetResourceGroups();
        await rgCollection.CreateOrUpdateAsync(WaitUntil.Completed, resourceGroupName, new ResourceGroupData(new AzureLocation(location)));

        // Create a vnet and subnet for the VM (no public access)
        Console.WriteLine("[Setup] Creating virtual network...");
        var networkApiOptions = new ArmClientOptions();
        networkApiOptions.SetApiVersion(new ResourceType("Microsoft.Network/virtualNetworks"), "2024-05-01");
        var networkClient = new ArmClient(cred, subscriptionId, networkApiOptions);

        var rgId = ResourceGroupResource.CreateResourceIdentifier(subscriptionId, resourceGroupName);
        var vnetId = new ResourceIdentifier($"{rgId}/providers/Microsoft.Network/virtualNetworks/{VnetName}");
        var vnetData = new GenericResourceData(new AzureLocation(location))
        {
            Properties = BinaryData.FromObjectAsJson(new
            {
                addressSpace = new { addressPrefixes = new[] { "10.0.0.0/16" } },
                subnets = new[]
                {
                    new
                    {
                        name = SubnetName,
                        properties = new
                        {
                            addressPrefix = "10.0.0.0/24",
                            defaultOutboundAccess = false,
                            privateEndpointNetworkPolicies = "Enabled",
                            privateLinkServiceNetworkPolicies = "Enabled"
                        }
                    }
                }
            })
        };

        await networkClient.GetGenericResources().CreateOrUpdateAsync(WaitUntil.Completed, vnetId, vnetData);
        Console.WriteLine($"[Setup] Virtual network created: {VnetName}/{SubnetName}");

        string subnetId = $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Network/virtualNetworks/{VnetName}/subnets/{SubnetName}";

        // Build retry policy with Delete fallback
        var executionParams = new ScheduledActionExecutionParameterDetail()
        {
            RetryPolicy = new UserRequestRetryPolicy()
            {
                RetryWindowInMinutes = 30,
                OnFailureAction = "Delete"
            }
        };

        // Build the VM create payload
        // baseProfile is the shared template; resourceOverrides carry per-VM settings
        // CRP creates the NIC automatically from networkInterfaceConfigurations
        var payload = new ResourceProvisionPayload(1)
        {
            ResourcePrefix = "fallback-demo",
            BaseProfile =
            {
                { "resourceGroupName", BinaryData.FromObjectAsJson(resourceGroupName) },
                { "computeApiVersion", BinaryData.FromObjectAsJson("2024-07-01") },
                { "properties", BinaryData.FromObjectAsJson(new
                    {
                        hardwareProfile = new { vmSize = "Standard_D2s_v5" },
                        additionalCapabilities = new { hibernationEnabled = true },
                        storageProfile = new
                        {
                            imageReference = new
                            {
                                publisher = "MicrosoftWindowsServer",
                                offer = "WindowsServer",
                                sku = "2022-datacenter-g2",
                                version = "latest"
                            },
                            osDisk = new
                            {
                                createOption = "FromImage",
                                managedDisk = new { storageAccountType = "Standard_LRS" },
                                deleteOption = "Delete"
                            }
                        },
                        networkProfile = new
                        {
                            networkInterfaceConfigurations = new[]
                            {
                                new
                                {
                                    name = "nic-config",
                                    properties = new
                                    {
                                        primary = true,
                                        deleteOption = "Delete",
                                        ipConfigurations = new[]
                                        {
                                            new
                                            {
                                                name = "ipconfig1",
                                                properties = new
                                                {
                                                    subnet = new { id = subnetId },
                                                    primary = true
                                                }
                                            }
                                        }
                                    }
                                }
                            },
                            networkApiVersion = "2022-07-01"
                        }
                    })
                }
            },
            ResourceOverrides =
            {
                new Dictionary<string, BinaryData>
                {
                    { "name", BinaryData.FromObjectAsJson("fallback-demo-vm") },
                    { "location", BinaryData.FromObjectAsJson(location) },
                    { "properties", BinaryData.FromObjectAsJson(new
                        {
                            osProfile = new
                            {
                                computerName = "demo-vm",
                                adminUsername = adminUsername,
                                adminPassword = adminPassword
                            }
                        })
                    }
                }
            }
        };

        var request = new ExecuteCreateContent(payload, executionParams)
        {
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Submit the create operation
        var result = await subscriptionResource.ExecuteVirtualMachineCreateOperationAsync(location, request);

        var operationIds = result.Value.Results
            .Where(r => r.Operation?.OperationId is not null)
            .Select(r => r.Operation!.OperationId)
            .ToHashSet();

        Console.WriteLine($"[Submit] {operationIds.Count} operation(s) submitted. Polling for results...\n");
        var completedOperations = new Dictionary<string, ResourceOperationDetails>();
        await UtilityMethods.HelperMethods.PollOperationStatus(operationIds, completedOperations, location, subscriptionResource);

        foreach (var (opId, details) in completedOperations)
        {
            Console.WriteLine($"[Result] Operation {opId}: State = {details.State}");

            if (details.State == ScheduledActionOperationState.Succeeded)
            {
                Console.WriteLine("[OK] Create succeeded — no fallback needed.");
            }
            else if (details.State == ScheduledActionOperationState.Failed)
            {
                if (details.ResourceOperationError is not null)
                {
                    Console.WriteLine($"[Error] Primary: {details.ResourceOperationError.ErrorCode} — {details.ResourceOperationError.ErrorDetails}");
                }

                if (details.FallbackOperationInfo is not null)
                {
                    var fallback = details.FallbackOperationInfo;
                    Console.WriteLine($"[Fallback] {fallback.LastOpType}: Status = {fallback.Status}");

                    if (fallback.Status == ScheduledActionOperationState.Succeeded)
                    {
                        Console.WriteLine("[Fallback] [OK] Succeeded — partially-created VM was deleted.");
                    }
                    else
                    {
                        Console.WriteLine("[Fallback] [FAIL] Failed. Manual cleanup may be needed.");
                        if (fallback.Error is not null)
                        {
                            Console.WriteLine($"[Fallback] Error: {fallback.Error.ErrorCode} — {fallback.Error.ErrorDetails}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("[Fallback] Not executed (may indicate a non-retriable error).");
                }
            }
        }
    }
}
