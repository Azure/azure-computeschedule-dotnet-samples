#requires -Version 7.0
<#
.SYNOPSIS
Previews or deletes resources inside the configured resource group, or one demo operation.
.DESCRIPTION
Without an operation parameter, reads config.json and deletes its resource group's
contents when -Execute is supplied and confirmed. The resource group is preserved.
OperationName or OperationResourceId selects the narrower operation cleanup,
which preserves shared networking and requires a completed operation.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High', DefaultParameterSetName = 'ResourceGroup')]
param(
    [Parameter(Mandatory, ParameterSetName = 'ResourceId')]
    [string] $OperationResourceId,

    [Parameter(Mandatory, ParameterSetName = 'Config')]
    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string] $OperationName,

    [Parameter(ParameterSetName = 'Config')]
    [Parameter(ParameterSetName = 'ResourceGroup')]
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\config.json'),

    [switch] $Execute,

    [Parameter(ParameterSetName = 'ResourceGroup')]
    [ValidateRange(1, 300)]
    [int] $PollIntervalSeconds = 10,

    [Parameter(ParameterSetName = 'ResourceGroup')]
    [ValidateRange(1, 240)]
    [int] $TimeoutMinutes = 60,

    [string] $InventoryPath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandArgumentPassing = 'Standard'

function Write-CleanupStatus {
    param([string] $Message)
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message"
}

function Invoke-AzJson {
    param([string[]] $Arguments)
    $output = & az @Arguments --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed (exit $LASTEXITCODE). Cleanup stopped; no automatic retries."
    }
    if ($output) { ($output -join "`n") | ConvertFrom-Json -Depth 100 }
}

function Get-GroupResources {
    @(Invoke-AzJson -Arguments @('resource', 'list', '--subscription', $subscription, '--resource-group', $group))
}

function Assert-TargetId {
    param([string] $Id, [string] $Type)
    if (!$Id.StartsWith("$groupId/providers/", [StringComparison]::OrdinalIgnoreCase) -or
        $Id -notmatch ('(?i)/providers/' + [regex]::Escape($Type) + '/[^/]+$')) {
        throw "Refusing an out-of-scope or unexpected resource: $Id"
    }
}

function Remove-Target {
    param([string] $Id, [string] $Kind)
    Write-CleanupStatus "Checking $Kind target: $Id"
    $existing = @(Get-GroupResources | Where-Object id -EQ $Id)
    if (!$existing.Count) {
        Write-Host "Already absent: $Id"
        return
    }
    if ($Kind -eq 'Disk') {
        $disk = Invoke-AzJson -Arguments @('disk', 'show', '--ids', $Id)
        if ($disk.managedBy -or @($disk.managedByExtended).Where({ $_ }).Count) {
            throw "Disk is still attached; refusing deletion: $Id"
        }
    }
    if ($Kind -eq 'NIC') {
        $nic = Invoke-AzJson -Arguments @('network', 'nic', 'show', '--ids', $Id)
        if ($nic.virtualMachine.id -or $nic.privateEndpoint.id) {
            throw "NIC is still attached; refusing deletion: $Id"
        }
    }
    if ($PSCmdlet.ShouldProcess($Id, "Delete $Kind")) {
        Write-CleanupStatus "Deleting $Kind (waiting for Azure): $Id"
        switch ($Kind) {
            'VM'   { Invoke-AzJson -Arguments @('vm', 'delete', '--ids', $Id, '--yes') | Out-Null }
            'NIC'  { Invoke-AzJson -Arguments @('network', 'nic', 'delete', '--ids', $Id) | Out-Null }
            'Disk' { Invoke-AzJson -Arguments @('disk', 'delete', '--ids', $Id, '--yes') | Out-Null }
        }
        Write-CleanupStatus "$Kind delete command completed: $Id"
    }
}

if ($PSCmdlet.ParameterSetName -ne 'ResourceId') {
    Write-CleanupStatus "Reading configuration: $ConfigPath"
    $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    $configSubscription = [guid]::Empty
    if ($config.subscriptionId -isnot [string] -or
        ![guid]::TryParseExact($config.subscriptionId, 'D', [ref]$configSubscription) -or
        $configSubscription -eq [guid]::Empty -or
        $config.resourceGroup -isnot [string] -or
        $config.resourceGroup -notmatch '^[a-zA-Z0-9_.()-]{1,90}$' -or $config.resourceGroup.EndsWith('.')) {
        throw 'config.json must provide a valid subscriptionId and resourceGroup.'
    }
    if ($PSCmdlet.ParameterSetName -eq 'ResourceGroup') {
        $subscription = $config.subscriptionId
        $group = $config.resourceGroup
        $groupId = "/subscriptions/$subscription/resourceGroups/$group"
        Write-Warning "RESOURCE GROUP CONTENTS CLEANUP: $groupId"
        Write-Warning 'The resource group is preserved, but ALL resources inside it are targeted, including shared networks and resources not created by this demo. Stop other provisioning before proceeding.'
        Write-CleanupStatus "Checking whether resource group '$group' exists..."
        $exists = Invoke-AzJson -Arguments @('group', 'exists', '--subscription', $subscription, '--name', $group)
        if (!$exists) {
            Write-Host "Resource group is already absent: $groupId"
            return
        }
        Write-CleanupStatus 'Loading resources for the cleanup preview...'
        $resources = Get-GroupResources
        foreach ($resource in $resources) { Write-Host "$($resource.type): $($resource.id)" }
        Write-CleanupStatus "Found $($resources.Count) resources in the configured group."
        if (!$Execute) {
            Write-Host 'Preview only. Run with -Execute to delete resources inside this group, with confirmation. The group itself is kept.'
            return
        }
        Write-CleanupStatus 'Ready to delete. Review the confirmation prompt below.'
        if ($PSCmdlet.ShouldProcess($groupId, 'Permanently delete ALL resources INSIDE this group; preserve the resource group')) {
            Write-CleanupStatus "Deleting resources inside '$group'; the resource group will NOT be deleted."
            Write-Warning 'Stopping this script stops monitoring only; Azure deletion may continue. A polling error does not mean deletion was cancelled.'
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $submitted = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            while ($resources.Count) {
                if ($timer.Elapsed.TotalMinutes -ge $TimeoutMinutes) {
                    throw "Timed out with $($resources.Count) resources remaining in $groupId. Azure deletions may still be running; the group was not deleted by this script."
                }
                # Start with compute and network consumers; retry dependency-blocked resources
                # only while earlier deletes are progressing. Do not bypass locks or policy.
                $ordered = $resources | Sort-Object @{
                    Expression = {
                        switch -Regex ($_.type) {
                            '^Microsoft.Compute/virtualMachines$|^Microsoft.Compute/virtualMachineScaleSets$' { 0; break }
                            '^Microsoft.Network/networkInterfaces$|^Microsoft.Network/privateEndpoints$|^Microsoft.Network/bastionHosts$' { 1; break }
                            '^Microsoft.Network/virtualNetworks$' { 3; break }
                            default { 2 }
                        }
                    }
                }, id
                foreach ($resource in $ordered) {
                    if (!$resource.id -or !$resource.id.StartsWith("$groupId/providers/", [StringComparison]::OrdinalIgnoreCase)) {
                        throw "Refusing resource outside the configured group: $($resource.id)"
                    }
                    if ($submitted.Contains($resource.id)) { continue }
                    Write-CleanupStatus "Submitting resource deletion: $($resource.id)"
                    $arguments = @('resource', 'delete', '--ids', $resource.id, '--no-wait', '--only-show-errors', '--output', 'json')
                    if ($resource.type -eq 'Microsoft.Compute/locations/bulkCreateCustom') {
                        $arguments += @('--api-version', '2026-08-06-preview')
                    }
                    & az @arguments | Out-Null
                    if ($LASTEXITCODE -eq 0) {
                        $null = $submitted.Add($resource.id)
                        Write-CleanupStatus "Deletion request accepted: $($resource.id)"
                    } else {
                        Write-Warning "Deletion request failed for $($resource.id) (exit $LASTEXITCODE). See Azure's error above; resource remains a cleanup target."
                    }
                }
                $remaining = Get-GroupResources
                $removed = @($resources | Where-Object { $_.id -notin $remaining.id }).Count
                $pending = @($remaining | Where-Object { $submitted.Contains($_.id) }).Count
                Write-CleanupStatus "Remaining resources: $($remaining.Count); removed since last check: $removed; accepted deletions still visible: $pending; elapsed $([int]$timer.Elapsed.TotalSeconds) seconds."
                if (!$remaining.Count) { break }
                if (!$removed -and !$pending) {
                    throw "Cleanup blocked: no deletions progressed and $($remaining.Count) resources remain. Resolve the reported dependency, permission, lock or policy errors. Resource group preserved."
                }
                $resources = $remaining
                Write-CleanupStatus "Waiting $PollIntervalSeconds seconds before the next progress check..."
                Start-Sleep -Seconds $PollIntervalSeconds
            }
            $exists = Invoke-AzJson -Arguments @('group', 'exists', '--subscription', $subscription, '--name', $group)
            if (!$exists) { throw "The resource group is now absent (it may have been removed by another process): $groupId" }
            Write-CleanupStatus "Cleanup complete: no resources remain in the resource listing; resource group preserved: $groupId"
        } else {
            Write-CleanupStatus 'Deletion not submitted (preview or confirmation declined).'
        }
        return
    }
    if ($config.region -isnot [string] -or $config.region -notmatch '^[a-z][a-z0-9]+$') {
        throw 'config.json must provide a valid region for operation cleanup.'
    }
    $OperationResourceId = "/subscriptions/$($config.subscriptionId)/resourceGroups/$($config.resourceGroup)/providers/Microsoft.Compute/locations/$($config.region)/bulkCreateCustom/$OperationName"
}

$pattern = '^/subscriptions/(?<subscription>[0-9a-f-]{36})/resourceGroups/(?<group>[^/]+)/providers/Microsoft\.Compute/locations/(?<region>[a-z0-9]+)/bulkCreateCustom/(?<operation>[0-9a-f-]{36})$'
if ($OperationResourceId -notmatch $pattern) {
    throw 'Supply the full /subscriptions/.../resourceGroups/.../providers/Microsoft.Compute/locations/.../bulkCreateCustom/<UUID> resource ID.'
}
$subscription = $Matches.subscription
$group = $Matches.group
$operationName = $Matches.operation
$parsedId = [guid]::Empty
if (![guid]::TryParseExact($subscription, 'D', [ref]$parsedId) -or
    ![guid]::TryParseExact($operationName, 'D', [ref]$parsedId)) {
    throw 'Subscription and operation names must be UUIDs.'
}
$groupId = "/subscriptions/$subscription/resourceGroups/$group"
$operationUrl = "https://management.azure.com${OperationResourceId}?api-version=2026-08-06-preview"
if (!$InventoryPath) { $InventoryPath = Join-Path $PSScriptRoot "..\cleanup-$operationName.json" }
$InventoryPath = [IO.Path]::GetFullPath($InventoryPath)

# Select only non-secret fields; never persist the operation's credential-bearing base profile.
Write-CleanupStatus "Reading completed operation: $OperationResourceId"
$operation = Invoke-AzJson -Arguments @('rest', '--method', 'get', '--url', $operationUrl,
    '--query', '{id:id,tags:tags,state:properties.provisioningState,names:properties.overridesProfile.overrides[].virtualMachineName,resolvedNames:properties.resources[].virtualMachineInfo.name}')
if ($operation.state -notin @('Succeeded', 'Failed', 'Canceled')) {
    throw "Operation state is '$($operation.state)'. Wait for terminal completion before cleanup."
}
if ($operation.tags.sample -ne 'BulkCreateCustom' -or $operation.id -ne $OperationResourceId) {
    throw 'The operation is not marked as this BulkCreateCustom sample. Refusing cleanup.'
}
$names = @(@($operation.names) + @($operation.resolvedNames) | Where-Object { $_ } | Sort-Object -Unique)
if (!$names.Count -or @($names | Where-Object { $_ -notmatch '^[a-z][a-z0-9-]{0,11}-[0-9a-f]{32}-[ab]-[0-9]{3}$' }).Count) {
    throw 'Cannot establish explicit demo VM names for this operation. Refusing cleanup.'
}
$vmIds = @($names | ForEach-Object { "$groupId/providers/Microsoft.Compute/virtualMachines/$_" })
$targets = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($id in $vmIds) { $targets[$id] = 'VM' }

$saved = $null
if (Test-Path -LiteralPath $InventoryPath) {
    $saved = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
    if ($saved.operationResourceId -ne $OperationResourceId) { throw 'Inventory belongs to another operation.' }
    foreach ($target in $saved.targets) {
        $type = switch ($target.kind) {
            'VM' { 'Microsoft.Compute/virtualMachines' }
            'NIC' { 'Microsoft.Network/networkInterfaces' }
            'Disk' { 'Microsoft.Compute/disks' }
            default { throw 'Unknown inventory resource type.' }
        }
        Assert-TargetId $target.id $type
        if ($target.sourceVmId -notin $vmIds) { throw 'Inventory contains a resource associated with another VM.' }
        if ($target.kind -eq 'VM' -and $target.id -ne $target.sourceVmId) { throw 'Invalid VM inventory entry.' }
        $targets[$target.id] = $target.kind
    }
}
$inventory = [Collections.Generic.List[object]]::new()
if ($saved) {
    foreach ($target in $saved.targets) { $inventory.Add($target) }
}
Write-CleanupStatus 'Discovering VM, NIC and disk cleanup targets...'
$existingResources = Get-GroupResources
foreach ($id in $vmIds) {
    if (!$inventory.Where({ $_.id -eq $id }).Count) {
        $inventory.Add(@{ id = $id; kind = 'VM'; sourceVmId = $id })
    }
    if (!@($existingResources | Where-Object id -EQ $id).Count) { continue }
    $vm = Invoke-AzJson -Arguments @('vm', 'show', '--ids', $id, '--query',
        '{disks:storageProfile.dataDisks[].managedDisk.id,osDisk:storageProfile.osDisk.managedDisk.id,nics:networkProfile.networkInterfaces[].id}')
    foreach ($diskId in @(@($vm.osDisk) + @($vm.disks) | Where-Object { $_ })) {
        Assert-TargetId $diskId 'Microsoft.Compute/disks'
        $targets[$diskId] = 'Disk'
        if (!$inventory.Where({ $_.id -eq $diskId }).Count) {
            $inventory.Add(@{ id = $diskId; kind = 'Disk'; sourceVmId = $id })
        }
    }
    foreach ($nicId in @($vm.nics | Where-Object { $_ })) {
        Assert-TargetId $nicId 'Microsoft.Network/networkInterfaces'
        $targets[$nicId] = 'NIC'
        if (!$inventory.Where({ $_.id -eq $nicId }).Count) {
            $inventory.Add(@{ id = $nicId; kind = 'NIC'; sourceVmId = $id })
        }
    }
}

Write-Host "Operation: $OperationResourceId"
Write-Host "State: $($operation.state)"
foreach ($target in $inventory) { Write-Host "$($target.kind): $($target.id)" }
Write-Host 'Preserved: resource group, VNets, subnets, public IPs, and unrelated resources.'
Write-Warning 'Orphaned resources from VMs absent before the first inventory cannot be attributed safely and are not deleted. Review those separately.'
if (!$Execute) {
    Write-Host 'Preview only. Use -Execute to delete these targets (confirmation required); -Execute -WhatIf also previews.'
    return
}
if ($WhatIfPreference) {
    foreach ($target in $inventory) { $null = $PSCmdlet.ShouldProcess($target.id, "Delete $($target.kind)") }
    $null = $PSCmdlet.ShouldProcess($OperationResourceId, 'Delete completed bulk-operation record')
    return
}

# Preserve dependency IDs before deleting VMs, so a later retry can remove retained disks/NICs.
@{ operationResourceId = $OperationResourceId; targets = @($inventory.ToArray()) } |
    ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $InventoryPath -Encoding utf8
Write-Host "Cleanup inventory: $InventoryPath"
foreach ($kind in @('VM', 'NIC', 'Disk')) {
    foreach ($target in $inventory | Where-Object kind -EQ $kind) { Remove-Target $target.id $kind }
}
$remaining = @(Get-GroupResources | Where-Object { $targets.ContainsKey($_.id) })
if ($remaining.Count) {
    throw "$($remaining.Count) target resources remain. Bulk-operation record preserved. Rerun with the same inventory after resolving failures."
}
if ($PSCmdlet.ShouldProcess($OperationResourceId, 'Delete completed bulk-operation record (VM deletion disabled)')) {
    # Dependencies were handled explicitly; do not ask the service to delete instances again.
    Invoke-AzJson -Arguments @('rest', '--method', 'delete', '--url', $operationUrl,
        '--url-parameters', 'deleteInstances=false') | Out-Null
    Write-Host 'VM/NIC/disk targets are absent. Bulk-operation record deletion requested; service deletion may complete asynchronously.'
}
