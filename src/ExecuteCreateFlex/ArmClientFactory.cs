using Azure.Core;
using Azure.ResourceManager;

namespace ExecuteCreateFlex;

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
    /// <param name="location">
    /// Optional. When provided, the client targets the location-specific ARM endpoint
    /// (<c>https://{location}.management.azure.com</c>) as a temporary workaround for
    /// regions not yet running the latest SDK. Defaults to <c>null</c>, which uses the
    /// standard global endpoint.
    /// </param>
    public static ArmClient CreateScheduleClient(TokenCredential credential, string subscriptionId, string? location = null)
    {
        // Optionally inject a custom completion-notification header:
        // options.AddPolicy(new SetHeaderPolicy(), HttpPipelinePosition.PerCall);

        if (location is not null)
        {
            // TODO: Remove this branch once the SDK is fully rolled out to all regions.
            //       The permanent form is: return new ArmClient(credential, subscriptionId);
            var options = new ArmClientOptions
            {
                Environment = new ArmEnvironment(
                    new Uri($"https://{location}.management.azure.com"),
                    "https://management.core.windows.net/")
            };
            return new ArmClient(credential, subscriptionId, options);
        }

        return new ArmClient(credential, subscriptionId);
    }
}
