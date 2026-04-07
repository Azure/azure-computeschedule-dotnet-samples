# Azure Compute Schedule .NET Samples

This project demonstrates how to use the Azure ComputeSchedule SDK to automate virtual machine lifecycle operations using scheduled actions.

## Projects

| Project | Description |
|---|---|
| `ExecuteCreate` | Create VMs via ComputeSchedule with a standard provisioning payload |
| `ExecuteCreateFlex` | Create VMs using the Flex API — prioritized VM size fallbacks for flexible allocation |
| `ExecuteStart` | Start existing VMs via ComputeSchedule |
| `ExecuteDeallocate` | Deallocate VMs via ComputeSchedule |
| `ExecuteDelete` | Delete VMs via ComputeSchedule |
| `ExecuteHibernate` | Hibernate VMs via ComputeSchedule |
| `AllScenarios` | Combines multiple operations in a single runnable program |

## Features

- Authenticate with Azure using `DefaultAzureCredential`
- Create, start, deallocate, delete, and hibernate virtual machines via scheduled actions
- Flex provisioning: specify prioritized VM size profiles — ComputeSchedule picks the best available SKU
- Automatically creates a virtual network and subnet before VM provisioning
- Handles operation polling, terminal-state detection, and error scenarios
- Demonstrates retry policies for scheduled actions
- Configurable via `.env` file (no hardcoded secrets)

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An Azure subscription
- An existing resource group in Azure
- Azure CLI installed and logged in (`az login`) — used by `DefaultAzureCredential`
- NuGet feeds configured (see `src/NuGet.config`):
  - `nuget.org` — public packages (including `Unofficial.Azure.ResourceManager.ComputeSchedule`)

## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/Azure/azure-computeschedule-dotnet-samples.git
cd azure-computeschedule-dotnet-samples/src
```

### 2. Configure credentials

Authenticate with Azure using the Azure CLI:

```bash
az login
```

`DefaultAzureCredential` will pick this up automatically.

### 3. Set up environment variables

Each project that requires configuration ships with a `.env.example` file. Copy it to `.env` and fill in your values:

```bash
# Example for ExecuteCreateFlex
cd src/ExecuteCreateFlex
cp .env.example .env
```

Then edit `.env`:

```
AZURE_SUBSCRIPTION_ID=<your-subscription-id>
AZURE_RESOURCE_GROUP=<your-resource-group>
AZURE_LOCATION=eastus2euap
AZURE_VNET_NAME=flex-vnet
AZURE_SUBNET_NAME=flex-subnet
AZURE_VM_PREFIX=sampleflex
AZURE_VM_ADMIN_USERNAME=<admin-username>
AZURE_VM_ADMIN_PASSWORD=<strong-password>
```

> **Note:** `.env` is in `.gitignore` and will never be committed. Only `.env.example` is tracked.

### 5. Build and run

```bash
# Build the entire solution
cd src
dotnet build

# Run a specific project from the repository root
dotnet run --project src/ExecuteCreateFlex/ExecuteCreateFlex.csproj -- --api-demo --resource-count 5
dotnet run --project src/ExecuteCreate/ExecuteCreate.csproj
dotnet run --project src/ExecuteStart/ExecuteStart.csproj
dotnet run --project src/ExecuteDeallocate/ExecuteDeallocate.csproj
dotnet run --project src/ExecuteDelete/ExecuteDelete.csproj
dotnet run --project src/ExecuteHibernate/ExecuteHibernate.csproj

# Or, if you are already in src/
dotnet run --project ./ExecuteCreateFlex/ExecuteCreateFlex.csproj -- --batch-demo --resource-count 200

# Or, if you are already in src/ExecuteCreateFlex/
dotnet run -- --api-demo --resource-count 5
dotnet run -- --batch-demo --resource-count 200
```

## Project Structure

```
src/
├── Common/                   # Shared library: polling, helper methods, operation wrappers
│   ├── ComputescheduleOperations.cs
│   ├── HelperMethods.cs
│   ├── SetHeaderPolicy.cs
│   └── UtilityMethods.cs
├── ExecuteCreate/            # Standard VM create operation
├── ExecuteCreateFlex/        # Flex VM create operation (prioritized size profiles)
├── ExecuteStart/             # VM start operation
├── ExecuteDeallocate/        # VM deallocate operation
├── ExecuteDelete/            # VM delete operation
├── ExecuteHibernate/         # VM hibernate operation
├── AllScenarios/             # Combined scenarios
├── NuGet.config              # Feed configuration (public + private)
└── azure-computeschedule-dotnet-samples.sln
```