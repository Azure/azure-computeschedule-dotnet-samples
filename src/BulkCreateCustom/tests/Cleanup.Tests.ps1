#requires -Version 7.0
# Offline harness: the az function below replaces Azure CLI; no Azure calls are made.
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '..\scripts\Cleanup.ps1'
$root = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/offline-rg"
$operationId = "$root/providers/Microsoft.Compute/locations/eastus/bulkCreateCustom/22222222-2222-2222-2222-222222222222"
$name = 'demo-33333333333333333333333333333333-a-000'
$vmId = "$root/providers/Microsoft.Compute/virtualMachines/$name"
$nicId = "$root/providers/Microsoft.Network/networkInterfaces/nic-unique"
$diskId = "$root/providers/Microsoft.Compute/disks/disk-unique"
$inventory = Join-Path ([IO.Path]::GetTempPath()) "cleanup-test-$([guid]::NewGuid()).json"
$configPath = Join-Path ([IO.Path]::GetTempPath()) "cleanup-config-test-$([guid]::NewGuid()).json"

function Assert($condition, $message) {
    if (!$condition) { throw "Cleanup test failed: $message" }
}

function Start-Sleep {
    param([int] $Seconds)
    $global:CleanupTestState.sleeps++
}

function az {
    $a = @($args)
    $global:LASTEXITCODE = 0
    $global:CleanupTestState.calls.Add(($a -join ' '))
    if ($a[0] -eq 'group' -and $a[1] -eq 'exists') {
        return ($global:CleanupTestState.groupExists | ConvertTo-Json)
    }
    if ($a[0] -eq 'group' -and $a[1] -eq 'delete') {
        throw 'The cleanup script must never delete the resource group.'
    }
    if ($a[0] -eq 'resource' -and $a[1] -eq 'delete') {
        $id = $a[[array]::IndexOf($a, '--ids') + 1]
        Assert ($id.StartsWith("$root/providers/")) 'resource deletion restricted to config scope'
        Assert ($a -contains '--no-wait') 'resource deletion is polled with progress'
        $global:CleanupTestState.deletes.Add(($a -join ' '))
        if ($global:CleanupTestState.failGroup) { $global:LASTEXITCODE = 1; return }
        $global:CleanupTestState.pendingDeletes.Add($id)
        return
    }
    if ($a[0] -eq 'rest' -and $a[2] -eq 'get') {
        return (@{ id=$operationId; state=$global:CleanupTestState.state; tags=@{ sample='BulkCreateCustom' }; names=@($name) } | ConvertTo-Json)
    }
    if ($a[0] -eq 'resource' -and $a[1] -eq 'list') {
        if ($global:CleanupTestState.pendingDeletes.Count) {
            if ($global:CleanupTestState.remainingChecks -gt 0) {
                $global:CleanupTestState.remainingChecks--
            } else {
                $global:CleanupTestState.resources = @($global:CleanupTestState.resources | Where-Object { $_ -notin $global:CleanupTestState.pendingDeletes })
                $global:CleanupTestState.pendingDeletes.Clear()
            }
        }
        return (ConvertTo-Json -InputObject @($global:CleanupTestState.resources | ForEach-Object {
            @{ id=$_; type=($_ -split '/providers/')[1] -replace '/[^/]+$','' }
        }))
    }
    if ($a[0] -eq 'vm' -and $a[1] -eq 'show') {
        $osDisk = if ($global:CleanupTestState.foreignDisk) {
            '/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/other-rg/providers/Microsoft.Compute/disks/foreign'
        } else { $diskId }
        return (@{ nics=@($nicId); osDisk=$osDisk; disks=@() } | ConvertTo-Json)
    }
    if ($a[0] -eq 'disk' -and $a[1] -eq 'show') {
        return (@{ managedBy=$(if ($global:CleanupTestState.attachedDisk) { 'another-vm' } else { $null }); managedByExtended=@() } | ConvertTo-Json)
    }
    if ($a[0] -eq 'network' -and $a[2] -eq 'show') {
        return (@{ virtualMachine=$null; privateEndpoint=$null } | ConvertTo-Json)
    }
    if ($a -contains 'delete') {
        $global:CleanupTestState.deletes.Add(($a -join ' '))
        if ($global:CleanupTestState.failNic -and $a[0] -eq 'network') {
            $global:LASTEXITCODE = 1
            return
        }
        $i = [array]::IndexOf($a, '--ids')
        if ($i -ge 0) { $global:CleanupTestState.resources = @($global:CleanupTestState.resources | Where-Object { $_ -ne $a[$i+1] }) }
        return
    }
    throw "Unexpected mock CLI command: $($a -join ' ')"
}

try {
    $global:CleanupTestState = @{
        calls = [Collections.Generic.List[string]]::new()
        deletes = [Collections.Generic.List[string]]::new()
        state = 'Succeeded'
        failNic = $false
        foreignDisk = $false
        attachedDisk = $false
        groupExists = $true
        failGroup = $false
        pendingDeletes = [Collections.Generic.List[string]]::new()
        remainingChecks = 2
        sleeps = 0
    }
    $deletes = $global:CleanupTestState.deletes
    $unrelated = "$root/providers/Microsoft.Network/virtualNetworks/existing-vnet"
    $global:CleanupTestState.resources = @($vmId, $nicId, $diskId, $unrelated)
    @{ subscriptionId='11111111-1111-1111-1111-111111111111'; resourceGroup='offline-rg'; region='eastus' } |
        ConvertTo-Json | Set-Content -LiteralPath $configPath
    & $scriptPath -ConfigPath $configPath
    & $scriptPath -ConfigPath $configPath -Execute -WhatIf
    Assert ($deletes.Count -eq 0) 'full-group preview and WhatIf never delete'
    $messages = & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false 6>&1 | Out-String
    Assert ($deletes.Count -eq 4 -and $global:CleanupTestState.groupExists -and !$global:CleanupTestState.resources.Count) 'contents cleanup empties but preserves the group'
    Assert ($global:CleanupTestState.sleeps -eq 2) 'poll while deletion is pending'
    Assert ($messages.Contains('Deletion request accepted') -and $messages.Contains('Remaining resources:') -and
        $messages.Contains('resource group preserved')) 'print acceptance, waiting and preservation messages'
    & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false
    Assert ($deletes.Count -eq 4) 'already empty group does not trigger another deletion'
    $global:CleanupTestState.resources = @($vmId, $nicId, $diskId, $unrelated)
    $global:CleanupTestState.failGroup = $true
    $rejected = $false
    try { & $scriptPath -ConfigPath $configPath -Execute -Confirm:$false }
    catch { $rejected = $_.Exception.Message -like '*Cleanup blocked*' }
    Assert $rejected 'contents cleanup errors prevent false success'
    $global:CleanupTestState.failGroup = $false
    $deletes.Clear()
    $global:CleanupTestState.calls.Clear()
    & $scriptPath -OperationName '22222222-2222-2222-2222-222222222222' -ConfigPath $configPath -InventoryPath $inventory
    Assert ($global:CleanupTestState.calls[0].Contains("https://management.azure.com${operationId}?api-version=")) 'config supplies exact target scope'
    Assert ($deletes.Count -eq 0 -and !(Test-Path $inventory)) 'config preview does not delete'
    $before = $global:CleanupTestState.calls.Count
    '{"subscriptionId":"<placeholder>","resourceGroup":"offline-rg","region":"eastus"}' | Set-Content -LiteralPath $configPath
    $rejected = $false
    try { & $scriptPath -OperationName '22222222-2222-2222-2222-222222222222' -ConfigPath $configPath -Execute -Confirm:$false }
    catch { $rejected = $_.Exception.Message -like '*config.json must provide*' }
    Assert ($rejected -and $global:CleanupTestState.calls.Count -eq $before) 'invalid config rejected before Azure calls'
    & $scriptPath -OperationResourceId $operationId -InventoryPath $inventory
    Assert ($deletes.Count -eq 0 -and !(Test-Path $inventory)) 'preview must not delete or save inventory'
    & $scriptPath -OperationResourceId $operationId -InventoryPath $inventory -Execute -WhatIf
    Assert ($deletes.Count -eq 0 -and !(Test-Path $inventory)) 'WhatIf must not mutate'
    $global:CleanupTestState.foreignDisk = $true
    $rejected = $false
    try { & $scriptPath -OperationResourceId $operationId -InventoryPath $inventory -Execute -Confirm:$false }
    catch { $rejected = $_.Exception.Message -like '*out-of-scope*' }
    Assert ($rejected -and $deletes.Count -eq 0) 'cross-resource-group dependencies must stop cleanup before deletion'
    $global:CleanupTestState.foreignDisk = $false
    $global:CleanupTestState.state = 'Creating'
    $rejected = $false
    try { & $scriptPath -OperationResourceId $operationId -InventoryPath $inventory -Execute -Confirm:$false }
    catch { $rejected = $_.Exception.Message -like '*Wait for terminal*' }
    Assert $rejected 'active operation must be rejected'
    Assert ($deletes.Count -eq 0) 'active operation must not delete'
    $global:CleanupTestState.state = 'Failed'
    $global:CleanupTestState.failNic = $true
    $rejected = $false
    try { & $scriptPath -OperationResourceId $operationId -InventoryPath $inventory -Execute -Confirm:$false }
    catch { $rejected = $_.Exception.Message -like '*Azure CLI failed*' }
    Assert $rejected 'CLI failure must stop cleanup'
    Assert (Test-Path $inventory) 'inventory must precede VM deletion'
    Assert ($vmId -notin $global:CleanupTestState.resources -and $diskId -in $global:CleanupTestState.resources) 'stop before disk deletion after NIC failure'
    Assert (!@($deletes | Where-Object { $_ -like 'rest*' }).Count) 'operation record must survive incomplete cleanup'
    $global:CleanupTestState.failNic = $false
    $global:CleanupTestState.attachedDisk = $true
    $rejected = $false
    try { & $scriptPath -OperationResourceId $operationId -InventoryPath $inventory -Execute -Confirm:$false }
    catch { $rejected = $_.Exception.Message -like '*Disk is still attached*' }
    Assert ($rejected -and $diskId -in $global:CleanupTestState.resources) 'attached disk must not be deleted'
    $global:CleanupTestState.attachedDisk = $false
    & $scriptPath -OperationResourceId $operationId -InventoryPath $inventory -Execute -Confirm:$false
    Assert ($global:CleanupTestState.resources.Count -eq 1 -and $global:CleanupTestState.resources[0] -eq $unrelated) 'only inventoried resources deleted'
    Assert ($deletes[-1] -like 'rest*delete*deleteInstances=false*') 'delete operation last without cascading instance deletion'
    Assert ($deletes[0] -like 'vm delete*') 'delete VMs before NICs/disks'
    Write-Host 'Offline cleanup checks passed: preview, WhatIf, active-state guard, failure propagation, resume inventory, scoped ordering.'
}
finally {
    Remove-Item -LiteralPath $inventory -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $configPath -Force -ErrorAction SilentlyContinue
    Remove-Variable -Name CleanupTestState -Scope Global -ErrorAction SilentlyContinue
}
