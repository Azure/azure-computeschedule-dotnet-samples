#requires -Version 7.0
# Offline only: az and sleep are mocked. No real Azure requests or deletions.
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '..\scripts\Delete-ResourceGroup.ps1'
$configPath = Join-Path ([IO.Path]::GetTempPath()) "delete-rg-test-$([guid]::NewGuid()).json"
function Assert($condition, $message) { if (!$condition) { throw "Delete group test failed: $message" } }
function Start-Sleep { param([int] $Seconds) $global:DeleteGroupTest.sleeps++ }
function az {
    $a = @($args)
    $global:LASTEXITCODE = 0
    $global:DeleteGroupTest.calls++
    Assert ($a[[array]::IndexOf($a, '--subscription') + 1] -eq '11111111-1111-1111-1111-111111111111') 'explicit config subscription'
    if ($a[0] -eq 'resource' -and $a[1] -eq 'list') {
        Assert ($a[[array]::IndexOf($a, '--resource-group') + 1] -eq 'offline-rg') 'preview config group'
        return '[]'
    }
    Assert ($a[0] -eq 'group' -and $a[[array]::IndexOf($a, '--name') + 1] -eq 'offline-rg') 'exact group target'
    if ($a[1] -eq 'exists') {
        if ($global:DeleteGroupTest.badResponse) { return '{}' }
        if ($global:DeleteGroupTest.submitted) {
            if ($global:DeleteGroupTest.failPoll) { $global:LASTEXITCODE = 1; return }
            if ($global:DeleteGroupTest.pending -gt 0) { $global:DeleteGroupTest.pending-- }
            else { $global:DeleteGroupTest.exists=$false }
        }
        return ($global:DeleteGroupTest.exists | ConvertTo-Json)
    }
    if ($a[1] -eq 'delete') {
        Assert ($a -contains '--no-wait') 'asynchronous submission with visible polling'
        $global:DeleteGroupTest.deletes++
        if ($global:DeleteGroupTest.failDelete) { $global:LASTEXITCODE=1; return }
        $global:DeleteGroupTest.submitted=$true
        return
    }
    throw 'Unexpected command'
}
function Reset-Mock {
    $global:DeleteGroupTest = @{
        calls=0; deletes=0; sleeps=0; exists=$true; submitted=$false
        pending=2; failDelete=$false; failPoll=$false; badResponse=$false
    }
}
try {
    @{ subscriptionId='11111111-1111-1111-1111-111111111111'; resourceGroup='offline-rg' } |
        ConvertTo-Json | Set-Content -LiteralPath $configPath
    Reset-Mock
    & $scriptPath -ConfigPath $configPath
    & $scriptPath -ConfigPath $configPath -Execute -WhatIf
    Assert ($global:DeleteGroupTest.deletes -eq 0) 'preview and WhatIf never delete'
    $messages = & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false 6>&1 | Out-String
    Assert ($global:DeleteGroupTest.deletes -eq 1 -and $global:DeleteGroupTest.sleeps -eq 2) 'submit once and poll to completion'
    Assert ($messages.Contains('Deletion request accepted') -and $messages.Contains('Group still exists') -and
        $messages.Contains('Deletion complete')) 'progress messages'
    & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false
    Assert ($global:DeleteGroupTest.deletes -eq 1) 'already absent is idempotent'
    foreach ($failure in @('failDelete', 'failPoll', 'badResponse')) {
        Reset-Mock
        $global:DeleteGroupTest[$failure]=$true
        $rejected=$false
        try { & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false }
        catch { $rejected=$_.Exception.Message -match 'Azure CLI failed|Unexpected resource-group' }
        Assert $rejected "$failure must not report success"
    }
    Reset-Mock
    '{"subscriptionId":"<placeholder>","resourceGroup":"offline-rg"}' | Set-Content -LiteralPath $configPath
    $rejected=$false
    try { & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false }
    catch { $rejected=$_.Exception.Message -like '*config.json must provide*' }
    Assert ($rejected -and $global:DeleteGroupTest.calls -eq 0) 'reject invalid config before Azure calls'
    Write-Host 'Offline deletion checks passed: config scope, preview, WhatIf, progress, idempotence and failure propagation.'
}
finally {
    Remove-Item -LiteralPath $configPath -Force
    Remove-Variable -Name DeleteGroupTest -Scope Global -ErrorAction SilentlyContinue
}
