# ExecuteVDICreateFlex scenarios

This folder contains compiling C# SDK examples for CreateFlex scenario shapes.

Run a scenario in preview mode to print the SDK request body without submitting it:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --scenario 12
```

List all scenario configurations:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --list-scenarios
```

Submit the scenario to ComputeSchedule by adding `--execute`:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --scenario 12 --execute
```

You can override the requested resource count:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --scenario 4 --resource-count 5
```

Scenario preview and execution write a sanitized log file by default under `.\logs`. Use `--log-file <path>` to choose a file or `--no-log-file` to disable file logging:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --scenario 12 --log-file .\logs\scenario-12.log
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --scenario 12 --no-log-file
```

| Scenario | Shape |
| --- | --- |
| 1 | 1 SKU, regional, Regular, Prioritized, Windows |
| 2 | 1 SKU, regional, Regular, LowestPrice, Windows |
| 3 | 1 SKU, regional, Spot, CapacityOptimized, Windows, Delete eviction, max price -1 |
| 4 | 3 SKUs, regional, Regular, Prioritized, Windows |
| 5 | 3 SKUs, regional, Regular, LowestPrice, Linux |
| 6 | 1 SKU, zone 1, Regular, Prioritized, Windows |
| 7 | 3 SKUs, zones 1/2/3, Regular, Prioritized, Windows, prioritized zone policy |
| 8 | 3 SKUs, zones 1/2/3, Regular, LowestPrice, Linux, best-effort single-zone policy |
| 9 | 1 SKU, regional, Spot, LowestPrice, Windows, Delete eviction, max price -1 |
| 10 | 3 SKUs, regional, Spot, Prioritized, Linux, Deallocate eviction, max price 0.5 |
| 11 | 3 SKUs, zones 1/2/3, Spot, CapacityOptimized, Windows, prioritized zone policy |
| 12 | 3 SKUs, regional, Regular, LowestPrice, no rank, Windows |
| 13 | 1 SKU, zone 1, Spot, LowestPrice, Linux, Delete eviction, max price 0.1 |
| 14 | 2 SKUs, zones 1/2, Regular, Prioritized, Windows, best-effort single-zone policy |

Spot-specific fields such as `evictionPolicy` and `maxPricePerVM` are preserved by round-tripping the `ComputeSchedulePriorityProfile` through the Azure SDK model serializer.
