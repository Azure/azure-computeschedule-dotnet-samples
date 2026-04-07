using Azure.Core;
using Azure.ResourceManager;

namespace ExecuteVDICreateFlex;

/// <summary>
/// Factory methods for creating ARM clients with the correct configuration
/// for each phase of the ExecuteCreateFlex workflow.
/// </summary>
internal static class ArmClientFactory
{
    /// <summary>
    /// Creates a standard ARM client for general operations such as fetching
    /// resource groups and creating virtual networks.
    /// </summary>
    public static ArmClient CreateStandardClient(TokenCredential credential) =>
        new(credential);

    /// <summary>
    /// Creates an ARM client with the Microsoft.Network API version pinned to
    /// <c>2025-03-01</c>, required for reliable VNet creation.
    /// </summary>
    public static ArmClient CreateVNetClient(TokenCredential credential, string subscriptionId)
    {
        var options = new ArmClientOptions();
        options.SetApiVersion(new ResourceType("Microsoft.Network/virtualNetworks"), "2025-03-01");
        return new ArmClient(credential, subscriptionId, options);
    }

    /// <summary>
    /// Creates an ARM client for ComputeSchedule operations.
    /// </summary>
    /// <param name="credential">The Azure credential used to authenticate.</param>
    /// <param name="subscriptionId">The Azure subscription ID.</param>
    public static ArmClient CreateScheduleClient(TokenCredential credential, string subscriptionId)
    {
        return new ArmClient(credential, subscriptionId);
    }
}
