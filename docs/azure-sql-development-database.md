# Azure SQL development database

This document describes the Azure SQL infrastructure defined for the TradingEngine development environment: the logical server, the free-offer serverless database, the managed-identity authentication model and the safety gate that keeps SQL disabled until issue #35 deliberately enables it.

**Current state: the SQL infrastructure is defined in Bicep but disabled.** No Azure SQL resources exist. Nothing in this repository creates them; see [Deployment safety gate](#deployment-safety-gate).

## Logical server versus database

Azure SQL separates the container from the data:

- **Logical server** (`Microsoft.Sql/servers`, `infra/modules/sql-server.bicep`) — a management and security boundary, not a running VM. It owns the DNS name (`<server>.database.windows.net`), the firewall rules, the TLS floor and the Microsoft Entra administrator. It has no compute cost of its own.
- **Database** (`Microsoft.Sql/servers/databases`, `infra/modules/sql-database.bicep`) — the billed resource that stores the TradingEngine configuration schema managed by EF Core (see [Current configuration persistence](current-configuration-persistence.md)).

The server name is generated deterministically with the same convention as the Web App: `sql-<namingPrefix>-<environment>-<uniqueString(subscriptionId, resourceGroupName)>`, so it is globally unique without committing live identifiers. The database name is the readable `sqldb-<namingPrefix>-<environment>`.

## Authentication paths

Three distinct identities are involved. None uses a password.

- **GitHub OIDC infrastructure identity** (`tradingengine-github-infrastructure-development`) — authenticates the workflow to Azure Resource Manager for what-if and deployments. It operates at the ARM control plane only; it is not a SQL principal and cannot read data.
- **Web App system-assigned managed identity** — the runtime identity the API will use to connect to the database. Bicep exposes its principal ID as the `managedIdentityPrincipalId` deployment output. It becomes a SQL principal only through the contained-user bootstrap below.
- **Microsoft Entra SQL administrator** — a user, group or service principal configured on the logical server via the `sqlEntraAdminLogin`, `sqlEntraAdminObjectId` and `sqlEntraAdminPrincipalType` parameters. The server uses **Microsoft Entra-only authentication**: there is no SQL administrator login or password, and SQL authentication is disabled entirely. The administrator performs the one-time bootstrap and any future schema migrations.

The tenant ID is resolved at deploy time with `tenant().tenantId`; it is never committed.

## Free-tier cost controls

The database targets the Azure SQL Database **free offer** (General Purpose serverless), which provides 100,000 vCore-seconds of compute and 32 GB of storage per month at no charge:

| Setting | Value | Reason |
| --- | --- | --- |
| SKU | `GP_S_Gen5` (`GeneralPurpose`, `Gen5`, capacity 1) | Serverless General Purpose, the only family eligible for the free offer |
| `useFreeLimit` | `true` | Opts the database into the monthly free limits |
| `freeLimitExhaustionBehavior` | `AutoPause` | Pauses the database when free limits are exhausted — never bills overage |
| `maxSizeBytes` | 34359738368 (32 GB) | The free-offer storage ceiling |
| `autoPauseDelay` | 60 minutes | Shortest supported idle period before auto-pause |
| `minCapacity` / `capacity` | 0.5 / 1 vCore | Lowest sensible range for a development configuration database |
| `requestedBackupStorageRedundancy` | `Local` | Cheapest redundancy; no geo-replica cost |

`useFreeLimit` and `freeLimitExhaustionBehavior` are hard-coded in `sql-database.bicep` rather than parameterised, so the template cannot be coaxed into a bill-over-usage configuration by a parameter override.

**Eligibility caveat:** the free offer is limited per subscription and is not available in every region or subscription type. Region and subscription eligibility for `ukwest` on the target subscription must be confirmed immediately before issue #35 enables the deployment. The `Microsoft.Sql` resource provider must also be registered on the subscription first — it is currently not registered, which is expected while SQL is disabled.

## Development network access

The logical server sets `publicNetworkAccess: 'Enabled'` and includes the `AllowAllWindowsAzureIps` firewall rule (`0.0.0.0`–`0.0.0.0`), which permits connections from Azure services including the Web App.

**This is intentionally broad at the network layer.** `0.0.0.0` does not open the server to the internet — it only allows traffic originating from Azure backbone addresses — but it does mean any Azure service can attempt a connection. The real boundary is authentication: Microsoft Entra-only authentication plus database-level permissions remain mandatory, so a network path alone grants nothing.

Private endpoints and VNet integration are deliberately out of scope for development.

For a one-off local connection (for example the bootstrap below), a temporary firewall rule for the operator's current IP can be added with `az sql server firewall-rule create`, used, and then **must be removed afterwards**. Personal IP addresses are never committed to this repository.

## Build and lint

```powershell
az bicep build --file infra/main.bicep --stdout | Out-Null
az bicep lint --file infra/main.bicep
```

## What-if previews

Two previews exist, and they answer different questions.

**Safe merge preview (SQL disabled)** — what merging actually deploys today:

```powershell
az deployment sub what-if `
  --name tradingengine-dev-whatif `
  --subscription $subscription `
  --location ukwest `
  --template-file infra/main.bicep `
  --parameters infra/environments/dev.bicepparam
```

**Planned SQL preview (SQL enabled)** — what issue #35 would create. The Entra administrator values are supplied at the command line and are never committed:

```powershell
az deployment sub what-if `
  --name tradingengine-dev-whatif-sql `
  --subscription $subscription `
  --location ukwest `
  --template-file infra/main.bicep `
  --parameters infra/environments/dev.bicepparam `
  --parameters provisionAzureSql=true `
  --parameters sqlEntraAdminLogin='<entra-admin-display-name>' `
  --parameters sqlEntraAdminObjectId='<entra-admin-object-id>' `
  --parameters sqlEntraAdminPrincipalType='User'
```

The pull-request workflow runs both automatically; see [GitHub Actions infrastructure deployment](github-actions-infrastructure-deployment.md).

## Deployment safety gate

`provisionAzureSql` is `false` in three independent places, so merging this story cannot create Azure SQL:

- `infra/main.bicep` declares `param provisionAzureSql bool = false`, and both SQL modules are wrapped in `if (provisionAzureSql)` — with `false` the resources are not even part of the compiled deployment.
- `infra/environments/dev.bicepparam` sets `param provisionAzureSql = false` explicitly.
- The `deploy` job in the infrastructure workflow passes `--parameters provisionAzureSql=false` on the command line, which overrides the parameter file even if it is later edited.

Issue #35 will deliberately flip the gate after Jason approves the cost. Until then, no path — merge, manual dispatch or parameter-file edit alone — provisions SQL.

**Known limitation:** the template cannot itself reject an enabled-but-incomplete configuration at compile time. Bicep `assert` declarations require the experimental Assertions feature in the installed Bicep version, so they are not used. Instead, the workflow fails fast when the `AZURE_SQL_ENTRA_ADMIN_*` preview variables are absent, the `sql-server.bicep` module constrains `entraAdminPrincipalType` to `User`, `Group` or `ServicePrincipal` at deploy time, and Azure rejects an empty administrator login or object ID during deployment validation.

## Future deployment (issue #35)

When approved, enabling SQL is a deliberate change: set `provisionAzureSql = true` in `dev.bicepparam`, remove the `--parameters provisionAzureSql=false` override from the deploy step, and supply the Entra administrator values through GitHub environment configuration — never in source control. Confirm free-offer eligibility and `Microsoft.Sql` provider registration first.

## Contained-user bootstrap (after provisioning)

Bicep cannot create a contained database user for the managed identity — that is a data-plane operation. After issue #35 provisions the server, an operator performs this once:

1. Connect to `sqldb-tradingengine-dev` as the configured Entra SQL administrator (for example with `sqlcmd` or Azure Data Studio using Microsoft Entra authentication). A temporary firewall rule for the operator IP may be needed and must be removed afterwards.
2. Create a contained user mapped to the Web App managed identity:

```sql
CREATE USER [app-tradingengine-dev-<suffix>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [app-tradingengine-dev-<suffix>];
ALTER ROLE db_datawriter ADD MEMBER [app-tradingengine-dev-<suffix>];
```

3. Grant **only** `db_datareader` and `db_datawriter` to the runtime identity. Do not grant `db_owner` or `db_ddladmin` — the Web App must not be able to change schema.
4. Run EF Core migrations separately under an explicitly authorised migration/admin identity, not under the Web App identity.

The future passwordless connection-string shape is:

```
Server=tcp:<server>.database.windows.net,1433;Database=sqldb-tradingengine-dev;Authentication=Active Directory Default;Encrypt=True;
```

The application setting is **not** configured by this story; that belongs to issue #35.

## Verification after provisioning

```powershell
az sql server show --name $sqlServerName --resource-group rg-tradingengine-dev -o table
az sql db show --name sqldb-tradingengine-dev --server $sqlServerName --resource-group rg-tradingengine-dev -o table
```

Confirm `administrators.azureADOnlyAuthentication` is `true`, `minimalTlsVersion` is `1.2`, the SKU is `GP_S_Gen5`, `useFreeLimit` is `true` and `freeLimitExhaustionBehavior` is `AutoPause`.

## Rollback and teardown

**Incremental deployments do not delete.** Removing or disabling the SQL modules in Bicep does *not* delete an already-deployed server or database — incremental mode only adds and updates. To remove SQL after it has been provisioned, delete the resources explicitly.

**SQL-only teardown** (preserves the App Service and everything else in the resource group):

```powershell
az sql db delete --name sqldb-tradingengine-dev --server $sqlServerName --resource-group rg-tradingengine-dev --yes
az sql server delete --name $sqlServerName --resource-group rg-tradingengine-dev --yes
```

Do **not** delete the resource group to remove SQL — `rg-tradingengine-dev` also contains the App Service Plan and Web App.

**Full platform teardown** (only when deliberately removing the entire development platform, including the App Service): delete the resource group as described in [Azure development deployment](azure-development-deployment.md#teardown).
