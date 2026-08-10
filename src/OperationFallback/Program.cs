using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Configuration;
using UtilityMethods;

namespace OperationFallback;

/// <summary>
/// Demonstrates the operation fallback (OnFailureAction) feature.
///
/// Configure your environment in appsettings.json before running.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        // Load settings from appsettings.json
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var settings = config.GetSection("Settings");
        string subscriptionId = settings["SubscriptionId"] ?? throw new InvalidOperationException("SubscriptionId is required in appsettings.json");
        string resourceGroupName = settings["ResourceGroupName"] ?? throw new InvalidOperationException("ResourceGroupName is required in appsettings.json");
        string location = settings["Location"] ?? throw new InvalidOperationException("Location is required in appsettings.json");
        // ArmLocation: The ARM location used to target the Scheduledactions endpoint
        string armLocation = settings["ArmLocation"] ?? throw new InvalidOperationException("ArmLocation is required in appsettings.json");
        var vmNames = settings.GetSection("VmNames").GetChildren().Select(c => c.Value!).ToArray();
        if (vmNames.Length == 0) throw new InvalidOperationException("VmNames is required in appsettings.json");
        string scenario = settings["Scenario"] ?? "HibernateFallback";

        TokenCredential cred = new DefaultAzureCredential();

        // Adding custom headers to the ARM client (optional)
        var customHeaders = new Dictionary<string, string>
        {
            ["x-ms-sa-completion-notification"] = "true",
        };
        var clientOptions = HelperMethods.GetGeneralOptions(armLocation);
        clientOptions.AddPolicy(new SetHeaderPolicy(customHeaders), HttpPipelinePosition.PerCall);

        ArmClient client = new(cred, subscriptionId, clientOptions);

        var subscriptionResourceId = SubscriptionResource.CreateResourceIdentifier(subscriptionId);
        var subscriptionResource = client.GetSubscriptionResource(subscriptionResourceId);

        var resourceIds = vmNames
            .Select(vm => new ResourceIdentifier($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Compute/virtualMachines/{vm}"))
            .ToList();

        switch (scenario)
        {
            case "HibernateFallback":
                var hibernateResourceGroup = (await subscriptionResource.GetResourceGroupAsync(resourceGroupName)).Value;
                await HibernateWithDeallocateFallback.RunAsync(hibernateResourceGroup, resourceIds, location);
                break;
            case "StartFallback":
                var startResourceGroup = (await subscriptionResource.GetResourceGroupAsync(resourceGroupName)).Value;
                await StartWithCleanBootFallback.RunAsync(startResourceGroup, resourceIds, location);
                break;
            case "HibernateFallbackNoRetry":
                var noRetryResourceGroup = (await subscriptionResource.GetResourceGroupAsync(resourceGroupName)).Value;
                await HibernateFallbackOnlyNoRetry.RunAsync(noRetryResourceGroup, resourceIds, location);
                break;
            case "CreateFallback":
                var createSettings = config.GetSection("Settings:Create");
                string adminUsername = createSettings["AdminUsername"] ?? throw new InvalidOperationException("Settings:Create:AdminUsername is required in appsettings.json");
                string adminPassword = createSettings["AdminPassword"] ?? throw new InvalidOperationException("Settings:Create:AdminPassword is required in appsettings.json");
                await CreateWithDeleteFallback.RunAsync(location, subscriptionId, resourceGroupName, adminUsername, adminPassword);
                break;
            default:
                Console.WriteLine($"Unknown scenario: {scenario}");
                Console.WriteLine("Valid scenarios: HibernateFallback, StartFallback, HibernateFallbackNoRetry, CreateFallback");
                break;
        }
    }
}
