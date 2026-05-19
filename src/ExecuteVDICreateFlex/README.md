# ExecuteVDICreateFlex Sample

This sample demonstrates the Azure Compute Schedule VDI Flex create flow for virtual machines.

Use this README for setup and execution steps. REST payload and response details live in [rest-api-documentation.md](./rest-api-documentation.md).

## Documentation

- [rest-api-documentation.md](./rest-api-documentation.md): request and response reference for the Flex create API

## What This Sample Does

- Authenticates with Azure using `DefaultAzureCredential`
- Creates or reuses network prerequisites needed for the request
- Submits a Flex create request to Scheduled Actions
- Polls operation status until the requested resources reach terminal states
- Supports two demo modes: API demo and batch demo

## Prerequisites

- .NET SDK installed
- Azure CLI installed
- Azure CLI signed in with `az login`
- Access to an Azure subscription and resource group
- Valid package sources from `src/NuGet.config`

## Configuration

This sample loads settings from a local `.env` file or environment variables.

The loader searches for `.env` from the current working directory and parent directories, so the sample can be run from the repository root, `src`, or the project folder.

1. Copy `.env.example` to `.env`.
2. Fill in the required values.

Expected settings:

```env
AZURE_SUBSCRIPTION_ID=<your-subscription-id>
AZURE_RESOURCE_GROUP=<your-resource-group>
AZURE_LOCATION=eastus2euap
AZURE_VNET_NAME=flex-vnet
AZURE_SUBNET_NAME=flex-subnet
AZURE_VM_PREFIX=sampleflex
AZURE_VM_ADMIN_USERNAME=<admin-username>
AZURE_VM_ADMIN_PASSWORD=<strong-password>
```

## Build

From the repository root:

```powershell
dotnet build .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj
```

From the `src` directory:

```powershell
dotnet build .\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj
```

From the project directory:

```powershell
dotnet build .\ExecuteVDICreateFlex.csproj
```

## Run

### API demo

From the repository root:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --api-demo --resource-count 5
```

From the `src` directory:

```powershell
dotnet run --project .\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --api-demo --resource-count 5
```

From the `src/ExecuteVDICreateFlex` directory:

```powershell
dotnet run -- --api-demo --resource-count 5
```

### Batch demo

From the repository root:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --batch-demo --resource-count 200
```

From the `src` directory:

```powershell
dotnet run --project .\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --batch-demo --resource-count 200
```

From the `src/ExecuteVDICreateFlex` directory:

```powershell
dotnet run -- --batch-demo --resource-count 200
```

### Scenario examples

Preview one of the CreateFlex scenario examples without submitting it:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --scenario 12
```

Submit the selected scenario to ComputeSchedule:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --scenario 12 --execute
```

Scenario definitions live in [Scenarios](./Scenarios/README.md).

List all scenario configurations:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --list-scenarios
```

## Command-Line Options

- `--api-demo`: runs the direct API demo
- `--batch-demo`: runs the batch demo
- `--batch-request-demo`: alias for `--batch-demo`
- `--list-scenarios`: lists all CreateFlex scenario configurations
- `--scenario <n>`: prints the request body for one of the CreateFlex scenario examples
- `--execute`: submits a selected `--scenario` request after printing it
- `--resource-count <n>`: overrides the default requested VM count
- `--log-file <path>`: writes detailed Flex create diagnostics to the specified file
- `--no-log-file`: disables per-run file logging

Only one demo mode should be passed at a time.

If no demo mode is provided, the sample prints the supported `dotnet run -- ...` usage examples and exits.

## File Logging

Flex create file logging is enabled by default. Each run writes a timestamped log under a `logs` folder in the current working directory and prints the log path at startup.

The log captures setup details, correlation IDs, sanitized request payloads, submission results, polling summaries, batch progress, failed operations, and unhandled exceptions. Sensitive JSON fields such as `adminPassword`, `protectedSettings`, and `protectedSettingsFromKeyVault` are redacted in log payloads.

Use a custom path or disable logging:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --scenario 12 --log-file .\logs\scenario-12.log
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --scenario 12 --no-log-file
```

## Expected Behavior

- The sample prints the selected demo mode and requested resource count
- A correlation ID is generated for the request
- The sample polls operation status and prints a final summary

## Troubleshooting

- If you run from `src/ExecuteVDICreateFlex`, use `dotnet run -- ...`. `dotnet run Program.cs` is not the standard SDK-style invocation for this project.
- If you run from `src`, use `dotnet run --project ./ExecuteVDICreateFlex/ExecuteVDICreateFlex.csproj -- ...`.
- If authentication fails, run `az login` and confirm the expected subscription is available.
- If configuration loading fails, verify that `.env` exists in this folder and contains valid values.
- If package restore fails, check `src/NuGet.config` and your feed access.

## Related Files

- [rest-api-documentation.md](./rest-api-documentation.md): request and response documentation for the Flex create REST API
- `Program.cs`: entry point and CLI argument handling
- `ApiDemo.cs`: direct API demo flow
- `BatchDemo.cs`: batch demo flow
