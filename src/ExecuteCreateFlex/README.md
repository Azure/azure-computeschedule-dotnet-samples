# ExecuteCreateFlex API Demo (ComputeSchedule)

This demo is an API-first walkthrough for external customers.
It focuses on **ComputeSchedule API usage** for `ExecuteCreateFlex` and intentionally skips most project boilerplate.

## 1) What this demo shows

- How to build `ExecuteCreateFlexContent`
- How to call `VirtualMachinesExecuteCreateFlexAsync(...)`
- How to poll operation status with `GetVirtualMachineOperationStatusAsync(...)`
- How to summarize success/failure outcomes


## 2) Why flex create?
Fixed-shape VM creation can fail when a specific instance type is temporarily constrained in a zone or region. Flex create is better because it asks for **capacity intent** (VM Size) rather than one exact shape.

- **Better reliability under capacity pressure**: if the top size is unavailable, the platform can place the workload on the next acceptable size instead of failing fast.
- **Faster time to provision**: fewer retries and less manual fallback logic when scaling out.
- **More resilient autoscaling**: scale events are less brittle because provisioning does not depend on a single SKU.

In short, normal create optimizes for exact shape matching, while flex create optimizes for successful placement and operational continuity.

## 3) Prerequisites

- Azure subscription + resource group + VNet/subnet
- Azure identity available to `DefaultAzureCredential`
- Package:
  - `Unofficial.Azure.ResourceManager.ComputeSchedule` `1.2.0-alpha.20260401.1` (caveat:)

## 4) Minimal API sequence (direct SDK usage)

```csharp
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ComputeSchedule;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;

// ---- Inputs ----
var subscriptionId = "<subscription-id>";
var location = "<azure-location>";
var resourceGroupName = "<resource-group-name>";
var subnetId = "/subscriptions/.../resourceGroups/.../providers/Microsoft.Network/virtualNetworks/.../subnets/...";

TokenCredential credential = new DefaultAzureCredential();
var armClient = new ArmClient(credential, subscriptionId);
var subscription = armClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(subscriptionId));

// 1) Build execution params : REMOVE
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
        // you can use the rank field for specifying sku priority
        new VmSizeProfile(vmSize: "Standard_D2ads_v5", rank: 0), // primary VM size
        new VmSizeProfile(vmSize: "Standard_E4as_v5", rank: 1),  // backup VM size
        // we can add more
    },
    OsType.Windows,
    // explain this part fully
    new PriorityProfile
    {
        Type = PriorityType.Regular,
        AllocationStrategy = AllocationStrategy.Prioritized,
    });

// 3) Build payload (focus on this more)
var payload = new ResourceProvisionFlexPayload(resourceCount: 1, flexProperties)
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
        computerName = "demovm01", // <= 15 chars for Windows
        adminUsername = "<admin-username>",
        adminPassword = "<admin-password>"
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
            deleteOption = "Detach", // we can also use "Delete"
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

// Optional: per-resource override
payload.ResourceOverrides.Add(new Dictionary<string, BinaryData>
{
    ["name"] = BinaryData.FromString("\"demovm01\""),
    ["location"] = BinaryData.FromString($"\"{location}\""),
    ["properties"] = BinaryData.FromObjectAsJson(new
    {
        osProfile = new
        {
            computerName = "demovm01",
            adminUsername = "<admin-username>",
            adminPassword = "<admin-password>"
        }
    })
});

// 4) Build request wrapper - don't show this part
var request = new ExecuteCreateFlexContent(payload, executionParams)
{
    CorrelationId = Guid.NewGuid().ToString()
};

// 5) Execute API
CreateFlexResourceOperationResult result =
    await subscription.VirtualMachinesExecuteCreateFlexAsync(location, request);

// 6) Poll operation status // just use a wrapper (unify polling)
var opIds = result.Results
    .Where(r => r.ErrorCode == null && r.Operation.State != ScheduledActionOperationState.Blocked)
    .Select(r => r.Operation.OperationId)
    .ToHashSet();

while (opIds.Count > 0)
{
    var status = await subscription.GetVirtualMachineOperationStatusAsync(
        location,
        new GetOperationStatusContent(opIds, Guid.NewGuid().ToString()));

    var completed = status.Results
        .Where(r => r.Operation.State == ScheduledActionOperationState.Succeeded
                 || r.Operation.State == ScheduledActionOperationState.Failed
                 || r.Operation.State == ScheduledActionOperationState.Cancelled)
        .ToList();

    // summarize
    var succeeded = completed.Count(r => r.Operation.State == ScheduledActionOperationState.Succeeded);
    var failed = completed.Count(r => r.Operation.State == ScheduledActionOperationState.Failed);
    var cancelled = completed.Count(r => r.Operation.State == ScheduledActionOperationState.Cancelled);

    Console.WriteLine($"Completed={completed.Count}, Succeeded={succeeded}, Failed={failed}, Cancelled={cancelled}");

    // keep only in-progress operations
    opIds = status.Results
        .Where(r => r.Operation.State != ScheduledActionOperationState.Succeeded
                 && r.Operation.State != ScheduledActionOperationState.Failed
                 && r.Operation.State != ScheduledActionOperationState.Cancelled)
        .Select(r => r.Operation.OperationId)
        .ToHashSet();

    if (opIds.Count > 0)
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
    }
}
```

## 5) Required (API)

- `ScheduledActionExecutionParameterDetail`
- `ResourceProvisionFlexPayload` (+ `FlexProperties`)
- `ExecuteCreateFlexContent`
- `VirtualMachinesExecuteCreateFlexAsync(...)`
- `GetVirtualMachineOperationStatusAsync(...)`

## 6) Demo talk track

1. We build one `ExecuteCreateFlexContent` request with prioritized VM sizes.
2. We submit with `VirtualMachinesExecuteCreateFlexAsync`.
3. We poll with `GetVirtualMachineOperationStatusAsync` until terminal states.
4. We report `Succeeded/Failed/Cancelled` counts and failed error details.
5. We show a demo with 1000 VMs batched

