using System.ClientModel.Primitives;
using System.Text.Json;
using Azure;
using Azure.ResourceManager.ComputeSchedule.Models;

namespace ExecuteVDICreateFlex;

internal static class JsonStringApiDemo
{
    public static async Task RunAsync(int? resourceCountOverride = null, FlexRunLogger? logger = null)
    {
        await ExecuteVDICreateFlexApiDemo.RunAsync(
            "API sample from JSON string",
            BuildRequestFromJsonString,
            resourceCountOverride,
            logger);
    }

    private static ExecuteCreateFlexContent BuildRequestFromJsonString(
        FlexCreateConfig config,
        string subnetId,
        int resourceCount)
    {
        var requestBodyJson = BuildRequestBodyJson(config, subnetId, resourceCount);
        return ModelReaderWriter.Read<ExecuteCreateFlexContent>(
            BinaryData.FromString(requestBodyJson),
            ModelReaderWriterOptions.Json)
            ?? throw new InvalidOperationException("Unable to deserialize the ExecuteCreateFlex JSON request body.");
    }

    private static string BuildRequestBodyJson(FlexCreateConfig config, string subnetId, int resourceCount)
    {
        var resourcePrefix = BuildResourcePrefix(config.VmPrefix);
        var virtualMachineName = BuildWindowsComputerName($"{resourcePrefix}vm0");

        return $$"""
            {
              "resourceConfigParameters": {
                "resourceCount": {{resourceCount}},
                "resourcePrefix": {{JsonString(resourcePrefix)}},
                "virtualMachineBaseProfile": {
                  "computeApiVersion": "2023-09-01",
                  "resourceGroupName": {{JsonString(config.ResourceGroupName)}},
                  "properties": {
                    "hardwareProfile": {
                      "vmSize": "Standard_D2ads_v5"
                    },
                    "storageProfile": {
                      "imageReference": {
                        "publisher": "MicrosoftWindowsServer",
                        "offer": "WindowsServer",
                        "sku": "2025-datacenter-azure-edition",
                        "version": "latest"
                      },
                      "osDisk": {
                        "osType": "Windows",
                        "caching": "ReadWrite",
                        "managedDisk": {
                          "storageAccountType": "Standard_LRS"
                        },
                        "deleteOption": "Delete",
                        "diskSizeGB": 127,
                        "createOption": "FromImage"
                      },
                      "diskControllerType": "SCSI"
                    },
                    "networkProfile": {
                      "networkInterfaceConfigurations": [
                        {
                          "name": "samplenic",
                          "properties": {
                            "primary": true,
                            "enableIPForwarding": true,
                            "ipConfigurations": [
                              {
                                "name": "samplenic",
                                "properties": {
                                  "subnet": {
                                    "id": {{JsonString(subnetId)}}
                                  },
                                  "primary": true
                                }
                              }
                            ]
                          }
                        }
                      ],
                      "networkApiVersion": "2020-11-01"
                    }
                  }
                },
                "virtualMachineOverrides": [
                  {
                    "name": {{JsonString(virtualMachineName)}},
                    "resourceGroupName": {{JsonString(config.ResourceGroupName)}},
                    "properties": {
                      "hardwareProfile": {
                        "vmSize": "Standard_D2ads_v5"
                      },
                      "osProfile": {
                        "computerName": {{JsonString(virtualMachineName)}},
                        "adminUsername": {{JsonString(config.VmAdminUsername)}},
                        "adminPassword": {{JsonString(config.VmAdminPassword)}},
                        "windowsConfiguration": {
                          "provisionVMAgent": true,
                          "enableAutomaticUpdates": true
                        }
                      }
                    }
                  }
                ],
                "flexProperties": {
                  "vmSizeProfiles": [
                    {
                      "name": "Standard_D2ads_v5"
                    },
                    {
                      "name": "Standard_E2ads_v5"
                    },
                    {
                      "name": "Standard_D2ds_v5"
                    }
                  ],
                  "osType": "Windows",
                  "priorityProfile": {
                    "type": "Regular",
                    "allocationStrategy": "LowestPrice"
                  }
                }
              },
              "executionParameters": {
                "retryPolicy": {
                  "retryCount": 1,
                  "retryWindowInMinutes": 45
                }
              },
              "correlationid": {{JsonString(Guid.NewGuid().ToString())}}
            }
            """;
    }

    private static string JsonString(string value) => JsonSerializer.Serialize(value);

    private static string BuildResourcePrefix(string vmPrefix)
    {
        var sanitizedPrefix = new string(vmPrefix.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
        if (string.IsNullOrWhiteSpace(sanitizedPrefix))
        {
            sanitizedPrefix = "vm";
        }

        return $"{sanitizedPrefix}-";
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
}
