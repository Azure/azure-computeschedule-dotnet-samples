#requires -Version 7.0
<#
.SYNOPSIS
Previews or creates the resource group specified by config.json.
.DESCRIPTION
Reads subscriptionId, resourceGroup and region from config.json in the parent
project directory. Requires -Execute to create. Existing groups are not updated.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\config.json'),
    [switch] $Execute
)

$ErrorActionPreference = 'Stop'

function Write-Status([string] $Message) {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message"
}

function Invoke-AzJson([string[]] $Arguments) {
    $output = & az @Arguments --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Azure CLI failed (exit $LASTEXITCODE). Resource group creation was not confirmed." }
    if ($output) { ($output -join "`n") | ConvertFrom-Json -Depth 100 }
}

Write-Status "Reading $ConfigPath"
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$subscription = [guid]::Empty
if ($config.subscriptionId -isnot [string] -or
    ![guid]::TryParseExact($config.subscriptionId, 'D', [ref]$subscription) -or
    $subscription -eq [guid]::Empty -or
    $config.resourceGroup -isnot [string] -or
    $config.resourceGroup -notmatch '^[a-zA-Z0-9_.()-]{1,90}$' -or $config.resourceGroup.EndsWith('.') -or
    $config.region -isnot [string] -or $config.region -notmatch '^[a-z][a-z0-9]+$') {
    throw 'config.json must provide a valid subscriptionId, resourceGroup and region.'
}
$group = $config.resourceGroup
$id = "/subscriptions/$($config.subscriptionId)/resourceGroups/$group"
Write-Status "Target: $id; location: $($config.region)"
Write-Status 'Checking whether the resource group already exists...'
$exists = Invoke-AzJson @('group', 'exists', '--subscription', $config.subscriptionId, '--name', $group)
if ($exists -isnot [bool]) { throw 'Azure returned an unexpected resource-group existence response.' }
if ($exists) {
    $existing = Invoke-AzJson @('group', 'show', '--subscription', $config.subscriptionId, '--name', $group)
    if ($existing.id -ne $id -or $existing.properties.provisioningState -ne 'Succeeded') {
        throw "Existing resource group is not ready: $id"
    }
    Write-Status "Resource group already exists in '$($existing.location)'. No changes made."
    if ($existing.location -ne $config.region) {
        Write-Warning 'Resource-group metadata location differs from config.region. The group is preserved; resources can have their own locations.'
    }
    return
}
if (!$Execute) {
    Write-Status 'Preview only. Add -Execute to create this resource group.'
    return
}
if (!$PSCmdlet.ShouldProcess($id, "Create resource group in $($config.region)")) { return }
Write-Status 'Creating resource group; waiting for Azure...'
Invoke-AzJson @('group', 'create', '--subscription', $config.subscriptionId, '--name', $group,
    '--location', $config.region, '--tags', 'sample=BulkCreateCustom') | Out-Null
Write-Status 'Checking resource group creation...'
$created = Invoke-AzJson @('group', 'show', '--subscription', $config.subscriptionId, '--name', $group)
if ($created.id -ne $id -or $created.properties.provisioningState -ne 'Succeeded') {
    throw "Resource group creation is not confirmed as Succeeded: $id"
}
Write-Status "Resource group ready: $id. No VNet or VMs were created."
