# Azure development deployment

This document describes how to build, validate, deploy and tear down the minimal TradingEngine development platform defined in `infra/`.

The deployment creates a dedicated development resource group containing a low-cost Linux App Service Plan and a Web App that hosts the existing `TradingEngine.Api` shell (`/health` and `/version` only). No Key Vault, Application Insights, authentication or trading infrastructure is provisioned.

Azure SQL infrastructure (a logical server and a free-offer serverless database) is enabled for the development environment: `provisionAzureSql` is `true` in `dev.bicepparam` since issue #35, so the next deployment creates the SQL resources and sets the `ConnectionStrings__TradingEngine` app setting on the Web App. The Entra administrator parameters are supplied at deploy time and never committed. See [Azure SQL development database](azure-sql-development-database.md) and the [operations runbook](azure-sql-operations-runbook.md).

## Prerequisites

- Azure CLI **2.48.1 or later**. SCM basic authentication is disabled on the Web App, so `az webapp deploy` must authenticate with Microsoft Entra; earlier CLI versions can fall back to basic credentials and fail.
- Bicep CLI (`az bicep install` if `az bicep version` fails).
- An Azure account with permission to create resource groups and App Service resources in the target subscription.
- .NET 10 SDK for publishing the API.

## Select and verify the subscription

Choose the intended subscription explicitly and verify it before running any deployment command. Substitute your own subscription name or ID; do not commit a real subscription ID to this repository.

```powershell
$subscription = '<subscription-name-or-id>'

az login
az account set --subscription $subscription
az account show --subscription $subscription --output table
```

Confirm the displayed subscription is the intended development subscription before continuing.

## Cost and approval

The development App Service Plan uses the paid **B1 Linux** tier and incurs charges while it exists, including when the application is idle.

Before provisioning:

- Verify the current price for B1 in the configured Azure region.
- Confirm that subscription cost budgets and alerts are configured.
- Confirm that the expected ongoing cost is acceptable.

Deleting the generated development resource group removes the App Service Plan and stops its ongoing compute charge.

## Verify the .NET Linux runtime

Confirm that App Service offers the .NET 10 Linux runtime before deploying:

```powershell
$runtimes = az webapp list-runtimes --os-type linux | ConvertFrom-Json

$runtimeConfigs = @($runtimes | ForEach-Object {
    if ($_ -is [string]) { $_ } else { $_.config }
})

if ('DOTNETCORE|10.0' -notin $runtimeConfigs) {
    throw 'DOTNETCORE|10.0 is not available.'
}
```

Current Azure CLI versions return structured objects from `list-runtimes`; older supported versions may return strings. The check above handles both formats.

The template sets `linuxFxVersion` to `DOTNETCORE|10.0`. If the runtime is not listed, stop and raise it with Jason rather than silently targeting another .NET version.

## Build and lint the Bicep

Use `--stdout` so the generated ARM JSON is not written into `infra/`. If a file is needed, output to the gitignored `artifacts/` directory instead.

```powershell
az bicep build --file infra/main.bicep --stdout | Out-Null
az bicep lint --file infra/main.bicep
```

## Preview changes with what-if

Run a subscription-level what-if against the dev parameters. This creates no resources. Use an explicit deployment name so `what-if`, `create` and `show` all refer to the same deployment.

```powershell
$deploymentName = 'tradingengine-dev'

az deployment sub what-if `
  --name $deploymentName `
  --subscription $subscription `
  --location ukwest `
  --template-file infra/main.bicep `
  --parameters infra/environments/dev.bicepparam
```

## Deploy

```powershell
az deployment sub create `
  --name $deploymentName `
  --subscription $subscription `
  --location ukwest `
  --template-file infra/main.bicep `
  --parameters infra/environments/dev.bicepparam `
  --parameters sqlEntraAdminLogin='<entra-admin-display-name>' `
  --parameters sqlEntraAdminObjectId='<entra-admin-object-id>' `
  --parameters sqlEntraAdminPrincipalType='User'
```

The Entra administrator parameters are required because `provisionAzureSql` is `true`; supply them at the command line and never commit them.

The deployment outputs the resource group name, Web App name, default hostname and Managed Identity principal ID. Capture them for the publish step:

```powershell
$outputs = az deployment sub show `
  --name $deploymentName `
  --subscription $subscription `
  --query properties.outputs -o json | ConvertFrom-Json
$webAppName = $outputs.webAppName.value
$resourceGroupName = $outputs.resourceGroupName.value
$hostName = $outputs.defaultHostName.value
```

## Publish the API

Publish `TradingEngine.Api`, stamping the build with the current commit so `/version` identifies the deployed commit, then deploy the package to the Web App:

```powershell
$commitSha = git rev-parse HEAD

dotnet publish src/TradingEngine.Api/TradingEngine.Api.csproj `
  --configuration Release `
  -p:SourceRevisionId=$commitSha `
  --output ./artifacts/api

Compress-Archive -Path ./artifacts/api/* -DestinationPath ./artifacts/api.zip -Force

az webapp deploy `
  --resource-group $resourceGroupName `
  --name $webAppName `
  --subscription $subscription `
  --src-path ./artifacts/api.zip `
  --type zip
```

`az webapp deploy` authenticates with Microsoft Entra because SCM basic authentication is disabled on the Web App.

## Verify the endpoints

Both endpoints are anonymous and safe to call from outside Azure:

```powershell
Invoke-RestMethod "https://$hostName/health"
Invoke-RestMethod "https://$hostName/version"
```

`/health` should report a healthy status and `/version` should return the deployed application version. Record the version value against the deployment.

## Teardown

**Full platform teardown** — when deliberately removing the entire development platform, delete only the generated development resource group. This removes the App Service Plan, Web App, Managed Identity and (if it has ever been provisioned) the Azure SQL server and database, and nothing else.

```powershell
az group delete --name $resourceGroupName --subscription $subscription --yes --no-wait
```

**SQL-only teardown** — to remove only the Azure SQL database and logical server while preserving the App Service, delete those two resources explicitly; see [Azure SQL development database](azure-sql-development-database.md#rollback-and-teardown). Do not delete the resource group for this purpose.

Note that an incremental Bicep deployment does not delete a resource merely because its module is removed or disabled — teardown of provisioned SQL resources is always an explicit `az sql` delete.

## Notes

- The Web App name is generated with `uniqueString` so it is globally unique without committing subscription IDs or live identifiers.
- The Web App runs the `DOTNETCORE|10.0` Linux runtime, matching the repository's .NET 10 target.
- HTTPS only, minimum TLS 1.2, FTPS disabled, `alwaysOn` enabled and `/health` configured as the App Service health-check path.
- FTP and SCM basic publishing credentials are disabled; deployments must use Microsoft Entra authentication.
- A system-assigned Managed Identity is enabled for Azure SQL access; its principal ID is exported as the `managedIdentityPrincipalId` deployment output. The contained-user bootstrap that maps it into the database is documented in the [operations runbook](azure-sql-operations-runbook.md) and runs once after the SQL deployment.
- The Web App's application-settings collection is owned by Bicep (`app-service.bicep` emits a `Microsoft.Web/sites/config` resource when SQL is enabled, which replaces the whole collection). It currently owns `ConnectionStrings__TradingEngine` — non-secret, `Authentication=Active Directory Default` uses the managed identity — and `Diagnostics__DatabaseProbeKey`, a shared key gating `/health/database`. The API exposes `/health` (anonymous platform probe, never touches SQL) and `/health/database` (explicit readiness check requiring the `X-Database-Probe-Key` header).
- The `appServicePlanFreeOfferExpirationTime` parameter preserves the subscription-assigned temporary App Service Plan free offer already present on the plan (`2026-10-15T17:51:00` for dev, matching the live value reported by what-if). Without it, a deployment would remove the expiry. Reassess the parameter after the offer expires.
