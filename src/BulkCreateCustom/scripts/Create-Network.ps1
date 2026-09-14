#requires -Version 7.0
<#
.SYNOPSIS
Creates the VNet/subnet named by config.json's subnetId; previews by default.
.DESCRIPTION
Requires the network resource group to exist. Existing networks are inspected,
not overwritten. No public IP, NAT gateway, peering or resource group is created.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string] $ConfigPath = (Join-Path $PSScriptRoot '..\config.json'),
    [string] $VnetAddressPrefix = '10.0.0.0/16',
    [string] $SubnetAddressPrefix = '10.0.8.0/21',
    [switch] $Execute
)

$ErrorActionPreference = 'Stop'

function Write-Status([string] $Message) {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message"
}

function Invoke-AzJson([string[]] $Arguments) {
    $output = & az @Arguments --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Azure CLI failed (exit $LASTEXITCODE). No automatic retry; inspect any partially created network." }
    if ($output) { ($output -join "`n") | ConvertFrom-Json -Depth 100 }
}

function Get-CidrRange([string] $Prefix) {
    if ($Prefix -notmatch '^(\d{1,3}\.){3}\d{1,3}/(\d{1,2})$') { throw "Invalid IPv4 CIDR: $Prefix" }
    $parts = $Prefix.Split('/')
    $bits = [int]$parts[1]
    $octets = @($parts[0].Split('.') | ForEach-Object { [int]$_ })
    if ($bits -gt 32 -or @($octets | Where-Object { $_ -gt 255 }).Count) { throw "Invalid IPv4 CIDR: $Prefix" }
    $number = [long]0
    foreach ($octet in $octets) { $number = $number * 256 + $octet }
    $size = [long][math]::Pow(2, 32 - $bits)
    if ($number % $size -ne 0) { throw "CIDR must start at a network boundary: $Prefix" }
    @{ Start=$number; End=$number + $size - 1; Size=$size }
}

function Assert-Subnet($Subnet) {
    $prefixes = @(@($Subnet.addressPrefix) + @($Subnet.addressPrefixes) | Where-Object { $_ })
    if ($SubnetAddressPrefix -notin $prefixes) {
        throw 'Existing subnet address prefix differs. Refusing to resize it. Set subnetId in config.json to a new subnet name to create a larger subnet without affecting existing VMs.'
    }
    if ($Subnet.defaultOutboundAccess -ne $false) {
        throw 'Existing subnet does not explicitly disable default outbound access. Review it separately; this script will not modify its connectivity.'
    }
    if (@($Subnet.delegations).Where({ $_ }).Count) { throw 'Subnet is delegated; review its suitability for these VMs.' }
    if ($Subnet.provisioningState -ne 'Succeeded') { throw "Subnet is not ready: $($Subnet.provisioningState)" }
}

Write-Status "Reading $ConfigPath"
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
$pattern = '^/subscriptions/(?<sub>[0-9a-f-]{36})/resourceGroups/(?<group>[a-zA-Z0-9_.()-]{1,90})/providers/Microsoft.Network/virtualNetworks/(?<vnet>[a-zA-Z0-9_.-]+)/subnets/(?<subnet>[a-zA-Z0-9_.-]+)$'
if ($config.subnetId -isnot [string] -or $config.subnetId -notmatch $pattern) { throw 'config.json must contain a complete subnetId.' }
$subscription = $Matches.sub
$networkGroup = $Matches.group
$vnetName = $Matches.vnet
$subnetName = $Matches.subnet
$uuid = [guid]::Empty
if (![guid]::TryParseExact($subscription, 'D', [ref]$uuid) -or $uuid -eq [guid]::Empty -or
    $subscription -ne $config.subscriptionId -or $config.region -notmatch '^[a-z][a-z0-9]+$') {
    throw 'Invalid subscription/region, or subnetId belongs to a different subscription.'
}
$vnetRange = Get-CidrRange $VnetAddressPrefix
$subnetRange = Get-CidrRange $SubnetAddressPrefix
if ($subnetRange.Start -lt $vnetRange.Start -or $subnetRange.End -gt $vnetRange.End) { throw 'Subnet must fit within VnetAddressPrefix.' }
if ($subnetRange.Size - 5 -lt 150) { throw 'Choose a subnet with at least 150 usable IPv4 addresses after Azure reservations.' }
$vnetId = "/subscriptions/$subscription/resourceGroups/$networkGroup/providers/Microsoft.Network/virtualNetworks/$vnetName"
Write-Status "Network target: $($config.subnetId)"
Write-Status "Region: $($config.region); VNet: $VnetAddressPrefix; subnet: $SubnetAddressPrefix; defaultOutboundAccess=false."
Write-Status "Subnet address capacity: $($subnetRange.Size - 5) usable IPv4 addresses before existing allocations."
if ($networkGroup -ne $config.resourceGroup) {
    Write-Warning "VM resource group is '$($config.resourceGroup)', but the network resource group from subnetId is '$networkGroup'. The network will be created in '$networkGroup'."
}
if (!(Invoke-AzJson @('group', 'exists', '--subscription', $subscription, '--name', $networkGroup))) {
    throw "Network resource group '$networkGroup' does not exist. Create/choose it separately."
}
$networks = @(Invoke-AzJson @('network', 'vnet', 'list', '--subscription', $subscription, '--resource-group', $networkGroup))
$existing = $networks | Where-Object id -EQ $vnetId
$createVnet = !$existing
if ($existing) {
    if ($existing.location -ne $config.region -or $existing.provisioningState -ne 'Succeeded') {
        throw 'Existing VNet has a different region or is not ready. Refusing to modify it.'
    }
    $subnet = $existing.subnets | Where-Object name -EQ $subnetName
    if ($subnet) {
        Assert-Subnet $subnet
        Write-Status 'VNet/subnet already exist with the requested settings. No changes needed.'
        return
    }
    $contained = $false
    foreach ($prefix in $existing.addressSpace.addressPrefixes | Where-Object { $_ -notmatch ':' }) {
        $range = Get-CidrRange $prefix
        if ($subnetRange.Start -ge $range.Start -and $subnetRange.End -le $range.End) { $contained = $true }
    }
    if (!$contained) { throw 'Requested subnet does not fit the existing VNet address space.' }
    foreach ($s in $existing.subnets) {
        foreach ($prefix in (@(@($s.addressPrefix) + @($s.addressPrefixes)) | Where-Object { $_ -and $_ -notmatch ':' })) {
            $range = Get-CidrRange $prefix
            if ($subnetRange.Start -le $range.End -and $subnetRange.End -ge $range.Start) {
                throw "Requested subnet overlaps existing subnet '$($s.name)'."
            }
        }
    }
}
$target = if ($createVnet) { $vnetId } else { $config.subnetId }
$description = if ($createVnet) { 'Create VNet with private subnet' } else { 'Create private subnet in existing VNet' }
Write-Status "$description : $target"
Write-Warning 'No explicit internet egress is provisioned. Guest updates/assessment need an approved outbound path; verify routing and subnet capacity before running VMs.'
if (!$Execute) { Write-Status 'Preview only. Add -Execute to create the network.'; return }
if (!$PSCmdlet.ShouldProcess($target, $description)) { return }
$subnetProperties = @{ addressPrefix=$SubnetAddressPrefix; defaultOutboundAccess=$false }
$payload = if ($createVnet) {
    @{
        location=$config.region
        tags=@{ sample='BulkCreateCustom' }
        properties=@{
            addressSpace=@{ addressPrefixes=@($VnetAddressPrefix) }
            subnets=@(@{ name=$subnetName; properties=$subnetProperties })
        }
    }
} else { @{ properties=$subnetProperties } }
# Create the subnet with its policy settings in the same PUT, never a default-outbound intermediate subnet.
$bodyPath = Join-Path ([IO.Path]::GetTempPath()) "bulk-network-$([guid]::NewGuid()).json"
try {
    $payload | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $bodyPath -Encoding utf8
    Write-Status 'Submitting network creation...'
    Invoke-AzJson @('rest', '--method', 'put', '--url', "https://management.azure.com${target}?api-version=2022-11-01",
        '--body', "@$bodyPath") | Out-Null
    Write-Status 'Waiting for VNet provisioning (up to 10 minutes)...'
    Invoke-AzJson @('network', 'vnet', 'wait', '--subscription', $subscription, '--resource-group', $networkGroup,
        '--name', $vnetName, '--created', '--interval', '10', '--timeout', '600') | Out-Null
    Write-Status 'Waiting for subnet provisioning (up to 10 minutes)...'
    Invoke-AzJson @('network', 'vnet', 'subnet', 'wait', '--subscription', $subscription, '--resource-group', $networkGroup,
        '--vnet-name', $vnetName, '--name', $subnetName, '--created', '--interval', '10', '--timeout', '600') | Out-Null
    $subnet = Invoke-AzJson @('network', 'vnet', 'subnet', 'show', '--ids', $config.subnetId)
    Assert-Subnet $subnet
    Write-Status "Network ready: $($config.subnetId). Configuration was not changed."
}
finally {
    Remove-Item -LiteralPath $bodyPath -Force
}
