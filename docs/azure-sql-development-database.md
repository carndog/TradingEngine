# Azure SQL development database

This document describes the Azure SQL infrastructure for the TradingEngine development environment: the logical server, the free-offer serverless database, the managed-identity authentication model and the deployment gate that issue #35 deliberately enabled.

**Current state: SQL provisioning is enabled for the development environment.** `provisionAzureSql` is `true` in `infra/environments/dev.bicepparam`, so the next infrastructure deployment creates the logical server and free-offer database and sets the `ConnectionStrings__TradingEngine` app setting on the Web App. Provisioning still requires Jason's explicit cost approval immediately before deployment; see [Deployment gate](#deployment-gate) and the [operations runbook](azure-sql-operations-runbook.md).

## Logical server versus database

Azure SQL separates the container from the data:

- **Logical server** (`Microsoft.Sql/servers`, `infra/modules/sql-server.bicep`) — a management and security boundary, not a running VM. It owns the DNS name (`<server>.database.windows.net`), the firewall rules, the TLS floor and the Microsoft Entra administrator. It has no compute cost of its own.
- **Database** (`Microsoft.Sql/servers/databases`, `infra/modules/sql-database.bicep`) — the billed resource that stores the TradingEngine configuration schema managed by EF Core (see [Current configuration persistence](current-configuration-persistence.md)).

The server name is generated deterministically with the same convention as the Web App: `sql-<namingPrefix>-<environment>-<uniqueString(subscriptionId, resourceGroupName)>`, so it is globally unique without committing live identifiers. The database name is the readable `sqldb-<namingPrefix>-<environment>`.

## Authentication paths

Three distinct identities are involved. None uses a password.

- **GitHub OIDC infrastructure identity** (`tradingengine-github-infrastructure-development`) — authenticates the workflow to Azure Resource Manager for what-if and deployments. It operates at the ARM control plane only; it is not a SQL principal and cannot read data.
- **Web App system-assigned managed identity** — the runtime identity the API will use to connect to the database. Bicep exposes its principal ID as the `managedIdentityPrincipalId` deployment output. It becomes a SQL principal only through the contained-user bootstrap below.
- **Microsoft Entra SQL administrator** — a user, group or application configured on the logical server via the `sqlEntraAdminLogin`, `sqlEntraAdminObjectId` and `sqlEntraAdminPrincipalType` parameters. The server uses **Microsoft Entra-only authentication**: there is no SQL administrator login or password, and SQL authentication is disabled entirely. The administrator performs the one-time bootstrap and any future schema migrations.

The tenant ID is resolved at deploy time with `tenant().tenantId`; it is never committed.

## Free-tier cost controls

The database targets the Azure SQL Database **free offer** (General Purpose serverless), which provides 100,000 vCore-seconds of compute, 32 GB of data storage and 32 GB of backup storage per month at no charge:

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

**Eligibility caveat:** the free offer allows up to 10 General Purpose databases per subscription and is not available in every region or subscription type. Region and subscription eligibility for `ukwest` on the target subscription must be confirmed immediately before the deployment that provisions SQL. The `Microsoft.Sql` resource provider must also be registered on the subscription first.

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

## What-if preview

With `provisionAzureSql = true` in `dev.bicepparam`, the merge preview is the SQL-enabled preview — what merging actually deploys. The Entra administrator values are supplied at the command line and are never committed:

```powershell
az deployment sub what-if `
  --name tradingengine-dev-whatif `
  --subscription $subscription `
  --location ukwest `
  --template-file infra/main.bicep `
  --parameters infra/environments/dev.bicepparam `
  --parameters sqlEntraAdminLogin='<entra-admin-display-name>' `
  --parameters sqlEntraAdminObjectId='<entra-admin-object-id>' `
  --parameters sqlEntraAdminPrincipalType='User'
```

The pull-request workflow runs this preview automatically against the `development-infrastructure-preview` environment; see [GitHub Actions infrastructure deployment](github-actions-infrastructure-deployment.md).

## Deployment gate

Issue #35 deliberately changed the gate: `provisionAzureSql` is now `true` in `infra/environments/dev.bicepparam`, so merging the issue-35 change provisions Azure SQL on the next infrastructure deployment. The remaining controls are:

- `infra/main.bicep` still declares `param provisionAzureSql bool = false`, so the template default stays safe for any other parameter file or ad-hoc deployment.
- The Entra administrator parameters are never committed. Both the `whatif` job (`development-infrastructure-preview` environment) and the `deploy` job (`development` environment) fail fast when the `AZURE_SQL_ENTRA_ADMIN_*` secrets are absent, and the `deploy` job supplies them to `az deployment sub create` from the `development` environment secrets.
- The `deploy` job runs only on pushes to `main` or a manual dispatch from `main`, against the protected `development` environment.

**Known limitation:** the template cannot itself reject an enabled-but-incomplete configuration at compile time. Bicep `assert` declarations require the experimental Assertions feature in the installed Bicep version, so they are not used. Instead, the workflow fails fast when the `AZURE_SQL_ENTRA_ADMIN_*` secrets are absent, the `sql-server.bicep` module constrains `entraAdminPrincipalType` to `User`, `Group` or `Application` at deploy time, and Azure rejects an empty administrator login or object ID during deployment validation.

## Runtime connection string

When `provisionAzureSql` is `true`, `main.bicep` composes the passwordless connection string and the `app-service.bicep` module publishes it as the `ConnectionStrings__TradingEngine` app setting on the Web App:

```
Server=tcp:<server>.database.windows.net,1433;Database=sqldb-tradingengine-dev;Authentication=Active Directory Default;Encrypt=True;
```

`Authentication=Active Directory Default` uses the `DefaultAzureCredential` chain in Microsoft.Data.SqlClient: the Web App's system-assigned managed identity in Azure, and developer credentials (Azure CLI, Visual Studio) locally. The pinned Microsoft.Data.SqlClient 6.1.6 supports this mode natively — the extension-package split only applies from version 7.0. The setting is non-secret (no password) and Bicep is its source of truth; the what-if shows it as a `Microsoft.Web/sites/config` change on the Web App. When `provisionAzureSql` is `false` the app setting is not emitted at all.

## Contained-user bootstrap (after provisioning)

Bicep cannot create contained database users — that is a data-plane operation. After the deployment provisions the server, an operator performs the bootstrap once, as the configured Entra SQL administrator:

```sql
CREATE USER [app-tradingengine-dev-<suffix>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [app-tradingengine-dev-<suffix>];
ALTER ROLE db_datawriter ADD MEMBER [app-tradingengine-dev-<suffix>];
```

Grant **only** `db_datareader` and `db_datawriter` to the runtime identity. Do not grant `db_owner` or `db_ddladmin` — the Web App must not be able to change schema. EF Core migrations run separately under an explicitly authorised migration identity (`db_ddladmin` + `db_datareader` + `db_datawriter`), never under the Web App identity and never at application startup.

The full portal-first procedure — including the temporary operator firewall rule, the migration identity's Entra application, federated credential, GitHub environment and contained user — is in the [Azure SQL operations runbook](azure-sql-operations-runbook.md).

## Verification after provisioning

```powershell
az sql server show --name $sqlServerName --resource-group rg-tradingengine-dev -o table
az sql db show --name sqldb-tradingengine-dev --server $sqlServerName --resource-group rg-tradingengine-dev -o table
```

Confirm `administrators.azureADOnlyAuthentication` is `true`, `minimalTlsVersion` is `1.2`, the SKU is `GP_S_Gen5`, `useFreeLimit` is `true` and `freeLimitExhaustionBehavior` is `AutoPause`. The full smoke test — `__EFMigrationsHistory`, the `/health/database` readiness check through the Web App managed identity, and a failed connection by an identity without a contained user — is in the [operations runbook](azure-sql-operations-runbook.md).

## Rollback and teardown

**Incremental deployments do not delete.** Removing or disabling the SQL modules in Bicep does *not* delete an already-deployed server or database — incremental mode only adds and updates. To remove SQL after it has been provisioned, delete the resources explicitly.

**SQL-only teardown** (preserves the App Service and everything else in the resource group):

```powershell
az sql db delete --name sqldb-tradingengine-dev --server $sqlServerName --resource-group rg-tradingengine-dev --yes
az sql server delete --name $sqlServerName --resource-group rg-tradingengine-dev --yes
```

Do **not** delete the resource group to remove SQL — `rg-tradingengine-dev` also contains the App Service Plan and Web App.

**Full platform teardown** (only when deliberately removing the entire development platform, including the App Service): delete the resource group as described in [Azure development deployment](azure-development-deployment.md#teardown).
