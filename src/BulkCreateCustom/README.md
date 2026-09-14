# BulkCreateCustom: create 100 + 50 VMs

A standalone .NET 10 sample using `Azure.ResourceManager.Compute.BulkActions`
**1.2.0-beta.2**. It submits two concurrent requests: **Batch A (100 VMs)** and
**Batch B (50 VMs)**, then monitors their per-VM outcomes. A separate command
previews or submits Bulk Delete for Batch B's 50 VMs.

> Normal execution submits creation of **150 billable Windows VMs**, NICs and
> managed OS disks. There is no create preview or automatic cleanup. Confirm
> authorization, quota, capacity, budget and a cleanup owner before running.
> `--validate` is offline; delete and PowerShell previews read Azure.

## Quickstart

Run these commands from the repository root in PowerShell. You need the .NET 10
SDK, PowerShell 7 for scripts, Azure CLI signed in with `az login`, and an
enabled subscription/region with the required RBAC and provider registrations.
Review the [full prerequisites](docs/reference.md#configuration-and-prerequisites):
allow at least 150 free subnet IPs, sufficient VM-family/regional quota
(up to 1,200 vCPUs), eligible sizes/zones and a verified Windows image version.

### 1. Configure locally

```powershell
# Only on a fresh clone; do not overwrite an existing local config.
if (!(Test-Path src\BulkCreateCustom\config.json)) {
    Copy-Item src\BulkCreateCustom\config.example.json src\BulkCreateCustom\config.json
}
```

Edit `src\BulkCreateCustom\config.json`: replace placeholders for subscription,
resource group, region, subnet, zones, size candidates and administrator
credentials. Verify `imageVersion` and `imageMinimumOSDiskGB` against the image.
The example's empty `adminPassword` must be set locally.

**The password is plaintext in this gitignored file.** Restrict file access,
never commit/share it, and keep the example password-free. No `.env` is loaded.
The .NET default is the **source project directory's config**, regardless of
working directory; scripts use that same file, one directory above `scripts`.
`--config <path>` (or PowerShell `-ConfigPath`) overrides it. Rebuild after moving
the source tree; published executables on another machine need `--config`.

### 2. Prepare the group and network

Skip creation if the configured resources already exist and meet the prerequisites.
Preview each target first; `-Execute` permits changes subject to `ShouldProcess`.

```powershell
.\src\BulkCreateCustom\scripts\Create-ResourceGroup.ps1
.\src\BulkCreateCustom\scripts\Create-ResourceGroup.ps1 -Execute
.\src\BulkCreateCustom\scripts\Create-Network.ps1
.\src\BulkCreateCustom\scripts\Create-Network.ps1 -Execute
```

The network script follows the full `subnetId`; if its resource group differs
from the VM group, that network group must already exist. It leaves matching
networks unchanged and refuses conflicting ones. Defaults are VNet
`10.0.0.0/16` and subnet `10.0.8.0/21`, with default outbound access disabled.
It does **not** provide public IPs, NAT or complete connectivity/security.
Arrange approved egress/private access separately. See [network reference](docs/reference.md#create-the-configured-network).

### 3. Validate offline, then create 100 + 50

```powershell
dotnet build src\BulkCreateCustom\BulkCreateCustom.csproj
dotnet run --project src\BulkCreateCustom\BulkCreateCustom.csproj --no-build -- --validate

# LIVE: submits both batches, after separate deployment approval.
dotnet run --project src\BulkCreateCustom\BulkCreateCustom.csproj
```

`--validate` runs all C# validation under `tests` with synthetic configuration
and fake transports, without credentials or Azure calls. It does not prove
live capacity, deployment success or applied VM properties.

The normal console shows concise progress and outcomes. Add `-- --verbose`
to the normal `dotnet run` command for full redacted JSON and per-VM console
details. **Full redacted logs are always saved**, even without `--verbose`;
the executable prints their location.

### 4. Inspect the overrides and outcomes

| Batch | What to inspect in the redacted request/log |
|---|---|
| A: 100 | Unique VM resource names and Windows computer names in the per-VM overrides |
| B: 50 | Per-size OS disk settings in `vmSizesProfile[].override.virtualMachineProfile.storageProfile.osDisk`, plus 50 identity-only per-VM entries |

Read [`RequestBuilder.cs`](RequestBuilder.cs): precedence is **base < per-size
< per-VM**. When supplied, the per-VM override array must match capacity; it
is not universally mandatory in the schema. Batch B currently supplies all 50
names, not 50 repeated disk overrides. Allocation owns size/zone/priority.

Save Batch B's **bulk-resource UUID** from `/bulkCreateCustom/<UUID>` for deletion;
do not use the async-operation UUID. Request acceptance is not VM completion.
Reported disk/computer-name expectations are not live GET verification: inspect
the actual VMs/disks in Azure before claiming the overrides were applied.
See [request details](docs/reference.md#request-shape) and [logging](docs/reference.md#output-for-later-resource-inspection).

### 5. Preview, then delete Batch B's 50 VMs

```powershell
# Read-only Azure discovery; inspect all 50 IDs and the redacted request.
dotnet run --project src\BulkCreateCustom\BulkCreateCustom.csproj -- --delete-batch-b "<batch-b-resource-uuid>" --verbose

# LIVE: submit only after reviewing the preview.
dotnet run --project src\BulkCreateCustom\BulkCreateCustom.csproj -- --delete-batch-b "<batch-b-resource-uuid>" --execute
```

Both forms support `--config <path>` and `--verbose`; deletion does not require
the administrator password. The command requires a successful, tagged Batch B
with 50 matching successful VM results. It polls separate Bulk Delete operation
IDs and saves a receipt beside the log. **Batch A remains billable.** The group,
network and creation record are not targeted; retained disks/NICs may also remain.
See [Bulk Delete safety and receipts](docs/reference.md#bulk-delete-the-second-create-operations-vms).

**Do not blindly rerun after an error or interruption.** Each create run has new
names; neither create nor Bulk Delete reruns resume earlier work. Ctrl+C/timeouts
stop local monitoring, not Azure operations. Investigate saved IDs/logs/receipts.

## Flow and reading order

```text
config.json -> scripts\Create-ResourceGroup.ps1 -> scripts\Create-Network.ps1
                         |
Program.cs -> CreateDemo.cs -> RequestBuilder.cs -> Batch A100 + Batch B50
          \-> DeleteDemo.cs ----------------------> preview / delete Batch B50
Support\ = CLI, logging, SDK adapter and polling helpers
tests\   = offline C# validation and mocked PowerShell checks
docs\    = detailed reference, troubleshooting and broader cleanup
bruno\   = separate HTTP client/configuration; Batch A create only
```

Read [Program.cs](Program.cs), [DemoConfig.cs](DemoConfig.cs),
[CreateDemo.cs](CreateDemo.cs), [RequestBuilder.cs](RequestBuilder.cs), then
[DeleteDemo.cs](DeleteDemo.cs). The implementation class names remain
`BulkCreateDemo`, `BulkCreateRequestBuilder` and `BulkDeleteDemo`.
The create flow explicitly calls `BuildPerVmRequest` for A and
`BuildPerSizeRequest` for B before running both batches. Consult
[Support](Support) for execution, logging and CLI helpers, then
[tests](tests) for offline validation.
The create SDK submission and pageable status calls are in
[`Support\SdkBulkCreateClient.cs`](Support/SdkBulkCreateClient.cs);
the Bulk Delete submission is visible directly in `DeleteDemo.cs`.

- [Detailed reference and troubleshooting](docs/reference.md)
- [Separate Bruno collection](bruno/README.md): independent configuration,
  Batch A create only, no requests until explicitly sent.

## Choose the right cleanup scope

All deletion commands preview by default. Review the targets before adding
`--execute` (.NET) or `-Execute` (PowerShell). See the [cleanup reference](docs/reference.md#cleanup-all-resources-in-configjsons-resource-group).

| Command | Deletes | Preserves |
|---|---|---|
| .NET `--delete-batch-b <UUID>` | Batch B's 50 VMs; dependencies follow VM delete options | Batch A, group, VNet, create-operation record |
| `scripts\Cleanup.ps1 -OperationName <UUID>` | One completed demo operation's VMs and identified disks/NICs, then its record | Group, shared networking, unrelated resources |
| `scripts\Cleanup.ps1` | All listed resources inside the configured group | The resource group itself |
| `scripts\Delete-ResourceGroup.ps1` | The configured group **and everything inside** | Resources outside the group |

Run the mocked PowerShell checks independently (no Azure calls/deletions):

```powershell
pwsh -NoProfile -File src\BulkCreateCustom\tests\Create-ResourceGroup.Tests.ps1
pwsh -NoProfile -File src\BulkCreateCustom\tests\Create-Network.Tests.ps1
pwsh -NoProfile -File src\BulkCreateCustom\tests\Cleanup.Tests.ps1
pwsh -NoProfile -File src\BulkCreateCustom\tests\Delete-ResourceGroup.Tests.ps1
```
