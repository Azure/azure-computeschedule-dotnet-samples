using Azure.ResourceManager.ComputeSchedule.Models;

namespace ExecuteVDICreateFlex.Scenarios;

internal static class CreateFlexScenarioCatalog
{
    private static readonly string[] s_singleSku = ["Standard_D2ads_v5"];
    private static readonly string[] s_twoSkus = ["Standard_D2ads_v5", "Standard_E2ads_v5"];
    private static readonly string[] s_threeSkus = ["Standard_D2ads_v5", "Standard_E2ads_v5", "Standard_D2ds_v5"];
    private static readonly string[] s_allZones = ["1", "2", "3"];

    public static IReadOnlyList<CreateFlexScenarioDefinition> All { get; } =
    [
        new(
            CreateFlexScenarioId.Scenario01,
            "1 SKU, regional, Regular, Prioritized, Windows",
            s_singleSku,
            IncludeVmSizeRanks: true,
            ComputeSchedulePriorityType.Regular,
            ComputeScheduleAllocationStrategy.Prioritized,
            ComputeScheduleOSType.Windows,
            []),
        new(
            CreateFlexScenarioId.Scenario02,
            "1 SKU, regional, Regular, LowestPrice, Windows",
            s_singleSku,
            IncludeVmSizeRanks: false,
            ComputeSchedulePriorityType.Regular,
            ComputeScheduleAllocationStrategy.LowestPrice,
            ComputeScheduleOSType.Windows,
            []),
        new(
            CreateFlexScenarioId.Scenario03,
            "1 SKU, regional, Spot, CapacityOptimized, Windows, Delete eviction, max price -1",
            s_singleSku,
            IncludeVmSizeRanks: false,
            ComputeSchedulePriorityType.Spot,
            ComputeScheduleAllocationStrategy.CapacityOptimized,
            ComputeScheduleOSType.Windows,
            [],
            SpotEvictionPolicy: "Delete",
            SpotMaxPricePerVm: -1),
        new(
            CreateFlexScenarioId.Scenario04,
            "3 SKUs, regional, Regular, Prioritized, Windows",
            s_threeSkus,
            IncludeVmSizeRanks: true,
            ComputeSchedulePriorityType.Regular,
            ComputeScheduleAllocationStrategy.Prioritized,
            ComputeScheduleOSType.Windows,
            []),
        new(
            CreateFlexScenarioId.Scenario05,
            "3 SKUs, regional, Regular, LowestPrice, Linux",
            s_threeSkus,
            IncludeVmSizeRanks: false,
            ComputeSchedulePriorityType.Regular,
            ComputeScheduleAllocationStrategy.LowestPrice,
            ComputeScheduleOSType.Linux,
            []),
        new(
            CreateFlexScenarioId.Scenario06,
            "1 SKU, zone 1, Regular, Prioritized, Windows",
            s_singleSku,
            IncludeVmSizeRanks: true,
            ComputeSchedulePriorityType.Regular,
            ComputeScheduleAllocationStrategy.Prioritized,
            ComputeScheduleOSType.Windows,
            ["1"]),
        new(
            CreateFlexScenarioId.Scenario07,
            "3 SKUs, zones 1/2/3, Regular, Prioritized, Windows, prioritized zone policy",
            s_threeSkus,
            IncludeVmSizeRanks: true,
            ComputeSchedulePriorityType.Regular,
            ComputeScheduleAllocationStrategy.Prioritized,
            ComputeScheduleOSType.Windows,
            s_allZones,
            ComputeScheduleDistributionStrategy.Prioritized,
            [("1", 0), ("2", 1), ("3", 2)]),
        new(
            CreateFlexScenarioId.Scenario08,
            "3 SKUs, zones 1/2/3, Regular, , Linux, best-effort single-zone policy",
            s_threeSkus,
            IncludeVmSizeRanks: false,
            ComputeSchedulePriorityType.Regular,
            ComputeScheduleAllocationStrategy.LowestPrice,
            ComputeScheduleOSType.Linux,
            s_allZones,
            ComputeScheduleDistributionStrategy.BestEffortSingleZone),
        new(
            CreateFlexScenarioId.Scenario09,
            "1 SKU, regional, Spot, LowestPrice, Windows, Delete eviction, max price -1",
            s_singleSku,
            IncludeVmSizeRanks: false,
            ComputeSchedulePriorityType.Spot,
            ComputeScheduleAllocationStrategy.LowestPrice,
            ComputeScheduleOSType.Windows,
            [],
            SpotEvictionPolicy: "Delete",
            SpotMaxPricePerVm: -1),
        new(
            CreateFlexScenarioId.Scenario10,
            "3 SKUs, regional, Spot, Prioritized, Linux, Deallocate eviction, max price 0.5",
            s_threeSkus,
            IncludeVmSizeRanks: true,
            ComputeSchedulePriorityType.Spot,
            ComputeScheduleAllocationStrategy.Prioritized,
            ComputeScheduleOSType.Linux,
            [],
            SpotEvictionPolicy: "Deallocate",
            SpotMaxPricePerVm: 0.5),
        new(
            CreateFlexScenarioId.Scenario11,
            "3 SKUs, zones 1/2/3, Spot, CapacityOptimized, Windows, prioritized zone policy",
            s_threeSkus,
            IncludeVmSizeRanks: false,
            ComputeSchedulePriorityType.Spot,
            ComputeScheduleAllocationStrategy.CapacityOptimized,
            ComputeScheduleOSType.Windows,
            s_allZones,
            ComputeScheduleDistributionStrategy.Prioritized,
            [("1", 0), ("2", 1), ("3", 2)],
            SpotEvictionPolicy: "Delete",
            SpotMaxPricePerVm: -1),
        new(
            CreateFlexScenarioId.Scenario12,
            "3 SKUs, regional, Regular, LowestPrice, no rank, Windows",
            s_threeSkus,
            IncludeVmSizeRanks: false,
            ComputeSchedulePriorityType.Regular,
            ComputeScheduleAllocationStrategy.LowestPrice,
            ComputeScheduleOSType.Windows,
            []),
        new(
            CreateFlexScenarioId.Scenario13,
            "1 SKU, zone 1, Spot, LowestPrice, Linux, Delete eviction, max price 0.1",
            s_singleSku,
            IncludeVmSizeRanks: false,
            ComputeSchedulePriorityType.Spot,
            ComputeScheduleAllocationStrategy.LowestPrice,
            ComputeScheduleOSType.Linux,
            ["1"],
            SpotEvictionPolicy: "Delete",
            SpotMaxPricePerVm: 0.1),
        new(
            CreateFlexScenarioId.Scenario14,
            "2 SKUs, zones 1/2, Regular, Prioritized, Windows, best-effort single-zone policy",
            s_twoSkus,
            IncludeVmSizeRanks: true,
            ComputeSchedulePriorityType.Regular,
            ComputeScheduleAllocationStrategy.Prioritized,
            ComputeScheduleOSType.Windows,
            ["1", "2"],
            ComputeScheduleDistributionStrategy.BestEffortSingleZone)
    ];

    public static bool TryGet(int scenarioNumber, out CreateFlexScenarioDefinition scenario)
    {
        scenario = All.FirstOrDefault(item => item.Number == scenarioNumber)!;
        return scenario is not null;
    }
}
