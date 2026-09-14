#requires -Version 7.0
# Offline only: az is replaced by a mock; no Azure calls are made.
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '..\scripts\Create-ResourceGroup.ps1'
$configPath = Join-Path ([IO.Path]::GetTempPath()) "resource-group-test-$([guid]::NewGuid()).json"
function Assert($condition, $message) { if (!$condition) { throw "Resource group test failed: $message" } }
function az {
    $a = @($args)
    $global:LASTEXITCODE = 0
    $global:ResourceGroupTest.calls++
    Assert ($a[0] -eq 'group') 'only resource-group commands allowed'
    Assert ($a[[array]::IndexOf($a, '--subscription') + 1] -eq '11111111-1111-1111-1111-111111111111') 'subscription taken from config'
    Assert ($a[[array]::IndexOf($a, '--name') + 1] -eq 'offline-rg') 'group taken from config'
    switch ($a[1]) {
        'exists' { return ($global:ResourceGroupTest.exists | ConvertTo-Json) }
        'show' {
            return (@{
                id='/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/offline-rg'
                location='eastus'
                properties=@{ provisioningState=$global:ResourceGroupTest.state }
            } | ConvertTo-Json)
        }
        'create' {
            $global:ResourceGroupTest.creates++
            Assert ($a[[array]::IndexOf($a, '--location') + 1] -eq 'eastus') 'location taken from config'
            if ($global:ResourceGroupTest.fail) { $global:LASTEXITCODE = 1; return }
            $global:ResourceGroupTest.exists=$true
        }
        default { throw 'Unexpected command' }
    }
}
try {
    @{ subscriptionId='11111111-1111-1111-1111-111111111111'; resourceGroup='offline-rg'; region='eastus' } |
        ConvertTo-Json | Set-Content -LiteralPath $configPath
    $global:ResourceGroupTest = @{ exists=$false; creates=0; calls=0; state='Succeeded'; fail=$false }
    & $scriptPath -ConfigPath $configPath
    & $scriptPath -ConfigPath $configPath -Execute -WhatIf
    Assert ($global:ResourceGroupTest.creates -eq 0) 'preview and WhatIf never create'
    $output = & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false 6>&1 | Out-String
    Assert ($global:ResourceGroupTest.creates -eq 1 -and $output.Contains('Resource group ready')) 'explicit create and success message'
    & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false
    Assert ($global:ResourceGroupTest.creates -eq 1) 'existing group left unchanged'
    $global:ResourceGroupTest.exists=$false
    $global:ResourceGroupTest.fail=$true
    $rejected=$false
    try { & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false } catch { $rejected=$_.Exception.Message -like '*Azure CLI failed*' }
    Assert $rejected 'Azure creation error surfaced'
    $global:ResourceGroupTest.fail=$false
    $global:ResourceGroupTest.state='Failed'
    $rejected=$false
    try { & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false } catch { $rejected=$_.Exception.Message -like '*not confirmed*' }
    Assert $rejected 'failed provisioning is not reported successful'
    $before=$global:ResourceGroupTest.calls
    '{"subscriptionId":"<placeholder>","resourceGroup":"offline-rg","region":"eastus"}' | Set-Content -LiteralPath $configPath
    $rejected=$false
    try { & $scriptPath -ConfigPath $configPath -Execute } catch { $rejected=$_.Exception.Message -like '*config.json must provide*' }
    Assert ($rejected -and $global:ResourceGroupTest.calls -eq $before) 'invalid config rejected before Azure'
    Write-Host 'Offline resource-group checks passed: preview, WhatIf, config scope, creation, idempotence and failures.'
}
finally {
    Remove-Item -LiteralPath $configPath -Force
    Remove-Variable -Name ResourceGroupTest -Scope Global -ErrorAction SilentlyContinue
}
