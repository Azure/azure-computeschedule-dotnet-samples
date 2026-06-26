using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute.BulkActions;
using Azure.ResourceManager.Compute.BulkActions.Models;
using Azure.ResourceManager.Resources;

namespace ComputePowerOpsTest;

public static class Program
{
    private static readonly TimeSpan InitialPollDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMinutes(45);

    public static async Task Main(string[] args)
    {
        if (!TryParseArgs(args, out CliOptions? options, out string? validationError))
        {
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                Console.WriteLine($"Argument error: {validationError}");
                Console.WriteLine();
            }

            PrintUsage();
            return;
        }

        if (options is null)
        {
            PrintUsage();
            return;
        }

        TokenCredential credential = CreateCredential();
        ArmClient armClient = new(credential);

        var subscription = armClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(options.SubscriptionId));
        ResourceGroupResource resourceGroup = await subscription.GetResourceGroupAsync(options.ResourceGroupName);

        var executionParams = new ScheduledActionExecutionParameterDetail
        {
            RetryPolicy = new BulkOperationRetryPolicy
            {
                RetryCount = options.RetryCount,
                RetryWindowInMinutes = options.RetryWindowInMinutes
            }
        };

        List<ResourceIdentifier> vmResourceIds = options.VmNames
            .Select(vmName =>
                new ResourceIdentifier($"/subscriptions/{options.SubscriptionId}/resourceGroups/{options.ResourceGroupName}/providers/Microsoft.Compute/virtualMachines/{vmName}"))
            .ToList();

        Console.WriteLine("Submitting ExecuteStart request...");
        StartResourceOperationResult startResponse = await resourceGroup.BulkStartOperationAsync(
            new ExecuteStartContent(executionParams, new UserRequestResources(vmResourceIds)));

        HashSet<string> startOperationIds = GetPollableOperationIds(startResponse.Results);
        if (startOperationIds.Count == 0)
        {
            Console.WriteLine("No start operations were accepted for polling. Exiting.");
            return;
        }

        Dictionary<string, ComputeBulkOperationDetails> startCompletionStates = await PollUntilOperationsCompleteAsync(
            resourceGroup,
            startOperationIds,
            "start");

        List<ResourceIdentifier> startedVmIds = startCompletionStates
            .Values
            .Where(op => op.State == ScheduledActionOperationState.Succeeded && op.ResourceId is not null)
            .Select(op => op.ResourceId!)
            .Distinct()
            .ToList();

        if (startedVmIds.Count == 0)
        {
            Console.WriteLine("No VMs reached Started state successfully, so ExecuteDeallocate is skipped.");
            return;
        }

        Console.WriteLine($"Submitting ExecuteDeallocate request for {startedVmIds.Count} VM(s)...");
        DeallocateResourceOperationResult deallocateResponse = await resourceGroup.BulkDeallocateOperationAsync(
            new ExecuteDeallocateContent(executionParams, new UserRequestResources(startedVmIds)));

        HashSet<string> deallocateOperationIds = GetPollableOperationIds(deallocateResponse.Results);
        if (deallocateOperationIds.Count == 0)
        {
            Console.WriteLine("No deallocate operations were accepted for polling.");
            return;
        }

        await PollUntilOperationsCompleteAsync(resourceGroup, deallocateOperationIds, "deallocate");
        Console.WriteLine("Start then deallocate workflow completed.");
    }

    private static bool TryParseArgs(string[] args, out CliOptions? options, out string? validationError)
    {
        options = null;
        validationError = null;

        if (args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string? subscriptionId = null;
        string? resourceGroupName = null;
        int retryCount = 3;
        int retryWindowInMinutes = 45;
        var vmNames = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--subscription-id":
                    if (!TryReadValue(args, ref i, out string? subscriptionValue))
                    {
                        validationError = "Missing value for --subscription-id.";
                        return false;
                    }

                    subscriptionId = subscriptionValue;
                    break;

                case "--resource-group":
                    if (!TryReadValue(args, ref i, out string? resourceGroupValue))
                    {
                        validationError = "Missing value for --resource-group.";
                        return false;
                    }

                    resourceGroupName = resourceGroupValue;
                    break;

                case "--vm-name":
                    if (!TryReadValue(args, ref i, out string? vmNameValue))
                    {
                        validationError = "Missing value for --vm-name.";
                        return false;
                    }

                    vmNames.Add(vmNameValue!);
                    break;

                case "--vm-names":
                    if (!TryReadValue(args, ref i, out string? vmNamesValue))
                    {
                        validationError = "Missing value for --vm-names.";
                        return false;
                    }

                    vmNames.AddRange(
                        vmNamesValue!
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(name => !string.IsNullOrWhiteSpace(name)));
                    break;

                case "--retry-count":
                    if (!TryReadValue(args, ref i, out string? retryCountValue) || !int.TryParse(retryCountValue, out retryCount))
                    {
                        validationError = "--retry-count must be an integer in range 0-7.";
                        return false;
                    }

                    break;

                case "--retry-window-minutes":
                    if (!TryReadValue(args, ref i, out string? retryWindowValue) || !int.TryParse(retryWindowValue, out retryWindowInMinutes))
                    {
                        validationError = "--retry-window-minutes must be an integer in range 5-120.";
                        return false;
                    }

                    break;

                default:
                    validationError = $"Unknown argument '{arg}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            validationError = "--subscription-id is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(resourceGroupName))
        {
            validationError = "--resource-group is required.";
            return false;
        }

        if (vmNames.Count == 0)
        {
            validationError = "At least one VM is required. Use --vm-name or --vm-names.";
            return false;
        }

        if (retryCount < 0 || retryCount > 7)
        {
            validationError = "--retry-count must be in range 0-7.";
            return false;
        }

        if (retryWindowInMinutes < 5 || retryWindowInMinutes > 120)
        {
            validationError = "--retry-window-minutes must be in range 5-120.";
            return false;
        }

        options = new CliOptions(
            subscriptionId,
            resourceGroupName,
            vmNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            retryCount,
            retryWindowInMinutes);
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        value = null;
        int valueIndex = index + 1;
        if (valueIndex >= args.Length)
        {
            return false;
        }

        if (args[valueIndex].StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        value = args[valueIndex];
        index = valueIndex;
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/ComputePowerOpsTest/ComputePowerOpsTest.csproj -- --subscription-id <SUBSCRIPTION_ID> --resource-group <RESOURCE_GROUP> (--vm-name <VM_NAME> [--vm-name <VM_NAME> ...] | --vm-names <VM1,VM2,...>) [--retry-count <0-7>] [--retry-window-minutes <5-120>]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --project src/ComputePowerOpsTest/ComputePowerOpsTest.csproj -- --subscription-id 00000000-0000-0000-0000-000000000000 --resource-group my-rg --vm-name vm-one --vm-name vm-two");
        Console.WriteLine("  dotnet run --project src/ComputePowerOpsTest/ComputePowerOpsTest.csproj -- --subscription-id 00000000-0000-0000-0000-000000000000 --resource-group my-rg --vm-names vm-one,vm-two --retry-count 3 --retry-window-minutes 45");
    }

    private static TokenCredential CreateCredential()
    {
        if (HasManagedIdentityEnvironment())
        {
            Console.WriteLine("Authentication mode: DefaultAzureCredential (Managed Identity enabled environment detected).");
            return new DefaultAzureCredential();
        }

        Console.WriteLine("Authentication mode: DefaultAzureCredential (Managed Identity excluded for local/dev environment).");
        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeManagedIdentityCredential = true
        });
    }

    private static bool HasManagedIdentityEnvironment()
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MSI_ENDPOINT"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IMDS_ENDPOINT"));
    }

    private sealed record CliOptions(
        string SubscriptionId,
        string ResourceGroupName,
        IReadOnlyList<string> VmNames,
        int RetryCount,
        int RetryWindowInMinutes);

    private static async Task<Dictionary<string, ComputeBulkOperationDetails>> PollUntilOperationsCompleteAsync(
        ResourceGroupResource resourceGroup,
        HashSet<string> operationIds,
        string operationLabel)
    {
        var pending = new HashSet<string>(operationIds);
        var completed = new Dictionary<string, ComputeBulkOperationDetails>();

        await Task.Delay(InitialPollDelay);
        var deadlineUtc = DateTimeOffset.UtcNow + PollTimeout;

        while (pending.Count > 0)
        {
            if (DateTimeOffset.UtcNow > deadlineUtc)
            {
                throw new TimeoutException($"Timed out while polling {operationLabel} operations.");
            }

            GetBulkOperationStatusResult status = await resourceGroup.BulkGetOperationsStatusAsync(
                new GetBulkOperationStatusContent(pending));

            foreach (ComputeBulkOperationResult result in status.Results)
            {
                if (result.Operation is null || string.IsNullOrWhiteSpace(result.Operation.OperationId))
                {
                    continue;
                }

                string operationId = result.Operation.OperationId;
                if (!pending.Contains(operationId))
                {
                    continue;
                }

                Console.WriteLine($"[{operationLabel}] operationId={operationId}, state={result.Operation.State}");
                if (IsTerminal(result.Operation.State))
                {
                    if (result.Operation.Error is not null)
                    {
                        Console.WriteLine(
                            $"[{operationLabel}] operationId={operationId} errorCode={result.Operation.Error.ErrorCode}, errorDetails={result.Operation.Error.ErrorDetails}");
                    }

                    completed[operationId] = result.Operation;
                    pending.Remove(operationId);
                }
            }

            if (pending.Count > 0)
            {
                await Task.Delay(PollInterval);
            }
        }

        return completed;
    }

    private static bool IsTerminal(ScheduledActionOperationState? state)
    {
        return state == ScheduledActionOperationState.Succeeded
            || state == ScheduledActionOperationState.Failed
            || state == ScheduledActionOperationState.Cancelled;
    }

    private static HashSet<string> GetPollableOperationIds(IEnumerable<ComputeBulkOperationResult> operationResults)
    {
        var operationIds = new HashSet<string>();

        foreach (ComputeBulkOperationResult result in operationResults)
        {
            if (!string.IsNullOrWhiteSpace(result.ErrorCode))
            {
                Console.WriteLine(
                    $"Request rejected for resourceId={result.ResourceId}. errorCode={result.ErrorCode}, errorDetails={result.ErrorDetails}");
                continue;
            }

            if (result.Operation is null || string.IsNullOrWhiteSpace(result.Operation.OperationId))
            {
                continue;
            }

            operationIds.Add(result.Operation.OperationId);
        }

        return operationIds;
    }
}
