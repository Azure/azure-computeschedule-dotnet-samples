#requires -Version 7.0
# Offline only: replace az with a mock, never invoke Azure CLI.
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '..\scripts\Create-Network.ps1'
$configPath = Join-Path ([IO.Path]::GetTempPath()) "network-test-$([guid]::NewGuid()).json"
$subnetId = '/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/network-rg/providers/Microsoft.Network/virtualNetworks/test-vnet/subnets/test-subnet'
$vnetId = $subnetId -replace '/subnets/[^/]+$', ''
function Assert($condition, $message) { if (!$condition) { throw "Network test failed: $message" } }
function az {
    $a = @($args)
    $global:LASTEXITCODE = 0
    $global:NetworkTest.calls.Add(($a -join ' '))
    if ($a[0] -eq 'group') { return 'true' }
    if ($a[0] -eq 'rest') {
        $bodyFile = $a[[array]::IndexOf($a, '--body') + 1].Substring(1)
        $global:NetworkTest.body = Get-Content -LiteralPath $bodyFile -Raw | ConvertFrom-Json
        $global:NetworkTest.puts++
        if ($global:NetworkTest.failPut) { $global:LASTEXITCODE = 1 }
        return
    }
    if ($a -contains 'wait') { return }
    if ($a -contains 'show') {
        return (@{ addressPrefix='10.0.8.0/21'; defaultOutboundAccess=$false; provisioningState='Succeeded' } | ConvertTo-Json)
    }
    if ($a -contains 'list') { return (ConvertTo-Json -InputObject @($global:NetworkTest.networks) -Depth 15) }
    throw "Unexpected command: $($a -join ' ')"
}
try {
    @{ subscriptionId='11111111-1111-1111-1111-111111111111'; resourceGroup='vm-rg'; region='eastus'; subnetId=$subnetId } |
        ConvertTo-Json | Set-Content -LiteralPath $configPath
    $global:NetworkTest = @{ calls=[Collections.Generic.List[string]]::new(); networks=@(); puts=0; failPut=$false }
    & $scriptPath -ConfigPath $configPath
    & $scriptPath -ConfigPath $configPath -Execute -WhatIf
    Assert ($global:NetworkTest.puts -eq 0) 'preview and WhatIf do not create'
    & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false
    Assert ($global:NetworkTest.puts -eq 1) 'one atomic VNet/subnet PUT'
    Assert ($global:NetworkTest.body.properties.subnets[0].properties.addressPrefix -eq '10.0.8.0/21') 'larger /21 default'
    Assert ($global:NetworkTest.body.properties.subnets[0].properties.defaultOutboundAccess -eq $false) 'private subnet at creation'
    Assert ($global:NetworkTest.calls.Where({ $_ -like "*--url https://management.azure.com$vnetId`?*" }).Count -eq 1) 'subnetId determines network group'
    $net = @{
        id=$vnetId; location='eastus'; provisioningState='Succeeded'
        addressSpace=@{ addressPrefixes=@('10.0.0.0/16') }
        subnets=@(@{ name='test-subnet'; addressPrefix='10.0.8.0/21'; defaultOutboundAccess=$false; provisioningState='Succeeded' })
    }
    $global:NetworkTest.networks = @($net)
    & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false
    Assert ($global:NetworkTest.puts -eq 1) 'existing matching subnet unchanged'
    $net.subnets[0].addressPrefix='10.0.2.0/24'
    $rejected=$false
    try { & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false } catch { $rejected=$_.Exception.Message -like '*Refusing to resize*' }
    Assert ($rejected -and $global:NetworkTest.puts -eq 1) 'occupied old subnet is not resized'
    $net.subnets[0].addressPrefix='10.0.8.0/21'
    $net.subnets[0].defaultOutboundAccess = $true
    $rejected=$false
    try { & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false } catch { $rejected=$_.Exception.Message -like '*does not explicitly disable*' }
    Assert ($rejected -and $global:NetworkTest.puts -eq 1) 'existing connectivity not overwritten'
    $net.subnets=@(@{ name='other'; addressPrefix='10.0.9.0/24' })
    $rejected=$false
    try { & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false } catch { $rejected=$_.Exception.Message -like '*overlaps*' }
    Assert $rejected 'overlap rejected'
    $net.subnets=@(@{ name='old-subnet'; addressPrefix='10.0.2.0/24'; ipConfigurations=@(@{ id='existing-nic' }) })
    & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false
    Assert ($global:NetworkTest.puts -eq 2 -and $global:NetworkTest.body.properties.defaultOutboundAccess -eq $false) 'add subnet without rewriting VNet'
    Assert ($global:NetworkTest.body.properties.addressPrefix -eq '10.0.8.0/21' -and
        $net.subnets[0].addressPrefix -eq '10.0.2.0/24') 'new /21 coexists with old /24'
    $global:NetworkTest.networks=@()
    $global:NetworkTest.failPut=$true
    $rejected=$false
    try { & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false } catch { $rejected=$_.Exception.Message -like '*Azure CLI failed*' }
    Assert $rejected 'creation failure surfaced'
    $rejected=$false
    try { & $scriptPath -ConfigPath $configPath -SubnetAddressPrefix '10.0.2.0/25' } catch { $rejected=$_.Exception.Message -like '*at least 150*' }
    Assert $rejected 'undersized subnet rejected'
    Write-Host 'Offline network checks passed: preview, WhatIf, policy setting, exact target, existing network protection, overlap and failure handling.'
}
finally {
    Remove-Item -LiteralPath $configPath -Force
    Remove-Variable -Name NetworkTest -Scope Global -ErrorAction SilentlyContinue
}
