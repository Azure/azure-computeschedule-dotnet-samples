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
- Runs the API sample flow from request creation through final status polling, including an optional zonal variant
- Includes a JSON string variant that converts a request body string into `ExecuteCreateFlexContent` before submission

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

From the repository root:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --api-demo --resource-count 5
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --api-demo-with-zones --resource-count 5
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --api-demo-json-string --resource-count 5
```

From the `src` directory:

```powershell
dotnet run --project .\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --api-demo --resource-count 5
dotnet run --project .\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --api-demo-with-zones --resource-count 5
dotnet run --project .\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --api-demo-json-string --resource-count 5
```

From the `src/ExecuteVDICreateFlex` directory:

```powershell
dotnet run -- --api-demo --resource-count 5
dotnet run -- --api-demo-with-zones --resource-count 5
dotnet run -- --api-demo-json-string --resource-count 5
```

## Command-Line Options

- `--api-demo`: runs the direct API demo
- `--api-demo-with-zones`: runs the direct API demo with zones `1`, `2`, and `3` plus a prioritized zone allocation policy
- `--api-demo-json-string`: runs the direct API demo by converting a JSON request body string into `ExecuteCreateFlexContent`
- `--resource-count <n>`: overrides the default requested VM count
- `--log-file <path>`: writes detailed Flex create diagnostics to the specified file
- `--no-log-file`: disables per-run file logging

If no demo mode is provided, the sample prints the supported `dotnet run -- ...` usage examples and exits.

## File Logging

Flex create file logging is enabled by default. Each run writes a timestamped log under a `logs` folder in the current working directory and prints the log path at startup.

The log captures setup details, correlation IDs, sanitized request payloads, submission results, polling summaries, failed operations, and unhandled exceptions. Sensitive JSON fields such as `adminPassword`, `protectedSettings`, and `protectedSettingsFromKeyVault` are redacted in log payloads.

Use a custom path or disable logging:

```powershell
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --api-demo --log-file .\logs\api-demo.log
dotnet run --project .\src\ExecuteVDICreateFlex\ExecuteVDICreateFlex.csproj -- --api-demo --no-log-file
```

## Expected Behavior

- The sample prints the selected demo mode and requested resource count
- A correlation ID is generated for the request
- The JSON string demo deserializes the request body string before submitting the same Flex create API call
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
- `ApiDemoWithZones.cs`: zonal API demo flow
- `JsonStringApiDemo.cs`: JSON request body string conversion demo
