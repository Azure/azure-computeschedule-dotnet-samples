#requires -Version 7.0
<#
.SYNOPSIS
Previews or deletes the resource group in config.json, including ALL its contents.
.DESCRIPTION
Requires -Execute and confirmation. Existing locks/policies are not bypassed.
Stopping local monitoring does not cancel a deletion already accepted by Azure.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\config.json'),
    [switch] $Execute,
    [ValidateRange(1, 300)]
    [int] $PollIntervalSeconds = 10,
    [ValidateRange(1, 240)]
    [int] $TimeoutMinutes = 60
)

$ErrorActionPreference = 'Stop'

function Write-Status([string] $Message) {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message"
}

function Invoke-AzJson([string[]] $Arguments) {
    $output = & az @Arguments --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed (exit $LASTEXITCODE). Deletion completion is not confirmed. If already submitted, Azure deletion may continue."
    }
    if ($output) { ($output -join "`n") | ConvertFrom-Json -Depth 100 }
}

function Test-GroupExists {
    $exists = Invoke-AzJson @('group', 'exists', '--subscription', $config.subscriptionId, '--name', $group)
    if ($exists -isnot [bool]) { throw 'Unexpected resource-group existence response. Deletion completion is not confirmed.' }
    $exists
}

Write-Status "Reading $ConfigPath"
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$subscription = [guid]::Empty
if ($config.subscriptionId -isnot [string] -or
    ![guid]::TryParseExact($config.subscriptionId, 'D', [ref]$subscription) -or
    $subscription -eq [guid]::Empty -or
    $config.resourceGroup -isnot [string] -or
    $config.resourceGroup -notmatch '^[a-zA-Z0-9_.()-]{1,90}$' -or $config.resourceGroup.EndsWith('.')) {
    throw 'config.json must provide a valid subscriptionId and resourceGroup.'
}
$group = $config.resourceGroup
$id = "/subscriptions/$($config.subscriptionId)/resourceGroups/$group"
Write-Warning "DELETE ENTIRE RESOURCE GROUP: $id"
Write-Warning 'This includes ALL VMs, disks, networks and unrelated resources inside the group. Stop other provisioning into it before continuing.'
Write-Status 'Checking whether the group exists...'
if (!(Test-GroupExists)) {
    Write-Status "Resource group already absent: $id"
    return
}
Write-Status 'Loading resources for preview...'
$resources = @(Invoke-AzJson @('resource', 'list', '--subscription', $config.subscriptionId, '--resource-group', $group))
foreach ($resource in $resources) { Write-Host "$($resource.type): $($resource.id)" }
Write-Status "Preview contains $($resources.Count) listed resources; the deletion targets the entire group, not just this snapshot."
if (!$Execute) {
    Write-Status 'Preview only. Add -Execute to delete the resource group and all its contents.'
    return
}
if (!$PSCmdlet.ShouldProcess($id, 'Permanently delete resource group and ALL contents')) {
    Write-Status 'Deletion not submitted (WhatIf or confirmation declined).'
    return
}
Write-Status 'Submitting resource-group deletion...'
Invoke-AzJson @('group', 'delete', '--subscription', $config.subscriptionId, '--name', $group, '--yes', '--no-wait') | Out-Null
Write-Status "Deletion request accepted. Checking every $PollIntervalSeconds seconds (timeout: $TimeoutMinutes minutes)."
Write-Warning 'Stopping this script stops monitoring only; it does NOT cancel Azure deletion.'
$timer = [Diagnostics.Stopwatch]::StartNew()
while ($true) {
    Write-Status "Checking group existence; elapsed $([int]$timer.Elapsed.TotalSeconds) seconds..."
    if (!(Test-GroupExists)) {
        Write-Status "Deletion complete: resource group is absent: $id"
        break
    }
    if ($timer.Elapsed.TotalMinutes -ge $TimeoutMinutes) {
        throw "Timed out waiting for deletion of $id. Azure may still be deleting resources; inspect the operation before retrying."
    }
    Write-Status "Group still exists; completion is not confirmed. Next check in $PollIntervalSeconds seconds."
    Start-Sleep -Seconds $PollIntervalSeconds
}
