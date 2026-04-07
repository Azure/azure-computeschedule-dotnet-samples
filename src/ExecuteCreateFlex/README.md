# ExecuteCreateFlex Sample

This sample demonstrates the Azure Compute Schedule Flex create flow for virtual machines.

Use this README for setup and execution steps. REST payload and response details live in `rest-api-documentation.md`.

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

This sample loads settings from a local `.env` file in this folder.

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
dotnet build .\src\ExecuteCreateFlex\ExecuteCreateFlex.csproj
```

From the `src` directory:

```powershell
dotnet build .\ExecuteCreateFlex\ExecuteCreateFlex.csproj
```

From the project directory:

```powershell
dotnet build .\ExecuteCreateFlex.csproj
```

## Run

### API demo

From the repository root:

```powershell
dotnet run --project .\src\ExecuteCreateFlex\ExecuteCreateFlex.csproj -- --api-demo --resource-count 5
```

From the `src` directory:

```powershell
dotnet run --project .\ExecuteCreateFlex\ExecuteCreateFlex.csproj -- --api-demo --resource-count 5
```

From the `src/ExecuteCreateFlex` directory:

```powershell
dotnet run .\Program.cs -- --api-demo --resource-count 5
```

### Batch demo

From the repository root:

```powershell
dotnet run --project .\src\ExecuteCreateFlex\ExecuteCreateFlex.csproj -- --batch-demo --resource-count 200
```

From the `src` directory:

```powershell
dotnet run --project .\ExecuteCreateFlex\ExecuteCreateFlex.csproj -- --batch-demo --resource-count 200
```

From the `src/ExecuteCreateFlex` directory:

```powershell
dotnet run .\Program.cs -- --batch-demo --resource-count 200
```

## Command-Line Options

- `--api-demo`: runs the direct API demo
- `--batch-demo`: runs the batch demo
- `--resource-count <n>`: overrides the default requested VM count

Only one demo mode should be passed at a time.

## Expected Behavior

- The sample prints the selected demo mode and requested resource count
- A correlation ID is generated for the request
- The sample polls operation status and prints a final summary

## Troubleshooting

- If authentication fails, run `az login` and confirm the expected subscription is available.
- If configuration loading fails, verify that `.env` exists in this folder and contains valid values.
- If package restore fails, check `src/NuGet.config` and your feed access.

## Related Files

- `rest-api-documentation.md`: request and response documentation for the Flex create REST API
- `Program.cs`: entry point and CLI argument handling
- `ApiDemo.cs`: direct API demo flow
- `BatchDemo.cs`: batch demo flow
