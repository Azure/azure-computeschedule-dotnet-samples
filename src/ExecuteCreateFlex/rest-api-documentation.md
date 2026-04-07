# VDI Execute Flex Create Configuration for Scheduled Actions

This guide explains how to configure a Flex create request for Scheduled Actions. The request body allows clients to define a base VM profile, choose Flex placement behavior and control execution-time retry settings.

## Endpoint Format

Use the following endpoint format to construct the request URL for the Flex create operation:

```text
https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.ComputeSchedule/locations/{location}/virtualMachinesExecuteCreateFlex?api-version=2026-03-01-preview
```


| Component       | Value                              |
| ----------------- | ------------------------------------ |
| Base URL        | `https://management.azure.com`     |
| Subscription ID | `{subscriptionId}`                 |
| Provider        | `Microsoft.ComputeSchedule`        |
| Location        | `{location}`                       |
| Operation       | `virtualMachinesExecuteCreateFlex` |
| API Version     | `2026-03-01-preview`               |

Example:

```text
https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.ComputeSchedule/locations/eastus2euap/virtualMachinesExecuteCreateFlex?api-version=2026-03-01-preview
```

The location in the request URL must match the `baseProfile.location` value in the request body. Use the endpoint and `api-version` exactly as shown in the current preview example unless your environment has a newer published version available.

## Overview

At a high level, a Flex create request has three main parts:


| Field                      | Type   | Required | Description                                             |
| ---------------------------- | -------- | ---------- | --------------------------------------------------------- |
| `resourceConfigParameters` | object | Yes      | Defines the VM template and Flex placement inputs.      |
| `executionParameters`      | object | Yes      | Defines execution-time behavior such as retry settings. |

Within `resourceConfigParameters`, clients define a reusable base VM profile, a resource count and prefix, and a `flexProperties` block that controls SKU preference, priority mode, zone behavior, and Spot-specific settings.

## Resource Configuration

`resourceConfigParameters` is the main configuration block for the Flex create request.


| Field               | Type    | Required | Description                                                                      | Notes                                                                                                           |
| --------------------- | --------- | ---------- | ---------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `baseProfile`       | object  | Yes      | Defines the shared VM template used as the starting point for created resources. | Includes location, resource group, identity, compute, storage, OS, and network settings.                        |
| `resourceOverrides` | array   | Yes      | Per-resource override objects applied on top of`baseProfile`.                    | Scheduled Actions usage populates one object per resource, with observed fields including`name` and `location`. |
| `resourceCount`     | integer | Yes      | Number of resources to create.                                                   | Examples use a placeholder such as`{{resourceCount}}`.                                                          |
| `resourcePrefix`    | string  | Yes      | Prefix used to name created resources.                                           | Examples append a scenario suffix such as`-1`, `-2`, or `-14`.                                                  |
| `flexProperties`    | object  | Yes      | Defines Flex-specific placement and priority behavior.                           | This is the primary block for SKU, zone, and Spot selection.                                                    |

## Base Profile

`baseProfile` defines the base VM configuration before Flex-specific placement rules are applied. For more details about fields go to [Virtual Machines - Create Or Update](https://learn.microsoft.com/en-us/rest/api/compute/virtual-machines/create-or-update?view=rest-compute-2025-04-01&tabs=HTTP#create-a-custom-image-vm-from-an-unmanaged-generalized-os-image.).

## Flex Properties

`flexProperties` controls SKU preference, placement behavior, and pricing mode.


| Field                                         | Type    | Required | Supported values shown in examples                           | Description                                                                    |
| ----------------------------------------------- | --------- | ---------- | -------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| `capacityType`                                | string  | Yes      | `VM`                                                         | Capacity mode for the request.                                                 |
| `vmSizeProfiles`                              | array   | Yes      | One or more VM size entries                                  | Ordered list of preferred VM SKUs.                                             |
| `vmSizeProfiles[].name`                       | string  | Yes      | `Standard_D2ads_v5`, `Standard_E2ads_v5`, `Standard_D2ds_v5` | VM SKU name to consider.   max 10.                                             |
| `vmSizeProfiles[].rank`                       | integer | No       | `0`, `1`, `2`                                                | Allowed only when`priorityProfile.allocationStrategy` is `Prioritized`.        |
| `osType`                                      | string  | Yes      | `Windows`, `Linux`                                           | OS type aligned with the base profile image and OS settings.                   |
| `priorityProfile.type`                        | string  | Yes      | `Regular`                                                    | Capacity purchase model.                                                       |
| `priorityProfile.allocationStrategy`          | string  | Yes      | `Regular`: `Prioritized`, `LowestPrice`                      | Allowed values depend on`priorityProfile.type`.                                |
| `zoneAllocationPolicy.distributionStrategy`   | string  | No       | `Prioritized`, `BestEffortSingleZone`                        | If`zoneAllocationPolicy` is provided, top-level `zones` must also be provided. |
| `zoneAllocationPolicy.zonePreferences[].zone` | string  | No       | `1`, `2`, `3`                                                | Each zone must also appear in the top-level`zones` list.                       |
| `zoneAllocationPolicy.zonePreferences[].rank` | integer | No       | `0`, `1`, `2`, `3`                                           | Required for every`zonePreferences` entry when `zonePreferences` is supplied.  |

### Supported values shown in current examples

The examples supplied for this guide show the following value space. The validator adds the compatibility rules noted below.


| Field                                       | Values shown                          |
| --------------------------------------------- | --------------------------------------- |
| `priorityProfile.type`                      | `Regular`                             |
| `priorityProfile.allocationStrategy`        | `Prioritized`, `LowestPrice`          |
| `zoneAllocationPolicy.distributionStrategy` | `Prioritized`, `BestEffortSingleZone` |
| `osType`                                    | `Windows`, `Linux`                    |

Validator-enforced rules from the operation-layer input validator:

1. `priorityProfile.type` is required.
2. `priorityProfile.allocationStrategy` is constrained by priority type: `Regular` allows `LowestPrice` and `Prioritized`.
3. `vmSizeProfiles[].rank` can only be specified when allocation strategy is `Prioritized`.
4. If `zoneAllocationPolicy` is provided, top-level `zones` must also be provided.
5. If `distributionStrategy` is `Prioritized`, `zonePreferences` must be provided.
6. Each `zonePreferences[].zone` must also be present in the top-level `zones` list.

## Execution Parameters

`executionParameters` controls how the request is processed after submission.


| Field                              | Type    | Required | Description                                 | Example |
| ------------------------------------ | --------- | ---------- | --------------------------------------------- | --------- |
| `retryPolicy.retryCount`           | integer | Yes      | Number of retry attempts shown in examples. | `1`     |
| `retryPolicy.retryWindowInMinutes` | integer | Yes      | Retry window shown in examples.             | `60`    |

All supplied examples use the same `executionParameters` shape.

## How Configuration Choices Work

1. Choose whether the request is regional or zonal. Regional examples omit `baseProfile.zones`. Zonal examples include one or more explicit zones.
2. Choose one or more VM SKUs in `vmSizeProfiles`. Single-SKU scenarios use one entry, while multi-SKU scenarios include two or three entries.
3. Decide whether to include ranks. The validator only allows `vmSizeProfiles[].rank` when `priorityProfile.allocationStrategy` is `Prioritized`.
4. Choose `Regular` in `priorityProfile.type`.
5. Choose an allocation strategy compatible with the priority type. `Regular` supports `Prioritized` and `LowestPrice`. `Spot` supports `LowestPrice` and `CapacityOptimized`.
6. If zones matter, add top-level `zones`. If you also add `zoneAllocationPolicy`, the validator requires `zones` to be present.
7. If `zoneAllocationPolicy.distributionStrategy` is `Prioritized`, provide `zonePreferences`, and ensure each preference zone is also present in the top-level `zones` list.

## Representative Examples

This section includes a representative set of payloads that cover the main patterns.

### Example: regular, regional, prioritized, Windows, three SKUs

```json
{
  "resourceConfigParameters": {
    "baseProfile": {
      "location": "{{location}}",
      "ResourceGroupName": "{{resourceGroupName}}",
      "ComputeAPIVersion": "2025-04-01",
      "tags": {
        "<key>": "<value>"
      },
      "identity": { "type": "SystemAssigned" },
      "properties": {
        "hardwareProfile": { "vmSize": "Standard_D2ads_v5" },
        "storageProfile": {
          "imageReference": {
            "publisher": "MicrosoftWindowsServer",
            "offer": "WindowsServer",
            "sku": "2022-Datacenter",
            "version": "latest"
          },
          "osDisk": {
            "createOption": "FromImage",
            "managedDisk": {},
            "diskSizeGB": 127
          }
        },
        "osProfile": {
          "computerName": "saflexvm",
          "adminUsername": "testadmin",
          "adminPassword": "{{password}}",
          "windowsConfiguration": {}
        },
        "networkProfile": {
          "networkInterfaces": [
            {
              "id": "/subscriptions/{{subscriptionId}}/resourceGroups/{{resourceGroupName}}/providers/Microsoft.Network/networkInterfaces/{{nicName}}",
              "properties": { "primary": true }
            }
          ]
        }
      }
    },
    "resourceOverrides": [
      {
        "name": "vdi-vm-0",
        "location": "{{location}}"
      }
    ],
    "resourceCount": {{resourceCount}},
    "resourcePrefix": "vdi",
    "flexProperties": {
      "capacityType": "VM",
      "vmSizeProfiles": [
        { "name": "Standard_D2ads_v5", "rank": 0 },
        { "name": "Standard_E2ads_v5", "rank": 1 },
        { "name": "Standard_D2ds_v5", "rank": 2 }
      ],
      "osType": "Windows",
      "priorityProfile": {
        "type": "Regular",
        "allocationStrategy": "Prioritized"
      }
    }
  },
  "executionParameters": {
    "retryPolicy": {
      "retryCount": 1,
      "retryWindowInMinutes": 60
    }
  }
}
```

### Example: regular, regional, lowest price, linux, three SKUs

```json
{
  "resourceConfigParameters": {
    "baseProfile": {
      "location": "{{location}}",
      "ResourceGroupName": "{{resourceGroupName}}",
      "ComputeAPIVersion": "2022-11-01",
      "tags": {
        "azsecpack": "nonprod",
        "platformsettings.host_environment.service.platform_optedin_for_rootcerts": "true"
      },
      "identity": { "type": "SystemAssigned" },
      "properties": {
        "hardwareProfile": { "vmSize": "Standard_D2ads_v5" },
        "storageProfile": {
          "imageReference": {
            "publisher": "Canonical",
            "offer": "0001-com-ubuntu-server-jammy",
            "sku": "22_04-lts",
            "version": "latest"
          },
          "osDisk": {
            "createOption": "FromImage",
            "managedDisk": {},
            "diskSizeGB": 30
          }
        },
        "osProfile": {
          "computerName": "saflexvm",
          "adminUsername": "testadmin",
          "adminPassword": "{{password}}",
          "linuxConfiguration": {
            "disablePasswordAuthentication": false
          }
        },
        "networkProfile": {
          "networkInterfaces": [
            {
              "id": "/subscriptions/{{subscriptionId}}/resourceGroups/{{resourceGroupName}}/providers/Microsoft.Network/networkInterfaces/{{nicName}}",
              "properties": { "primary": true }
            }
          ]
        }
      }
    },
    "resourceOverrides": [
      {
        "name": "vdi-vm-0",
        "location": "{{location}}"
      }
    ],
    "resourceCount": {{resourceCount}},
    "resourcePrefix": "vdi",
    "flexProperties": {
      "capacityType": "VM",
      "vmSizeProfiles": [
        { "name": "Standard_D2ads_v5" },
        { "name": "Standard_E2ads_v5" },
        { "name": "Standard_D2ds_v5" }
      ],
      "osType": "Linux",
      "priorityProfile": {
        "type": "Regular",
        "allocationStrategy": "LowestPrice"
      }
    }
  },
  "executionParameters": {
    "retryPolicy": {
      "retryCount": 1,
      "retryWindowInMinutes": 60
    }
  }
}
```

## Response Shape

The sync path response returns an immediate scheduling result, not a final VM provisioning result. A successful sync response means the service accepted the request and created one or more operation records that should be tracked to terminal state.

### Example: Sync path response

The following sample is anonymized. Replace placeholder values with your own subscription, resource group, VM name, and operation ID.

```json
{
  "description": "Flex Create Resource request",
  "type": "VirtualMachines",
  "location": "eastus2euap",
  "results": [
    {
      "resourceId": "/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Compute/virtualMachines/{vmName}",
      "operation": {
        "operationId": "{operationId}",
        "resourceId": "/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Compute/virtualMachines/{vmName}",
        "opType": "Create",
        "subscriptionId": "{subscriptionId}",
        "deadline": "2026-04-07T00:00:15.483668+00:00",
        "deadlineType": "InitiateAt",
        "state": "PendingScheduling",
        "timeZone": "UTC",
        "retryPolicy": {
          "retryCount": 1,
          "retryWindowInMinutes": 90
        }
      }
    }
  ]
}
```

### Top-level response fields


| Field         | Type   | Description                                            | Notes                                                              |
| --------------- | -------- | -------------------------------------------------------- | -------------------------------------------------------------------- |
| `description` | string | Human-readable description of the request result.      | Example:`Flex Create Resource request`.                            |
| `type`        | string | Resource type category for the request.                | Example:`VirtualMachines`.                                         |
| `location`    | string | Scheduled Actions location that processed the request. | Typically matches the request location.                            |
| `results`     | array  | Per-resource scheduling results.                       | One entry is returned for each resource accepted by the sync path. |

### `results[]` fields


| Field          | Type   | Description                                          | Notes                                                          |
| ---------------- | -------- | ------------------------------------------------------ | ---------------------------------------------------------------- |
| `resourceId`   | string | ARM ID of the VM associated with this result.        | Present in the success-shaped sync response you provided.      |
| `errorCode`    | string | Resource-level error code.                           | Present only when the individual resource result has an error. |
| `errorDetails` | string | Resource-level error details.                        | Present only when the individual resource result has an error. |
| `operation`    | object | Operation tracking record created for this resource. | Use`operation.operationId` to poll for terminal state.         |

### `results[].operation` fields


| Field                    | Type   | Description                                        | Notes                                                                                                             |
| -------------------------- | -------- | ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `operationId`            | string | Unique ID for the scheduled create operation.      | Primary tracking key for follow-up status calls.                                                                  |
| `resourceId`             | string | ARM ID of the VM tied to the operation.            | Usually matches`results[].resourceId`.                                                                            |
| `opType`                 | string | Operation type.                                    | Sync create responses are expected to return`Create`.                                                             |
| `subscriptionId`         | string | Subscription associated with the operation.        | Copied into the operation record for tracking.                                                                    |
| `deadline`               | string | Timestamp associated with the scheduled operation. | Returned as an offset datetime.                                                                                   |
| `deadlineType`           | string | Deadline interpretation.                           | Current models define values such as`InitiateAt`.                                                                 |
| `state`                  | string | Current operation state.                           | On the sync path this can be`PendingScheduling`; terminal states (Succeeded, Cancelled, Failed) are reached later. |
| `timeZone`               | string | Time zone used for the operation deadline.         | Example:`UTC`.                                                                                                    |
| `retryPolicy`            | object | Retry policy captured on the operation.            | Reflects the operation record, not necessarily the exact request payload if the service applies defaults.         |
| `resourceOperationError` | object | Operation-level error details.                     | May appear on later status responses if the operation fails.                                                      |
| `completedAt`            | string | Completion timestamp.                              | Typically absent on the initial sync response and present only after completion.                                  |

### How to interpret the sync response

1. Treat the response as acceptance and scheduling metadata, not final VM creation success.
2. Persist every `results[].operation.operationId` returned by the sync call.
3. Use the operation status API to poll until each operation reaches a terminal state such as `Succeeded`, `Failed`, or `Cancelled`.
4. Do not assume `PendingScheduling` means the VM already exists and is fully provisioned.
5. Check `results[].errorCode` or later `operation.resourceOperationError` fields to diagnose per-resource failures.

## Field Interpretation and Client Guidance


| Question                                     | Guidance                                                                                                              |
| ---------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| When should I include`zones`?                | Include`baseProfile.zones` when you want a zonal request. Regional examples omit it entirely.                         |
| When should I include`zoneAllocationPolicy`? | Include it when zone distribution or zone ordering matters. Current examples only show this block in zonal scenarios. |
| When is`vmSizeProfiles[].rank` useful?       | Ranked entries appear in prioritized examples and help express preference order across multiple SKUs.                 |
| How do Windows and Linux requests differ?    | Update`imageReference`, `osProfile`, `osDisk.diskSizeGB`, and `flexProperties.osType` together.                       |
| What does`maxPricePerVM = -1` mean?          | The validator treats`-1` as the sentinel for no price cap.                                                            |

## Summary


| Concept               | Description                                                                                |
| ----------------------- | -------------------------------------------------------------------------------------------- |
| `baseProfile`         | Defines the reusable VM template including compute, storage, OS, identity, and networking. |
| `flexProperties`      | Defines placement, SKU preference, priority mode, zone policy, and Spot behavior.          |
| `executionParameters` | Defines execution-time retry settings.                                                     |

## Common Questions

**Q: Can I omit `rank` from every `vmSizeProfiles` entry?**

Yes, unless `priorityProfile.allocationStrategy` is `Prioritized`. The validator only permits `rank` when the allocation strategy is `Prioritized`.

**Q: Do I need `zoneAllocationPolicy` whenever I specify zones?**

No. But if you do specify `zoneAllocationPolicy`, the validator requires top-level `zones` to be present. If the distribution strategy is `Prioritized`, `zonePreferences` are also required.

**Q: Should Windows and Linux use the same `osProfile` shape?**

No. Windows examples use `windowsConfiguration`, while Linux examples use `linuxConfiguration.disablePasswordAuthentication`.

**Q: What fields change between regional and zonal requests?**

Zonal examples add `baseProfile.zones`, and some also add `flexProperties.zoneAllocationPolicy`. Regional examples omit both.
