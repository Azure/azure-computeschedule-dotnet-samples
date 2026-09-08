using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute.BulkActions;
using Azure.ResourceManager.Compute.BulkActions.Models;
using Azure.ResourceManager.Resources;
using System.ClientModel.Primitives;
using UtilityMethods;

namespace ExecuteStart
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            // Location: The location of the virtual machines
            const string location = "eastus2euap";

            // SubscriptionId: The subscription id under which the virtual machines are located, in this case, we are using a dummy subscriptionId
            const string subscriptionId = "a4f8220e-84cb-47a6-b2c0-c1900805f616";

            // ResourceGroupName: The resource group name under which the virtual machines are located, in this case, we are using a dummy resource group name
            const string resourceGroupName = "demo-rg";

            // Credential: The Azure credential used to authenticate the request
            TokenCredential cred = new DefaultAzureCredential();

            // Client: The Azure Resource Manager client used to interact with the Azure Resource Manager API
            ArmClient client = new(cred);
            var subscriptionResource = HelperMethods.GetSubscriptionResource(client, subscriptionId);
            var resourceGroupResource = await subscriptionResource.GetResourceGroupAsync(resourceGroupName);

            // Execution parameters for the request including the retry policy used by Scheduledactions to retry the operation in case of failures
            var executionParams = new BulkActionExecutionParameterDetail()
            {
                // Capacity recommendations are computed when a VM fails to start because of an allocation failure.
                CapacityRecommendationParameters = new BulkActionsCapacityRecommendationParametersContent()
                {
                    // Azure regions to consider when recommending alternative placement.
                    DesiredLocations = { "eastus2euap", "centraluseuap" },
                    // VM sizes to consider when recommending alternative capacity.
                    DesiredSizes = { "Standard_D2s_v5", "Standard_D4s_v5" },
                    // Return placement recommendations for individual availability zones.
                    IsAvailabilityZoneEnabled = true
                }
            };

            // List of virtual machine resource identifiers to perform execute/submit type operations on, in this case, we are using dummy VMs. Virtual Machines must all be under the same subscriptionid
            var resourceIds = new List<ResourceIdentifier>()
            {
                new($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Compute/virtualMachines/dummy-vm-600"),
                new($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Compute/virtualMachines/dummy-vm-611"),
                new($"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Compute/virtualMachines/dummy-vm-612"),
            };

            var executeStartRequest = new ExecuteStartContent(executionParams, new UserRequestResources(resourceIds));

            // Serialize the request to show the capacityRecommendationParameters wire format.
            Console.WriteLine(ModelReaderWriter.Write(executeStartRequest, ModelReaderWriterOptions.Json));

            StartResourceOperationResult result = await resourceGroupResource.Value.BulkStartOperationAsync(location, executeStartRequest);
            Console.WriteLine(ModelReaderWriter.Write(result, ModelReaderWriterOptions.Json));

            var operationIds = new HashSet<string>();
            foreach (var resourceResult in result.Results)
            {
                if (resourceResult.ErrorCode is not null)
                {
                    Console.WriteLine($"Start was not submitted for {resourceResult.ResourceId}: {resourceResult.ErrorCode} - {resourceResult.ErrorDetails}");
                    continue;
                }

                operationIds.Add(resourceResult.Operation.OperationId);
            }

            if (operationIds.Count == 0)
            {
                Console.WriteLine("No start operations were accepted for polling.");
                return;
            }

            await PollOperationsAsync(resourceGroupResource.Value, location, operationIds);
        }

        private static async Task PollOperationsAsync(
            ResourceGroupResource resourceGroup,
            string location,
            HashSet<string> operationIds)
        {
            var pendingOperationIds = new HashSet<string>(operationIds);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));

            try
            {
                while (pendingOperationIds.Count > 0)
                {
                    var request = new GetBulkOperationStatusContent(pendingOperationIds);
                    GetBulkOperationStatusResult statusResult = await resourceGroup.BulkGetOperationsStatusAsync(
                        location,
                        request,
                        timeout.Token);

                    foreach (var resourceResult in statusResult.Results)
                    {
                        if (resourceResult.ErrorCode is not null)
                        {
                            Console.WriteLine($"Could not get operation status for {resourceResult.ResourceId}: {resourceResult.ErrorCode} - {resourceResult.ErrorDetails}");
                            continue;
                        }

                        var operation = resourceResult.Operation;
                        Console.WriteLine($"Operation {operation.OperationId} for {resourceResult.ResourceId}: {operation.State}");

                        if (!IsTerminal(operation.State))
                        {
                            continue;
                        }

                        pendingOperationIds.Remove(operation.OperationId);
                        if (operation.State != BulkActionOperationState.Failed)
                        {
                            continue;
                        }

                        if (operation.Error is not null)
                        {
                            Console.WriteLine($"  Start failed: {operation.Error.ErrorCode} - {operation.Error.ErrorDetails}");
                        }

                        if (operation.CapacityRecommendation is null)
                        {
                            Console.WriteLine("  No capacity recommendation result was returned.");
                            continue;
                        }

                        DisplayCapacityRecommendation(operation.CapacityRecommendation);
                    }

                    if (pendingOperationIds.Count > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), timeout.Token);
                    }
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                Console.WriteLine($"Timed out waiting for operations: {string.Join(", ", pendingOperationIds)}");
            }
        }

        private static bool IsTerminal(BulkActionOperationState? state)
        {
            return state == BulkActionOperationState.Succeeded
                || state == BulkActionOperationState.Failed
                || state == BulkActionOperationState.Cancelled;
        }

        private static void DisplayCapacityRecommendation(CapacityRecommendation recommendation)
        {
            Console.WriteLine("  Capacity recommendation result:");
            Console.WriteLine($"    Status: {recommendation.Status} - Whether recommendation generation has not started, succeeded, failed, or was skipped.");
            Console.WriteLine($"    Error: {DisplayValue(recommendation.Error)} - Error code returned if recommendation generation failed.");
            Console.WriteLine($"    Error details: {DisplayValue(recommendation.ErrorDetails)} - Human-readable details for the recommendation-generation error.");

            var details = recommendation.Details;
            if (details is null)
            {
                Console.WriteLine("    Details: not returned - Placement details are normally present when recommendation generation succeeds.");
                return;
            }

            Console.WriteLine("    Details:");
            Console.WriteLine($"      Desired locations: {DisplayValues(details.DesiredLocations)} - Azure regions evaluated for alternative capacity.");
            Console.WriteLine($"      Recommendation requested on: {details.RecommendationRequestedOn?.ToString("O") ?? "not returned"} - UTC time when the capacity recommendation was requested.");
            Console.WriteLine($"      Desired sizes: {DisplayValues(details.DesiredSizes.Select(size => size.Sku))} - VM SKUs evaluated for alternative capacity.");
            Console.WriteLine($"      Split by availability zone: {details.IsSplitByAvailabilityZone?.ToString() ?? "not returned"} - Whether placement scores are reported separately for each availability zone.");

            if (details.PlacementScores.Count == 0)
            {
                Console.WriteLine("      Placement scores: none returned - No alternative placements were recommended.");
                return;
            }

            Console.WriteLine("      Placement scores - Candidate placements and their relative capacity assessment:");
            foreach (var placementScore in details.PlacementScores)
            {
                Console.WriteLine("        Candidate:");
                Console.WriteLine($"          SKU: {DisplayValue(placementScore.Sku)} - VM size evaluated for this candidate.");
                Console.WriteLine($"          Region: {DisplayValue(placementScore.Region)} - Azure region evaluated for this candidate.");
                Console.WriteLine($"          Availability zone: {DisplayValue(placementScore.AvailabilityZone)} - Zone evaluated when results are split by availability zone.");
                Console.WriteLine($"          Score: {DisplayValue(placementScore.Score)} - Relative likelihood that the candidate has capacity for the VM.");
                Console.WriteLine($"          Quota available: {placementScore.IsQuotaAvailable?.ToString() ?? "not returned"} - Whether the subscription has sufficient quota for the candidate.");
            }
        }

        private static string DisplayValues(IEnumerable<string> values)
        {
            var displayedValues = string.Join(", ", values);
            return string.IsNullOrEmpty(displayedValues) ? "none returned" : displayedValues;
        }

        private static string DisplayValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "not returned" : value;
        }
    }
}
