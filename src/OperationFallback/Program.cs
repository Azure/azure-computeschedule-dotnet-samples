using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using UtilityMethods;

namespace OperationFallback;

/// <summary>
/// Demonstrates the operation fallback (OnFailureAction) feature.
///
/// Uncomment the scenario you want to run below. Each scenario shows
/// a different fallback configuration:
///   1. Hibernate → Deallocate fallback (with retries)
///   2. Start → clean-boot fallback (with retries)
///   3. Hibernate → Deallocate fallback (no retries, fallback only)
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        const string location = "eastus2euap";
        const string subscriptionId = "a4f8220e-84cb-47a6-b2c0-c1900805f616";
        const string resourceGroupName = "demo-rg";

        TokenCredential cred = new DefaultAzureCredential();
        ArmClient client = new(cred);
        var subscriptionResource = HelperMethods.GetSubscriptionResource(client, subscriptionId);

        var resourceIds = new List<ResourceIdentifier>()
        {
            new($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Compute/virtualMachines/dummy-vm-600"),
        };

        // Scenario 1: Hibernate with Deallocate fallback
        await HibernateFallback.RunAsync(subscriptionResource, resourceIds, location);

        // Scenario 2: Start with clean-boot fallback
        // await StartFallback.RunAsync(subscriptionResource, resourceIds, location);

        // Scenario 3: Hibernate with Deallocate fallback (no retry window)
        // await HibernateFallbackNoRetry.RunAsync(subscriptionResource, resourceIds, location);
    }
}
