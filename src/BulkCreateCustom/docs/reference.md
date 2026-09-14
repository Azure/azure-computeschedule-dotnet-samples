# BulkCreateCustom reference

[Back to the customer quickstart](../README.md). Commands below run from the
repository root. This reference covers request details, operational failure
handling, and the separate, broader PowerShell cleanup tools.

## Find a topic

- [Request shape and override precedence](#request-shape)
- [Configuration and prerequisites](#configuration-and-prerequisites)
- [Build and offline validation](#build-and-offline-validation)
- [Live execution and failure handling](#live-execution-creates-resources)
- [Logs and later inspection](#output-for-later-resource-inspection)
- [Resource group and network setup](#create-the-configured-network)
- [Bulk Delete of Batch B](#bulk-delete-the-second-create-operations-vms)
- [Broader cleanup choices](#cleanup-all-resources-in-configjsons-resource-group)
- [Troubleshooting](#troubleshooting)

## Scope

This standalone .NET 10 sample pins `Azure.ResourceManager.Compute.BulkActions`
**1.2.0-beta.2**. It does not reference `Common` or change the legacy samples.
Normal execution **creates 150 billable Windows VMs (Batches A and B)**, their NICs,
and managed OS disks in an existing resource group/subnet. It performs no cleanup.
`--validate` is offline. The separate `--delete-batch-b` command previews a VM
deletion using Azure reads unless `--execute` is explicitly supplied.

For a direct HTTP version of Batch A, open the [Bruno collection](../bruno/README.md).
It uses the same request shape and does not send anything until you click Send.

Both batches are built and submitted concurrently in a normal .NET run.
Full redacted request JSON is always logged; `--verbose` also prints it.
The separate Bruno collection still sends Batch A only.

## Request shape

| Batch | Capacity (`VM`, not vCPU) | Overrides |
|---|---:|---|
| A | 100 | Explicit unique VM resource names and Windows computer names |
| B | 50 | Native `vmSizesProfile[].override.virtualMachineProfile.storageProfile.osDisk` settings, selected by allocated VM size |

**Batch B also has 50 identity-only per-VM entries.** When a per-VM override array
is supplied, its count must match capacity; the array is not universally
mandatory in the schema. These are not 50 repeated disk overrides.
Both batches explicitly name all VMs, so `virtualMachineNamePrefix`
is omitted (the service rejects it when all entries are named).

[`RequestBuilder.cs`](../RequestBuilder.cs) (`BulkCreateRequestBuilder`) shows the common profile followed by the per-size
and per-VM overlays. The precedence is **base < per-size < per-VM**. Neither the
base profile nor any override sets VM size, zone, or VM priority: allocation owns
these. Per-VM entries contain only name/computer-name fields, leaving credentials,
network, image and size-specific disk settings inherited from lower layers.
The primary create flow calls `BuildPerVmRequest` for Batch A and
`BuildPerSizeRequest` for Batch B explicitly, without a boolean mode selector.
Execution/polling and result accounting live under [Support](../Support);
offline C# validation lives under [tests](../tests) and runs through `--validate`.

Both requests use regular priority, **Prioritized** VM-size allocation with
explicit contiguous ranks starting at zero, and **BestEffortBalanced** zones.
Configured zones are set on the request's top-level `zones` array. No
prioritized-only zone preferences are set. Balancing is best-effort, not an
equal-per-zone guarantee.

Default examples (confirm availability before a live run):

| Rank | Candidate | vCPUs | Batch B OS disk |
|---:|---|---:|---:|
| 0 | Standard_D4s_v5 | 4 | 256 GiB |
| 1 | Standard_D8s_v5 | 8 | 320 GiB |
| 2 | Standard_E4s_v5 | 4 | 384 GiB |
| 3 | Standard_E8s_v5 | 8 | 448 GiB |

The allocator need not select every candidate. An observed VM's size determines
which disk override should apply. Batch A uses `imageMinimumOSDiskGB` throughout.
The sample allows up to ten distinct 4/8-vCPU D/E `s`, `ds`, `as` or `ads` v5
size candidates and validates distinct disks above the verified image minimum.
For example, the four defaults plus D4ds, D8ds, D4as, D8as, D4ads and D8ads v5
form a ten-entry configuration; each must be eligible in your environment.
**Ten is a sample safety cap, not a verified service maximum.** Adding other
families requires reviewing image, architecture, generation and disk
compatibility and adjusting validation. No 10,000-VM capacity/concurrency
guarantee is made.

The service retry policy contains **only `retryWindowInMinutes: 5`**.
This uses a five-minute retry window; no optional retry count or failure action
is added. The SDK's optional integer does not itself validate service-side
limits. Regional service acceptance remains a live-run prerequisite.

## Configuration and prerequisites

Edit `src\BulkCreateCustom\config.json`, replacing every placeholder. This local
file is gitignored. On a fresh clone, create it from the example:

```powershell
Copy-Item src\BulkCreateCustom\config.example.json src\BulkCreateCustom\config.json
```

With no arguments, the sample reads `src\BulkCreateCustom\config.json` directly,
matching the PowerShell scripts. The project directory is recorded in assembly
metadata at build time, so the terminal's working directory does not affect it.
The full config path is printed at startup. Config changes take effect on the
next run even with `--no-build`; the file is no longer copied to build/publish
output and any old `bin` copy is ignored.
Rebuild once to pick up this behavior, and rebuild if you move the source tree.
For a published executable on another machine, supply `--config <path>` explicitly.
That option also overrides the default locally; relative paths are resolved from
the terminal's working directory.
Unknown JSON properties are rejected. Set `adminPassword` in the local config
before creating VMs; the example leaves it empty intentionally.
No `.env` file is automatically loaded.

Before a separately authorized live run, confirm an enabled subscription/region
for the custom endpoint, provider registration and RBAC for the operation,
VMs, disks and NICs, and subnet join permission. The VM resource group and subnet
must already exist in the selected region; this sample restricts the subnet to
the same subscription. Confirm the configured zones and every size are eligible
for that subscription, region and zone combination.

Reserve at least 150 free subnet IP addresses (in addition to Azure's reserved
addresses and existing usage), sufficient regional and VM-family quota for the
selected mix (up to 1,200 vCPUs for 150 eight-core VMs), disk/NIC quota, budget
and capacity. Quota does not guarantee capacity. Partial fulfillment is explicitly
disabled, but later individual provisioning failures can still leave resources.

The fixed image is `MicrosoftWindowsServer:WindowsServer:2022-datacenter-azure-edition`.
Set `imageVersion` to a real, verified version rather than `latest`, and set
`imageMinimumOSDiskGB` to its verified minimum (the example's 127 GiB is not an
image metadata lookup). All per-size disks must be larger than that base disk
and at most 4095 GiB. Confirm image availability and disk/VM compatibility.
Windows username/password policies are checked locally but final image/service
validation remains authoritative.

The shared Windows profile explicitly sets `patchMode: AutomaticByOS` and
`assessmentMode: AutomaticByPlatform`, with automatic updates and the VM agent
enabled. This preserves OS-managed update installation while enabling Azure
periodic patch assessment. Both the .NET and Bruno requests include these
settings; they are inherited by the per-VM name overrides. Other subscription
policies and guest connectivity requirements must still be satisfied.

Authentication uses `DefaultAzureCredential`, e.g. an existing `az login`
session or a properly configured workload identity. The pinned SDK's transitive
`Azure.Core` 1.62.0 already supplies the `Azure.Identity` namespace/types; a
separate older Azure.Identity package introduces duplicate types and is not needed.
Supply the VM administrator password in `adminPassword` in your local
`src\BulkCreateCustom\config.json`. `BULK_VM_ADMIN_PASSWORD` is no longer read.
The local config is gitignored and is not copied to build/publish output.
This password is plaintext on disk: restrict access to the file, do not share it,
and never force-add it to Git. Keep `config.example.json` password-free. Escape
quotes/backslashes using JSON syntax. The request preview and logs redact the
password. Do not put Azure access tokens or client secrets in this config.
Bruno still uses its own secret environment variables, independently of this file.

## Build and offline validation

From the repository root in PowerShell:

```powershell
dotnet build src\BulkCreateCustom\BulkCreateCustom.csproj
dotnet run --project src\BulkCreateCustom\BulkCreateCustom.csproj --no-build -- --validate
```

If a machine-level NuGet configuration disables the repository's nuget.org feed:

```powershell
dotnet restore src\BulkCreateCustom\BulkCreateCustom.csproj --source https://api.nuget.org/v3/index.json
dotnet build src\BulkCreateCustom\BulkCreateCustom.csproj --no-restore
```

`--validate` uses synthetic configuration, SDK wire serialization/model factories
and an in-memory fake client. It does not construct a real Azure credential or
contact Azure. It asserts both batches are enabled by default, explicit single-batch
selection still works, exact counts and override locations, 150 unique names,
Windows length boundaries, UUID operation names, ten-candidate configuration,
ranks, top-level zones, retry values, required input validation and credential
redaction. A submission barrier proves both calls begin concurrently;
scenarios cover success, one submission failing, partial failure/cancellation,
one poll failing, timeout, local cancellation and malformed status data.
Additional SDK adapter checks use a synthetic token and an HTTP handler that
never opens a socket. They exercise concurrent PUTs, the pinned
`2026-08-06-preview` route, per-VM success/failure, HTTP 409 rejection with an
accepted sibling, and two-page per-VM status responses.
Regressions also cover pending VM operations, temporary resource/status 404s,
delayed per-VM results, recoverable status-service errors, bounded persistent
404s, and a missing second status page. The stub rejects any attempt to poll
the async-operation URL: progress comes from the custom per-VM endpoint.
These checks cannot establish real capacity, applied overrides or service acceptance.

## Live execution (creates resources)

Only after separate deployment approval, environment configuration and sign-in:

```powershell
dotnet run --project src\BulkCreateCustom\BulkCreateCustom.csproj
```

From `src\BulkCreateCustom`, simply use `dotnet run`. Both commands use the
sample's local `config.json`. There is no preview default. Missing/invalid configuration exits before
submission. Each run generates new names, so rerunning creates another batch;
it does **not** resume the previous run.

[`CreateDemo.cs`](../CreateDemo.cs) (`BulkCreateDemo`) starts both batches before awaiting their completion.
It logs both final SDK request bodies as indented JSON before either submission;
`--verbose` also displays them in the console.
Passwords and secret-bearing fields are replaced with
`[REDACTED]` in a separate JSON copy; the requests sent to Azure are unchanged.
The SDK calls `GetLocationBasedBulkCreateCustoms(region).CreateOrUpdateAsync`
with `WaitUntil.Started`, then directly polls
the per-VM status endpoint. The submission log includes the initial response's
HTTP status from `operation.GetRawResponse().Status`; this is not VM completion.
Monitoring uses
`VirtualMachinesGetOperationStatusAsync` (including all pages). The returned
async-operation ID is logged for investigation only; `UpdateStatusAsync` and
the `/asyncOperations/` endpoint are not used for monitoring. Each cycle also
reads the bulk resource's provisioning state to detect terminal batch failure.
It polls
every ten seconds until all 150 per-VM results are terminal and accounted for.
Missing results or known transient `BulkActionNotFoundException`/`ResourceNotFound`
404s after acceptance cause another observation attempt, not a new create request
or premature success. Temporary 408/429/500/502/503/504 errors during observation
are also retried within the same local deadline.

Acceptance is not reported as successful VM creation. A terminal failed/canceled
bulk resource stops that batch's polling with failure rather than waiting for VMs
that might never be created. Per-VM error codes and messages are read from
both top-level and nested operation errors. Missing status remains unknown,
not successful; failure of one selected batch does not cancel another.
An `InternalExecutionError` is a server-reported terminal failure, not something
client polling can repair. Investigate the async operation and Azure Activity
Log using the printed IDs; never assume that a generic backend error means no
resources were created.

The sample disables automatic HTTP retries to avoid blind create replay; the
five-minute **service** retry window is separate from the local polling deadline
(`pollTimeoutMinutes`, default 30, covering submission and observation). Ctrl+C
or a deadline stops local observation, **not** Azure work. No Cancel/Delete API is
called. A transport error can leave acceptance unknown. Use the operation
resource IDs printed before submission to investigate; do not blindly rerun.

Exit codes: `0` only if both operations succeed and all 150 per-VM results are
successful; `1` for incomplete/failing/unknown outcomes; `2` for invalid usage
or configuration. Each batch prints succeeded, failed, cancelled, pending and
unreported counts, plus fulfilled capacity when supplied. Missing results are
explicitly unreported, not invented rejection/failure counts. Rejected requests
report their HTTP/error code; absent per-VM outcomes remain unknown.

## Output for later resource inspection

VM resource names are `<prefix>-<32-character-run-id>-<batch>-<index:000>`.
Windows computer names use the first prefix letter, a random ten-character
run token, the batch letter and a three-digit index (exactly 15 characters).
No suffix is truncated. Operation resource names are distinct UUIDs as required
by the custom endpoint; correlation tags are also distinct per batch. The SDK
supplies its normal HTTP request identifiers.

The default console summarizes progress and operation/resource IDs. The full
redacted log, and the console with `--verbose`, include each returned VM's state, size,
zone, **expected** computer name and **expected** OS disk size. Expected values
are labeled as such: this sample does not GET the created Compute VM/disks to
assert applied properties. These IDs and mappings support later portal evidence.
After configuration validation, every live run creates a unique
`bulk-create-<UTC timestamp>-<GUID>.log` beside the executable and prints its path.
For a default Debug build this is `src\BulkCreateCustom\bin\Debug\net10.0`.
The file always receives the full redacted request JSON and status output,
regardless of `--verbose`, flushed after each write so it can be read while polling continues.
The two batches share synchronized logging. Existing `.gitignore` rules exclude
`.log` files. Configuration errors before the log is opened remain console-only;
failure to open the log prevents submission. Keep logs secure because they still
contain resource IDs and infrastructure details.
The offline no-file logging harness retains full output so validation can inspect
all details; concise console filtering applies to normal file-backed execution.

Only the explicitly redacted request preview should be logged; raw
serialized requests contain the administrator password. The sample keeps SDK
HTTP body logging disabled and omits exception messages.
Cleanup ownership and any live verification must be arranged separately.

## Create the configured network

If the configured resource group is missing, create it first:

```powershell
# Preview the subscription, group and metadata location from config.json.
.\src\BulkCreateCustom\scripts\Create-ResourceGroup.ps1

# Create only the resource group, leaving existing groups unchanged.
.\src\BulkCreateCustom\scripts\Create-ResourceGroup.ps1 -Execute

# Then create the configured network.
.\src\BulkCreateCustom\scripts\Create-Network.ps1 -Execute
```

`Create-ResourceGroup.ps1` reads `subscriptionId`, `resourceGroup`, and `region`
from `config.json` in the parent project directory. It supports `-ConfigPath`, `-WhatIf`, and
timestamped status messages. It does not create networks/VMs or change existing
group tags/location. If `subnetId` refers to a different network resource group,
that separate group must also exist; this script creates only `resourceGroup`.
Policy and authorization errors are surfaced without bypassing them.
Offline checks: `pwsh -NoProfile -File src\BulkCreateCustom\tests\Create-ResourceGroup.Tests.ps1`.

`Create-Network.ps1` reads `config.json` in the parent project directory. The full `subnetId`
determines the subscription, network resource group, VNet name and subnet name;
`region` determines the location. The network resource group must already exist.
It may differ from the VM `resourceGroup`; the script warns about that and follows
`subnetId` without modifying the configuration.

```powershell
# Preview only.
.\src\BulkCreateCustom\scripts\Create-Network.ps1

# Create the missing VNet/subnet.
.\src\BulkCreateCustom\scripts\Create-Network.ps1 -Execute
```

Defaults are VNet `10.0.0.0/16` and subnet `10.0.8.0/21` (2,043 usable IPv4
addresses before existing usage). Override with `-VnetAddressPrefix` and
`-SubnetAddressPrefix`; select non-overlapping ranges suitable for your network.
`-ConfigPath` selects another configuration. `-Execute -WhatIf` previews.

To replace a full `/24` for future runs, set `subnetId` in the source config to
a new subnet name (for example `flex-subnet2`) in the same VNet, then preview
and execute this script. It creates the new `/21` only if it fits and does not
overlap existing subnets. It never resizes or deletes the old subnet, moves
existing VMs, or cleans up retained NICs. Update Bruno's separate environment
if using that client. Larger address space still requires cleanup between runs;
the nominal capacity is not a live free-IP guarantee.

New subnets explicitly set `defaultOutboundAccess: false`. No public IP, NAT
gateway, firewall, NSG, peering or resource group is created. Arrange approved
egress for guest updates/assessment and private connectivity as needed; this
script does not establish complete network security or connectivity.

An existing matching network is left unchanged. The script refuses a conflicting
region, address range, delegated subnet, or subnet with default outbound access
enabled. It does not overwrite existing networking. New subnet prefixes are
checked against existing IPv4 subnets for overlap. Creation errors are surfaced;
if a partial creation succeeds, rerunning inspects what already exists.
It waits up to ten minutes each for the VNet and subnet to become ready.

Offline checks (mocked Azure CLI):

```powershell
pwsh -NoProfile -File src\BulkCreateCustom\tests\Create-Network.Tests.ps1
```

## Bulk Delete the second create operation's VMs

This demonstrates **Bulk Delete**, not deletion of the BulkCreateCustom resource.
Creation still submits A and B concurrently; deletion is a separate explicit step.
Use Batch B's resource UUID from the printed `/bulkCreateCustom/<UUID>` URL, not
its asynchronous operation ID and not Batch A's UUID.

```powershell
# Reads Azure and previews deletion. Target IDs are always visible; --verbose also displays JSON.
dotnet run --project src\BulkCreateCustom\BulkCreateCustom.csproj -- --delete-batch-b "<batch-b-resource-uuid>"

# Deliberately submit Bulk Delete for those 50 VMs.
dotnet run --project src\BulkCreateCustom\BulkCreateCustom.csproj -- --delete-batch-b "<batch-b-resource-uuid>" --execute
```

Both forms accept `--config <path>` and `--verbose`; otherwise they use the source
config and a concise console summary. Full redacted JSON and per-VM details are
always saved in the log. Preview is read-only, not offline.
The configured subscription, resource group and region must match Batch B.
Delete mode does not require `adminPassword` and never calls the create
builder. There is no automatic latest-operation selection or cleanup after creation.

[`DeleteDemo.cs`](../DeleteDemo.cs) (`BulkDeleteDemo`) reads and verifies a `sample=BulkCreateCustom`, `batch=b`,
50-VM operation in state `Succeeded`. It collects all pages of
`VirtualMachinesGetOperationStatusAsync()` (the custom per-VM status action,
sometimes called GETVMOperation; its HTTP method is POST). Every result must be
a successful Create operation with no errors. All 50 unique ARM VM IDs must
match the operation's resolved VM names and configured scope. GET may omit the
create-only `overridesProfile`; a complete resolved resource set is sufficient.
If overrides are also returned, their complete name set must match. When resolved
resources are absent, a complete returned override set can supply the names.
Partial, malformed or conflicting identity sets are rejected. Failed, incomplete,
duplicated, wrong-batch or unexpected results stop the command before deletion.

The validated ARM IDs are passed to `UserRequestResources` in
`ExecuteDeleteContent`, with a five-minute retry window and force deletion disabled.
`ResourceGroupResource.BulkDeleteOperationAsync()` submits the request.
Its HTTP response is acceptance/result information, not proof that all VMs are
gone. Each returned deletion-operation ID is then monitored using
`BulkGetOperationsStatusAsync()` and `GetBulkOperationStatusContent`.
The original creation status endpoint is **not** used to track deletion.

The SDK maps submission to
`POST .../providers/Microsoft.Compute/locations/<region>/virtualMachinesBulkDelete`
and deletion polling to
`POST .../providers/Microsoft.Compute/locations/<region>/virtualMachinesBulkGetOperationStatus`,
both under the configured subscription/resource-group scope.

**Bulk Delete is not restricted to VMs created with BulkCreateCustom.** It accepts
ARM VM resource IDs regardless of the tool that created them (Portal, ARM/Bicep,
CLI or SDK), subject to the API's supported subscription/region/scope and limits,
caller permissions, policy and locks. This demo command intentionally limits
discovery to Batch B; it does not expose an arbitrary-VM deletion option.

The command does not target Batch A, the bulk-operation record, resource group
or VNet/subnet. VM deletion may cascade to disks/NICs according to their configured
delete options; dependencies configured to be retained can remain billable.
No explicit disk/NIC cleanup, force deletion or permission bypass is performed.
The PowerShell cleanup scripts below are separate, broader operations.

The log includes initial HTTP status, target IDs, deletion-operation IDs and final
states/error codes. A `bulk-delete-*.receipt.jsonl` file is reserved beside the
log before submission, then appended with the initial accepted response, mappings
and observed outcomes. Each line is a JSON checkpoint; no create VM profile or
administrator password is included. Keep this receipt for investigation.
Partial rejection or an invalid response prevents overall success, while
identifiable accepted operations continue to be observed. Missing poll results
do not erase previously observed terminal outcomes.

`0` means a safe preview completed, or (with `--execute`) all 50 deletions
succeeded. `1` means a safety/authorization/operation failure, incomplete outcome,
cancellation or timeout. `2` indicates usage/configuration/file errors.
The deadline comes from `pollTimeoutMinutes` and is separate from service retry.
Ctrl+C only stops observation, not Azure deletion. HTTP retries on submission
are disabled: transport ambiguity must not cause automatic resubmission.
After interruption, use receipt IDs to investigate existing deletion operations;
rerunning this command is **not** a resume operation. A failed receipt write after
submission can leave Azure deletion running and is surfaced as an error.

The existing `--validate` mode exercises this flow using synthetic models and
an in-memory HTTP handler, without acquiring Azure credentials or deleting VMs.

## Cleanup (all resources in config.json's resource group)

To delete the **resource group itself and everything inside it**, use the
separate `Delete-ResourceGroup.ps1` instead:

```powershell
# Preview the entire group deletion.
.\src\BulkCreateCustom\scripts\Delete-ResourceGroup.ps1

# Delete the group AND all contents, with confirmation.
.\src\BulkCreateCustom\scripts\Delete-ResourceGroup.ps1 -Execute
```

It reads `subscriptionId` and `resourceGroup` from the parent project directory's
`config.json` (`-ConfigPath` overrides). Stop other provisioning first.
It submits deletion once, prints progress every ten seconds, and confirms group
absence. `-PollIntervalSeconds` and `-TimeoutMinutes` override the ten-second/
60-minute monitoring defaults. `-Execute -WhatIf` does not delete.
Stopping monitoring does not cancel deletion. Errors, locks and policy denials
are surfaced, not bypassed. The preview list is informational: deleting the
group removes all its contents, including resources added after the preview.
Offline checks: `pwsh -NoProfile -File src\BulkCreateCustom\tests\Delete-ResourceGroup.Tests.ps1`.

### Delete contents but keep the resource group

With no operation parameter, `Cleanup.ps1` reads `subscriptionId` and
`resourceGroup` from `config.json` in the parent project directory.

```powershell
# Preview the target resource group and its resources.
.\src\BulkCreateCustom\scripts\Cleanup.ps1

# Delete resources INSIDE the configured group; keep the group itself.
.\src\BulkCreateCustom\scripts\Cleanup.ps1 -Execute
```

**The resource group itself is preserved. All resources returned by its resource
listing are targeted, including VNets/subnets and resources unrelated to this
sample.** Review the displayed subscription/group before confirming. Stop any
other provisioning into the group first. A future demo run needs an existing
VNet/subnet again. Group-level metadata such as deployment history, role
assignments and locks is not swept separately.
`-ConfigPath <path>` selects another config. `-Execute -WhatIf` does not delete.
The script prints timestamped messages while loading the preview, submitting
individual deletions, and checking progress. It checks the resource listing every
10 seconds, with a 60-minute monitoring timeout. Override these with
`-PollIntervalSeconds` and `-TimeoutMinutes` if needed. Progress reports show the
remaining resource count and accepted deletions still visible, not a percentage.
Compute resources are submitted first, then network consumers, then other
resources and VNets. Dependency failures are retried while deletes progress;
if no deletion is pending and nothing was removed, the script stops with the
remaining resources rather than reporting success. Resource-specific constraints
can still require manual resolution. The group is checked for continued existence
at completion, and no group-delete command is issued.
Stopping the script or reaching the timeout does not cancel Azure deletion.
Permission, lock or policy failures are surfaced without bypassing them.

### Optional: clean only one operation

`Cleanup.ps1` is a separate PowerShell 7 / Azure CLI script. It does not run as
part of the demo. When `-OperationName` is supplied, it preserves the shared
resource group and networking. It reads `subscriptionId`, `resourceGroup`, and `region`
from `config.json` in the parent project directory, independent of the terminal's working
directory. Supply the completed bulk-resource UUID from the creation log
(not the asynchronous operation UUID). No VM password is required.

```powershell
$operationName = "<bulk-operation-uuid>"

# Preview only: list the exact targets, without deleting anything.
.\src\BulkCreateCustom\scripts\Cleanup.ps1 -OperationName $operationName

# After reviewing the preview: confirm each deletion interactively.
.\src\BulkCreateCustom\scripts\Cleanup.ps1 -OperationName $operationName -Execute
```

Use `-ConfigPath <path>` to select a different config file. For older runs in a
different subscription or resource group, the config must match that run.
Alternatively, `-OperationResourceId <full-resource-id>` retains the original
explicit-scope behavior and does not read config.json.

The script checks the operation's sample tag and terminal state, discovers its
explicit VM names, and inventories the managed disks/NICs referenced by those
VMs. It deletes VMs first, then detached NICs and managed disks, and requests
deletion of the bulk-operation record last. All targets must remain within the
operation's resource group. Attached NICs/disks are refused; shared networking,
public IPs and unrelated resources are preserved. Azure errors stop cleanup
rather than being treated as successful deletion. Permissions, policy and locks
may prevent cleanup and must be resolved through normal administration.

Before deletion, a gitignored `cleanup-<operation-uuid>.json` inventory is saved
in the project directory (the same location as before scripts moved into `scripts`).
Keep it intact and rerun the same command after a partial
failure; it retains dependency IDs even if the VM has already been deleted.
`-InventoryPath` can override its location. Treat this inventory as trusted
destructive-operation input: review it and do not load files from untrusted sources.
If the operation itself is absent/unreadable, the script stops instead of guessing.
The final operation-record DELETE may finish asynchronously.

**Limit:** disks/NICs orphaned before the first inventory cannot be reliably
attributed to this run and require separate review. The script does not sweep
resources merely because their names share a prefix. It refuses active operations
and never cancels them. Use one explicit operation ID per run you want to clean up.
`-Execute -WhatIf` provides an additional dry run; `-Confirm:$false` suppresses
individual confirmations only when deliberately combined with `-Execute`.

Offline mocked script checks (no Azure requests or real resource deletions):

```powershell
pwsh -NoProfile -File src\BulkCreateCustom\tests\Cleanup.Tests.ps1
```

## Public contract references

- [Pinned SDK source](https://github.com/Azure/azure-sdk-for-net/tree/470fcf36991d2b32caa7ee434c54d1a518b9c3fd/sdk/compute/Azure.ResourceManager.Compute.BulkActions):
  `LocationBasedBulkCreateCustomCollection`, `BulkCreateCustomProperties`,
  `BulkCreateCustomOverridesProfile`, `BulkCreateCustomVmSizeProfile`,
  `BulkCreateCustomOverrideBase`, `BulkOperationRetryPolicy`.
- [Matching custom-endpoint TypeSpec](https://github.com/Azure/azure-rest-api-specs/blob/8681ba204905f0aad414f3a0e2e687cc5fcb2453/specification/compute/resource-manager/Microsoft.Compute/Bulkactions/bulkCreateCustom.tsp):
  naming/count rules, override precedence and service-owned allocation fields.

## Troubleshooting

| Symptom | Safe next step |
|---|---|
| Configuration rejected | Replace all placeholders, use valid JSON and supported properties, and verify the source config path printed at startup. Do not log/share the password. |
| Source tree moved or published executable cannot find config | Rebuild for the new source location, or pass `--config <path>` explicitly. Old `bin` config copies are ignored. |
| NuGet feed disabled locally | Use the explicit restore command under [offline validation](#build-and-offline-validation). |
| Not enough subnet IPs / conflicting subnet | Review existing usage and address overlaps; use an approved new subnet rather than resizing or deleting an in-use subnet. A larger prefix does not guarantee currently free IPs. |
| Missing JSON or per-VM console detail | Read the full redacted log, or pass `--verbose` for create or delete. Details are logged by default. |
| 404/transient polling errors after acceptance | Observation retries are bounded by the local deadline; do not create another batch to repair observation. |
| `InternalExecutionError`, unknown acceptance, timeout or Ctrl+C | Investigate the saved resource/operation IDs, receipt and Activity Log. Azure work can continue after local monitoring ends. Do not blindly rerun. |
| Batch B delete safety check fails | Confirm the creation resource UUID, configured scope, `Succeeded` state and all 50 successful matching VM identities. Never weaken the guard to force deletion. |
| Cleanup blocked by attached dependencies, policy or locks | Inspect the remaining resources and resolve through normal administration. Keep operation cleanup inventory intact; no permission/lock bypass is performed. |

Offline validation cannot prove deployment success or that Azure applied the
expected overrides. This documentation reports no live deployment verification.
