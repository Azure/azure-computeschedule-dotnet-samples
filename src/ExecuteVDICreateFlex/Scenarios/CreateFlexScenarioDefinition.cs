using Azure.ResourceManager.ComputeSchedule.Models;

namespace ExecuteVDICreateFlex.Scenarios;

internal sealed record CreateFlexScenarioDefinition(
    CreateFlexScenarioId Id,
    string Name,
    IReadOnlyList<string> VmSizeNames,
    bool IncludeVmSizeRanks,
    ComputeSchedulePriorityType PriorityType,
    ComputeScheduleAllocationStrategy AllocationStrategy,
    ComputeScheduleOSType OsType,
    IReadOnlyList<string> Zones,
    ComputeScheduleDistributionStrategy? ZoneDistributionStrategy = null,
    IReadOnlyList<(string Zone, int Rank)>? ZonePreferences = null,
    string? SpotEvictionPolicy = null,
    double? SpotMaxPricePerVm = null)
{
    public int Number => (int)Id;

    public bool IsSpot => PriorityType == ComputeSchedulePriorityType.Spot;

    public bool HasZoneAllocationPolicy => ZoneDistributionStrategy.HasValue;
}
