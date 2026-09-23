# Azure SQL operations runbook

This runbook covers the one-time bootstrap and the recurring operations for the development Azure SQL database provisioned under issue #35. It is portal-first: each step names the portal blade to use, with PowerShell/Azure CLI equivalents afterwards. All commands use placeholders; never commit live values.

**Order of operations:** cost approval → merge/deploy infrastructure → contained-user bootstrap → migration identity setup → run the migration workflow → smoke test → remove temporary firewall rules → cost monitoring.

## 1. Cost approval gate

Issue #35 requires Jason's explicit cost approval **immediately before** the deployment that provisions SQL. Before approving, review:

- The pull-request what-if summary (the merge preview shows the SQL server, database and `ConnectionStrings__TradingEngine` app setting being created).
- The free-offer settings in `infra/modules/sql-database.bicep`: `useFreeLimit: true`, `freeLimitExhaustionBehavior: 'AutoPause'` — the database pauses at the monthly free limit and can never bill overage.
- Free-offer eligibility for `ukwest` on the target subscription (up to 10 free-offer General Purpose databases per subscription) and `Microsoft.Sql` resource provider registration:

```powershell
az provider show --namespace Microsoft.Sql --query registrationState -o tsv
az provider register --namespace Microsoft.Sql   # only if not registered
```

## 2. Deploy infrastructure

Merging the issue-35 pull request triggers `Deploy development infrastructure`, which creates the logical server, the free-offer database and the `ConnectionStrings__TradingEngine` app setting on the Web App. The Entra administrator values come from the `development` environment secrets (`AZURE_SQL_ENTRA_ADMIN_*`); they are never committed.

Capture the deployment outputs (server FQDN, database name, Web App managed identity principal ID) from the workflow summary, or:

```powershell
$outputs = az deployment sub show `
  --name <deployment-name> `
  --subscription <subscription-id> `
  --query properties.outputs -o json | ConvertFrom-Json
```

## 3. Contained-user bootstrap (portal-first)

Bicep cannot create contained database users — that is a data-plane operation performed once by the Entra SQL administrator.

1. **Inspect identities.** In the portal, open the SQL server → **Microsoft Entra ID** blade to confirm the configured administrator. Open the Web App → **Identity** → **System assigned** to confirm the managed identity is on; its object (principal) ID matches the `managedIdentityPrincipalId` deployment output. The Web App's system-assigned identity has an Entra principal (object) ID and an application (client) ID. When the principal name is unique, use the Web App name (`app-tradingengine-dev-<suffix>`) as the SQL contained user name.
2. **Allow your operator IP.** The server allows Azure services only. Portal: SQL server → **Networking** → add your client IP. CLI equivalent (remove it afterwards — see step 7):

```powershell
az sql server firewall-rule create `
  --resource-group rg-tradingengine-dev `
  --server <sql-server-name> `
  --name operator-bootstrap `
  --start-ip-address <operator-ip> --end-ip-address <operator-ip>
```

3. **Connect as the Entra SQL administrator.** Portal: database → **Query editor** → sign in with Microsoft Entra. Or Azure Data Studio / `sqlcmd -S <fqdn> -d <database> --authentication-method ActiveDirectoryDefault`.
4. **Create the Web App contained user** with data-plane roles only:

```sql
CREATE USER [app-tradingengine-dev-<suffix>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [app-tradingengine-dev-<suffix>];
ALTER ROLE db_datawriter ADD MEMBER [app-tradingengine-dev-<suffix>];
```

If an alias is needed, create it with `FROM EXTERNAL PROVIDER WITH OBJECT_ID = '<web-app-principal-id>'` and grant the roles to that alias. In the current development database, the verified alias ends in `-runtime`. For application principals, the GUID obtained with `CAST(sid AS uniqueidentifier)` can be the application (client) ID rather than the principal (object) ID; compare it with the Enterprise application's Application ID. Do **not** grant `db_owner` or `db_ddladmin` to the Web App identity — the runtime must not change schema.

5. **Create the migration identity contained user** (after step 4 creates the Entra application):

```sql
CREATE USER [tradingengine-github-migration-development] FROM EXTERNAL PROVIDER;
ALTER ROLE db_ddladmin ADD MEMBER [tradingengine-github-migration-development];
ALTER ROLE db_datareader ADD MEMBER [tradingengine-github-migration-development];
ALTER ROLE db_datawriter ADD MEMBER [tradingengine-github-migration-development];
```

`db_ddladmin` + `db_datareader` + `db_datawriter` is the minimum workable set for EF Core migrations: DDL for schema changes, write for the `__EFMigrationsHistory` insert, read for history and metadata queries. Do not grant `db_owner`.

## 4. Migration identity setup (one-time)

Migrations run through `.github/workflows/migrate-development-database.yml`, which is `workflow_dispatch`-only, restricted to `main`, and bound to a protected GitHub environment. It builds an EF Core migration bundle (never committed) and runs it as a dedicated Entra service principal over OIDC — never at application startup, never on push, never as part of app deployment.

Azure resource roles and SQL permissions are deliberately distinct: the identity needs ARM access only to manage a temporary firewall rule, and SQL permissions only inside the database.

### 4.1 Entra application and federated credential

Portal: **Microsoft Entra ID → App registrations → New registration** named `tradingengine-github-migration-development`, then **Certificates & secrets → Federated credentials → Add credential** → *Other issuer*. Enter these values explicitly: the GitHub Actions preset may generate a name-only subject without the immutable repository IDs:

- **Issuer**: `https://token.actions.githubusercontent.com`
- **Audience**: `api://AzureADTokenExchange`
- **Subject**: `repo:carndog@7319736/TradingEngine@1349997095:environment:development-database-migration`

CLI equivalent:

```powershell
$migrationAppId = az ad app create `
  --display-name 'tradingengine-github-migration-development' `
  --query appId -o tsv
$migrationSpObjectId = az ad sp create --id $migrationAppId --query id -o tsv

$credPath = Join-Path $env:TEMP 'migration-federated-credential.json'
@{
    name = 'github-development-database-migration'
    issuer = 'https://token.actions.githubusercontent.com'
    subject = 'repo:carndog@7319736/TradingEngine@1349997095:environment:development-database-migration'
    audiences = @('api://AzureADTokenExchange')
} | ConvertTo-Json -Depth 3 | Set-Content -Path $credPath -Encoding ascii

az ad app federated-credential create --id $migrationAppId --parameters $credPath
Remove-Item $credPath
```

### 4.2 ARM role: firewall-rule management only

The workflow lists SQL servers in `rg-tradingengine-dev`, verifies the named database (`az sql db show`), and adds/removes a temporary firewall rule for the runner IP. Make the custom role assignable at the development resource group and assign it to the migration principal at that resource group scope, with only these five SQL management actions:

```powershell
$resourceGroupId = az group show `
  --name rg-tradingengine-dev `
  --query id -o tsv

$rolePath = Join-Path $env:TEMP 'migration-firewall-role.json'
@{
    Name = 'TradingEngine migration firewall manager'
    Description = 'Create and delete firewall rules on the development SQL server for migration runs.'
    AssignableScopes = @($resourceGroupId)
    Actions = @(
        'Microsoft.Sql/servers/read'
        'Microsoft.Sql/servers/databases/read'
        'Microsoft.Sql/servers/firewallRules/read'
        'Microsoft.Sql/servers/firewallRules/write'
        'Microsoft.Sql/servers/firewallRules/delete'
    )
    NotActions = @()
} | ConvertTo-Json -Depth 3 | Set-Content -Path $rolePath -Encoding ascii

az role definition create --role-definition $rolePath
Remove-Item $rolePath

az role assignment create `
  --assignee-object-id $migrationSpObjectId `
  --assignee-principal-type ServicePrincipal `
  --role 'TradingEngine migration firewall manager' `
  --scope $resourceGroupId
```

### 4.3 GitHub environment

**Settings → Environments → New environment** named `development-database-migration`:

- **Required reviewers**: add at least one reviewer — every migration run is human-approved before an Azure token is issued.
- **Deployment branches and tags**: restrict to `main` only.
- Secrets: `AZURE_MIGRATION_CLIENT_ID` (the app/client ID above), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.

Separately, the `development` and `development-infrastructure-preview` environments each need an `AZURE_DATABASE_PROBE_KEY` secret — a generated shared key that Bicep publishes as the `Diagnostics__DatabaseProbeKey` app setting. It gates `/health/database` so anonymous internet requests cannot wake the serverless database or consume its free allowance; the smoke test in step 6 supplies it as the `X-Database-Probe-Key` header.

### 4.4 Contained SQL user

Created in step 3.5 above. The contained user name is the service principal display name, `tradingengine-github-migration-development`.

## 5. Run the migration

**Actions → Migrate development database → Run workflow** from `main`. The run resolves the server and database names, adds a temporary firewall rule for the runner, restores the infrastructure project and builds `efbundle` from the #44 migration, then applies it with `Authentication=Active Directory Default` (the OIDC workload identity). The cleanup step attempts to remove the firewall rule even on failure; verify it is absent as described in step 7.

## 6. Smoke test and verification

After provisioning, bootstrap and migration:

```powershell
# Free-offer properties and Entra-only authentication
az sql db show --name <database-name> --server <sql-server-name> `
  --resource-group rg-tradingengine-dev `
  --query "{sku:sku.name, useFreeLimit:properties.useFreeLimit, exhaustion:properties.freeLimitExhaustionBehavior, autoPause:properties.autoPauseDelay}"
az sql server show --name <sql-server-name> --resource-group rg-tradingengine-dev `
  --query "{entraOnly:properties.administrators.azureADOnlyAuthentication, tls:properties.minimalTlsVersion}"

# Applied migration (connect as the Entra admin)
# SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId;

# Managed-identity connectivity — the deployed API readiness check.
# /health/database is not anonymous: it requires the X-Database-Probe-Key header
# matching the Diagnostics__DatabaseProbeKey app setting (the AZURE_DATABASE_PROBE_KEY
# environment secret). Without the key it returns 404; /health stays Healthy regardless.
Invoke-RestMethod "https://<web-app-hostname>/health/database" `
  -Headers @{ 'X-Database-Probe-Key' = '<probe-key>' }
# Expect {"status":"Healthy"}.
```

**Negative check:** connect with an identity that has no contained database user (for example a second test Entra account or a service principal without a `CREATE USER` entry). The connection must fail with a login/permission error — Entra-only authentication plus contained users means no network path alone grants access.

## 7. Remove temporary firewall rules

Delete the operator rule from step 3 (portal: SQL server → **Networking**, or CLI). The workflow removes its own runner rule automatically; verify none remain:

```powershell
az sql server firewall-rule list `
  --resource-group rg-tradingengine-dev --server <sql-server-name> -o table
# Only AllowAllWindowsAzureIps (0.0.0.0) should remain.
az sql server firewall-rule delete `
  --resource-group rg-tradingengine-dev --server <sql-server-name> --name operator-bootstrap
```

## 8. Database-only teardown

Removes SQL while preserving the App Service Plan and Web App. Note the Web App keeps its `ConnectionStrings__TradingEngine` setting and `/health/database` will report Unhealthy until the next infrastructure deployment reconciles it.

```powershell
az sql db delete --name <database-name> --server <sql-server-name> `
  --resource-group rg-tradingengine-dev --yes
az sql server delete --name <sql-server-name> `
  --resource-group rg-tradingengine-dev --yes
```

Do **not** delete `rg-tradingengine-dev` — it contains the App Service. Incremental Bicep deployments never delete resources, so disabling `provisionAzureSql` alone does not remove SQL.

## 9. Cost monitoring

- Portal: database → **Metrics** → the **Free amount remaining** metric shows monthly vCore-seconds left. Create a free alert rule at 10,000 vCore-seconds (10%) remaining.
- The 100,000 vCore-second and 32 GB allowances reset each calendar month; `AutoPause` means exhaustion pauses the database rather than billing.
- Disconnect query tools when finished — open connections prevent auto-pause and consume the allowance.
- Keep the subscription budget and cost alerts from the App Service deployment active; the SQL free offer should produce zero SQL line items.
