using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ComputeSchedule;
using Azure.ResourceManager.ComputeSchedule.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Configuration;

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
        var vmNames = settings.GetSection("VmNames").GetChildren().Select(c => c.Value!).ToArray();
        if (vmNames.Length == 0) throw new InvalidOperationException("VmNames is required in appsettings.json");
        string scenario = settings["Scenario"] ?? "HibernateFallback";

        TokenCredential cred = new DefaultAzureCredential();
        ArmClient client = new(cred, subscriptionId);

        var subscriptionResourceId = SubscriptionResource.CreateResourceIdentifier(subscriptionId);
        var subscriptionResource = client.GetSubscriptionResource(subscriptionResourceId);

        var resourceIds = vmNames
            .Select(vm => new ResourceIdentifier($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Compute/virtualMachines/{vm}"))
            .ToList();

        switch (scenario)
        {
            case "HibernateFallback":
                await HibernateWithDeallocateFallback.RunAsync(subscriptionResource, resourceIds, location);
                break;
            case "StartFallback":
                await StartWithCleanBootFallback.RunAsync(subscriptionResource, resourceIds, location);
                break;
            case "HibernateFallbackNoRetry":
                await HibernateFallbackOnlyNoRetry.RunAsync(subscriptionResource, resourceIds, location);
                break;
            case "CreateFallback":
                await CreateWithDeleteFallback.RunAsync(subscriptionResource, location, subscriptionId, resourceGroupName);
                break;
            default:
                Console.WriteLine($"Unknown scenario: {scenario}");
                Console.WriteLine("Valid scenarios: HibernateFallback, StartFallback, HibernateFallbackNoRetry, CreateFallback");
                break;
        }
    }

    /// <summary>
    /// Polls operation status until all operations reach a terminal state (Succeeded, Failed, or Cancelled).
    /// </summary>
    internal static async Task<Dictionary<string, ResourceOperationDetails>> PollUntilComplete(
        HashSet<string> operationIds,
        string location,
        SubscriptionResource subscriptionResource)
    {
        var completed = new Dictionary<string, ResourceOperationDetails>();
        var pending = new HashSet<string>(operationIds);

        while (pending.Count > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));

            var statusRequest = new GetOperationStatusContent(pending, Guid.NewGuid().ToString());
            var response = await subscriptionResource.GetVirtualMachineOperationStatusAsync(location, statusRequest);

            foreach (var result in response.Value.Results)
            {
                var details = result.Operation;
                if (details is null)
                {
                    continue;
                }

                var state = details.State;
                if (state == ScheduledActionOperationState.Succeeded ||
                    state == ScheduledActionOperationState.Failed ||
                    state == ScheduledActionOperationState.Cancelled)
                {
                    completed[details.OperationId] = details;
                    pending.Remove(details.OperationId);
                }
                else
                {
                    Console.WriteLine($"[Polling] {details.OperationId}: {state}");
                }
            }
        }

        return completed;
    }
}
