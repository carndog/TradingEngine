# Azure development deployment

This document describes how to build, validate, deploy and tear down the minimal TradingEngine development platform defined in `infra/`.

The deployment creates a dedicated development resource group containing a low-cost Linux App Service Plan and a Web App that hosts the existing `TradingEngine.Api` shell (`/health` and `/version` only). No database, Key Vault, Application Insights, authentication or trading infrastructure is provisioned.

## Prerequisites

- Azure CLI 2.x with the Bicep CLI (`az bicep install` if `az bicep version` fails).
- An Azure account with permission to create resource groups and App Service resources in the target subscription.
- .NET 10 SDK for publishing the API.

## Cost and approval

The default SKU is **B1** (Linux Basic, 1 core, 1.75 GB RAM), the lowest tier that supports a stable always-on development deployment. Expected cost is roughly **$13 USD/month** at list prices; verify the current regional price in the Azure pricing calculator before deploying.

Per issue #34, obtain Jason's explicit approval of the selected SKU and expected cost immediately before provisioning.

## Build and lint the Bicep

```powershell
az bicep build --file infra/main.bicep
az bicep lint --file infra/main.bicep
```

## Preview changes with what-if

Run a subscription-level what-if against the dev parameters. This creates no resources.

```powershell
az deployment sub what-if `
  --location uksouth `
  --template-file infra/main.bicep `
  --parameters infra/environments/dev.bicepparam
```

## Deploy

```powershell
az deployment sub create `
  --location uksouth `
  --template-file infra/main.bicep `
  --parameters infra/environments/dev.bicepparam
```

The deployment outputs the resource group name, Web App name, default hostname and Managed Identity principal ID. Capture them for the publish step:

```powershell
$outputs = az deployment sub show `
  --name main `
  --query properties.outputs -o json | ConvertFrom-Json
$webAppName = $outputs.webAppName.value
$resourceGroupName = $outputs.resourceGroupName.value
$hostName = $outputs.defaultHostName.value
```

## Publish the API

Publish `TradingEngine.Api` and deploy the package to the Web App:

```powershell
dotnet publish src/TradingEngine.Api/TradingEngine.Api.csproj `
  --configuration Release `
  --output ./artifacts/api

Compress-Archive -Path ./artifacts/api/* -DestinationPath ./artifacts/api.zip -Force

az webapp deploy `
  --resource-group $resourceGroupName `
  --name $webAppName `
  --src-path ./artifacts/api.zip `
  --type zip
```

## Verify the endpoints

Both endpoints are anonymous and safe to call from outside Azure:

```powershell
Invoke-RestMethod "https://$hostName/health"
Invoke-RestMethod "https://$hostName/version"
```

`/health` should report a healthy status and `/version` should return the deployed application version. Record the version value against the deployment.

## Teardown

Delete only the generated development resource group. This removes the App Service Plan, Web App and Managed Identity created by this deployment and nothing else.

```powershell
az group delete --name $resourceGroupName --yes --no-wait
```

## Notes

- The Web App name is generated with `uniqueString` so it is globally unique without committing subscription IDs or live identifiers.
- The Web App runs the `DOTNETCORE|10.0` Linux runtime, matching the repository's .NET 10 target.
- HTTPS only, minimum TLS 1.2, FTPS disabled and `/health` configured as the App Service health-check path.
- A system-assigned Managed Identity is enabled for future use (for example Azure SQL access in a later story); nothing consumes it yet.
