# GitHub Actions infrastructure deployment

The workflow `.github/workflows/deploy-infrastructure-development.yml` validates and deploys the subscription-scope Bicep in `infra/` to the development environment. It uses a dedicated GitHub-to-Azure OpenID Connect (OIDC) identity, `tradingengine-github-infrastructure-development`, that is separate from the application-deployment identity; no client secret, publish profile or other long-lived credential is stored anywhere.

Application build, test and deployment remain the responsibility of `.github/workflows/deploy-development.yml`. The infrastructure workflow never builds or deploys application binaries.

## Workflow behaviour

The workflow triggers only when `infra/**` or the workflow file itself changes:

- **Pull requests targeting `main`**: the `validate` job runs `az bicep build --stdout` and `az bicep lint` against `infra/main.bicep`. For pull requests originating from this repository, a `whatif` job then runs a single merge preview against the `development-infrastructure-preview` environment and writes the change list to the workflow summary. Since issue #35, `provisionAzureSql` is `true` in `dev.bicepparam`, so the preview shows the Azure SQL server, free-offer database and `ConnectionStrings__TradingEngine` app setting that merging will create; the Entra administrator values come from the preview environment's secrets.
- **Pushes to `main`** (including merged pull requests): `validate` runs, then `deploy` runs `az deployment sub create` against the `development` environment and records the commit, deployment name and Bicep outputs in the workflow summary. The deploy command supplies the Entra administrator parameters from the `development` environment secrets (`AZURE_SQL_ENTRA_ADMIN_*`) and fails fast if any is absent — the values are never committed. Merging an infrastructure change therefore provisions Azure SQL; issue #35 requires Jason's explicit cost approval immediately before that deployment.
- **Manual `workflow_dispatch`**: recovery option. The `deploy` job still requires `refs/heads/main`, so a manual run only deploys when started from `main`.

The `validate` job holds only `contents: read` and receives no Azure OIDC token or environment secrets, so it is safe on fork pull requests. The `whatif` job is additionally gated on `github.event.pull_request.head.repo.full_name == github.repository`, so fork pull requests never reach the `development-infrastructure-preview` environment or receive an OIDC token. The `deploy` job uses the `development` environment — restricted to `main` — plus a `deploy-infrastructure-development` concurrency group with `cancel-in-progress: false` so overlapping deployments queue rather than cancel halfway through.

Deployment names are unique per run (`tradingengine-dev-<run-id>` for deployments, `tradingengine-dev-whatif-<run-id>` for previews) so each run is identifiable in the subscription deployment history.

## Required GitHub environment configuration

The workflow uses two GitHub environments with separate secrets so that pull-request previews and real deployments are independently controlled. Jason must create and configure both manually; the workflow does not create environments.

### `development` (existing — real deployments)

Remains restricted to the `main` branch and is used only by the `deploy` job. Keep its existing entries untouched — the application workflow still needs them — and add the infrastructure entries.

Existing secrets (unchanged, used by the application workflow):

| Secret | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | Application (client) ID of the application-deployment identity |
| `AZURE_TENANT_ID` | Microsoft Entra tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Target development subscription |

Existing variables (unchanged, used by the application workflow):

| Variable | Value |
| --- | --- |
| `AZURE_WEBAPP_NAME` | Existing Web App name |
| `AZURE_WEBAPP_HOSTNAME` | Web App default hostname |

New entries to add for the infrastructure `deploy` job:

| Entry | Type | Value |
| --- | --- | --- |
| `AZURE_INFRA_CLIENT_ID` | Secret | Application (client) ID of `tradingengine-github-infrastructure-development` |
| `AZURE_DEPLOYMENT_LOCATION` | Variable | `ukwest` — the Azure region used as the subscription-scope deployment location |
| `AZURE_SQL_ENTRA_ADMIN_LOGIN` | Secret | Login/display name of the Microsoft Entra SQL administrator configured on the logical server |
| `AZURE_SQL_ENTRA_ADMIN_OBJECT_ID` | Secret | Object ID of the Microsoft Entra SQL administrator |
| `AZURE_SQL_ENTRA_ADMIN_PRINCIPAL_TYPE` | Secret | `User`, `Group` or `Application` |
| `AZURE_DATABASE_PROBE_KEY` | Secret | Generated shared key published as the `Diagnostics__DatabaseProbeKey` app setting; gates `/health/database` |
| `AZURE_ENTRA_AUTH_CLIENT_ID` | Secret | Application (client) ID of the single-tenant Easy Auth app registration |
| `AZURE_ENTRA_AUTH_ALLOWED_PRINCIPALS` | Secret | JSON array of owner Entra object IDs, e.g. `["<object-id>"]`; becomes the Easy Auth `allowedPrincipals.identities` allowlist |

The `AZURE_SQL_ENTRA_ADMIN_*` and `AZURE_DATABASE_PROBE_KEY` secrets are required because `provisionAzureSql` is `true` in `dev.bicepparam`, and the `AZURE_ENTRA_AUTH_*` secrets because `configureEntraAuth` is `true`; the deploy job fails fast without them. They are never committed to the repository.

### `development-infrastructure-preview` (new — pull-request what-if only)

Used only by the `whatif` job on same-repository pull requests. Create it in **Settings → Environments** with:

- **Required reviewers**: add at least one reviewer so a human approves each preview before the Azure token is issued.
- **Deployment branches and tags**: permit same-repository pull-request branches — set to **All branches** (or a `refs/pull/*` name pattern). It must not be restricted to `main`, because pull-request runs deploy from the PR merge ref.
- The same-repository guard in the workflow (`github.event.pull_request.head.repo.full_name == github.repository`) is the primary boundary; fork pull requests can never reach this environment.

Secrets and variables:

| Entry | Type | Value |
| --- | --- | --- |
| `AZURE_INFRA_CLIENT_ID` | Secret | Application (client) ID of `tradingengine-github-infrastructure-development` |
| `AZURE_TENANT_ID` | Secret | Microsoft Entra tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Secret | Target development subscription |
| `AZURE_DEPLOYMENT_LOCATION` | Variable | `ukwest` |
| `AZURE_SQL_ENTRA_ADMIN_LOGIN` | Secret | Login/display name of the Microsoft Entra SQL administrator used by the merge what-if preview |
| `AZURE_SQL_ENTRA_ADMIN_OBJECT_ID` | Secret | Object ID of the Microsoft Entra SQL administrator used by the merge what-if preview |
| `AZURE_SQL_ENTRA_ADMIN_PRINCIPAL_TYPE` | Secret | `User`, `Group` or `Application` |
| `AZURE_DATABASE_PROBE_KEY` | Secret | Same generated probe key as `development`, so the preview renders the real app-settings change |
| `AZURE_ENTRA_AUTH_CLIENT_ID` | Secret | Same Easy Auth app registration client ID as `development`, so the preview renders the real `authsettingsV2` change |
| `AZURE_ENTRA_AUTH_ALLOWED_PRINCIPALS` | Secret | Same owner object-ID JSON array as `development` |

The `AZURE_SQL_ENTRA_ADMIN_*`, `AZURE_DATABASE_PROBE_KEY` and `AZURE_ENTRA_AUTH_*` entries are **secrets** on the `development-infrastructure-preview` environment and are used by the merge what-if preview so it renders the real SQL administrator, app-settings and Easy Auth configuration. The workflow fails fast if any is absent and never prints their values. They are not committed to the repository.

The environments are the trust boundary that prevents untrusted code from obtaining an Azure token.

## OIDC and Azure permissions

Infrastructure deployment uses a **separate Microsoft Entra identity** from application deployment. The existing application-deployment identity (`tradingengine-github-deploy-development`) keeps only its Website Contributor role on the Web App and receives no infrastructure permissions. The new infrastructure identity (`tradingengine-github-infrastructure-development`) receives only the permissions needed for Bicep what-if and infrastructure deployment, and is never used to deploy application binaries.

Both identities authenticate with OIDC federated credentials. **No client secret is created for either.**

Jason must perform the following bootstrap manually; the workflow does not change Azure RBAC.

### 1. Create the infrastructure Entra application

```powershell
$subscription = '<subscription-id>'
$tenantId = '<tenant-id>'
$infraAppDisplayName = 'tradingengine-github-infrastructure-development'

az login --tenant $tenantId
az account set --subscription $subscription
az account show --output table

$infraAppId = az ad app create `
  --display-name $infraAppDisplayName `
  --query appId -o tsv

$infraSpObjectId = az ad sp create `
  --id $infraAppId `
  --query id -o tsv
```

`$infraAppId` is the value for the `AZURE_INFRA_CLIENT_ID` secrets. `$infraSpObjectId` is the object ID used for all infrastructure role assignments — do not reuse the application-deployment principal.

### 2. Add both federated credentials

The infrastructure identity needs two federated credentials, one per GitHub environment:

| Purpose | Credential name | Subject |
| --- | --- | --- |
| Real infrastructure deployment | `github-development-infrastructure` | `repo:carndog@7319736/TradingEngine@1349997095:environment:development` |
| Pull-request what-if | `github-development-infrastructure-preview` | `repo:carndog@7319736/TradingEngine@1349997095:environment:development-infrastructure-preview` |

Both use issuer `https://token.actions.githubusercontent.com` and audience `api://AzureADTokenExchange`. This repository uses an ID-qualified OIDC subject format: `7319736` is the GitHub owner ID of `carndog` and `1349997095` is the repository ID of `TradingEngine`. These are public, stable GitHub identifiers required by the customized subject format — they are not secrets. Write each credential to a temporary JSON file and pass the file path to `--parameters` to avoid the Windows PowerShell/native-command quoting problem.

```powershell
$federatedCredentialPath = Join-Path `
  $env:TEMP `
  'tradingengine-github-infrastructure-federated-credentials.json'

@(
    @{
        name = 'github-development-infrastructure'
        issuer = 'https://token.actions.githubusercontent.com'
        subject = 'repo:carndog@7319736/TradingEngine@1349997095:environment:development'
        audiences = @('api://AzureADTokenExchange')
    },
    @{
        name = 'github-development-infrastructure-preview'
        issuer = 'https://token.actions.githubusercontent.com'
        subject = 'repo:carndog@7319736/TradingEngine@1349997095:environment:development-infrastructure-preview'
        audiences = @('api://AzureADTokenExchange')
    }
) | ForEach-Object {
    $_ | ConvertTo-Json -Depth 3 | Set-Content `
        -Path $federatedCredentialPath `
        -Encoding ascii

    az ad app federated-credential create `
      --id $infraAppId `
      --parameters $federatedCredentialPath
}

Remove-Item $federatedCredentialPath
```

### Narrowest viable permissions

A subscription-scope `az deployment sub create` that manages `rg-tradingengine-dev` needs:

- `Microsoft.Resources/deployments/*` at **subscription scope** — create deployments, run what-if and read outputs.
- `Microsoft.Resources/subscriptions/resourcegroups/read` and `Microsoft.Resources/subscriptions/resourcegroups/write` at **subscription scope** — the template declares the resource group itself, so the deployment must be able to create and reconcile it.
- **Contributor on `rg-tradingengine-dev`** — create and reconcile the App Service Plan and Web App inside the group, and read existing resources during what-if.

**Bootstrap decision, stated explicitly:** `Microsoft.Resources/subscriptions/resourcegroups/write` at subscription scope allows the identity to create *any* resource group in the subscription, not only `rg-tradingengine-dev`. This is unavoidable while the template manages the resource group at subscription scope. It is still far narrower than subscription-wide Contributor, which must not be granted. If that breadth is unacceptable, the alternative is to keep resource-group creation as a manual step and remove it from the template — a template design change that is out of scope for this issue.

### 3. Assign the infrastructure roles

All commands use placeholders. Do not commit real IDs to this repository. All infrastructure role assignments target `$infraSpObjectId` — the new infrastructure service principal — never the application-deployment principal.

```powershell
$resourceGroupName = 'rg-tradingengine-dev'
```

Create a custom role for the subscription-scope operations, scoped to the subscription only:

```powershell
$roleDefinitionPath = Join-Path $env:TEMP 'tradingengine-infra-deployer-role.json'

@{
    Name = 'TradingEngine infrastructure deployer'
    Description = 'Subscription-scope deployment and resource group management for TradingEngine development infrastructure.'
    AssignableScopes = @("/subscriptions/$subscription")
    Actions = @(
        'Microsoft.Resources/subscriptions/resourcegroups/read'
        'Microsoft.Resources/subscriptions/resourcegroups/write'
        'Microsoft.Resources/deployments/*'
    )
    NotActions = @()
} | ConvertTo-Json -Depth 3 | Set-Content -Path $roleDefinitionPath -Encoding ascii

az role definition create --role-definition $roleDefinitionPath
Remove-Item $roleDefinitionPath
```

Assign it at subscription scope, and Contributor on the development resource group:

```powershell
az role assignment create `
  --assignee-object-id $infraSpObjectId `
  --assignee-principal-type ServicePrincipal `
  --role 'TradingEngine infrastructure deployer' `
  --scope "/subscriptions/$subscription"

az role assignment create `
  --assignee-object-id $infraSpObjectId `
  --assignee-principal-type ServicePrincipal `
  --role 'Contributor' `
  --scope "/subscriptions/$subscription/resourceGroups/$resourceGroupName"
```

The application-deployment identity is untouched: it keeps Website Contributor on the Web App and nothing else. The infrastructure identity holds only the two assignments above.

## Trusted versus fork pull requests

| Pull request source | `validate` | `whatif` | `deploy` |
| --- | --- | --- | --- |
| This repository | Runs | Runs via `development-infrastructure-preview` | Never on PRs |
| Fork | Runs | Skipped | Never on PRs |

Fork pull requests execute only the unprivileged validation job: no OIDC token is issued, no environment secrets are exposed and no Azure operation is attempted. Same-repository pull requests use only the `development-infrastructure-preview` environment; the `development` environment is reachable solely from a push to `main` or a manual run started from `main`.

## Local validation

Use `--stdout` so generated ARM JSON is not written into `infra/`:

```powershell
az bicep build --file infra/main.bicep --stdout | Out-Null
az bicep lint --file infra/main.bicep
```

## Manual what-if and recovery deployment

Substitute the real subscription; do not commit it.

```powershell
$subscription = '<subscription-id>'
$deploymentName = 'tradingengine-dev-manual'

az deployment sub what-if `
  --name $deploymentName `
  --subscription $subscription `
  --location ukwest `
  --template-file infra/main.bicep `
  --parameters infra/environments/dev.bicepparam

az deployment sub create `
  --name $deploymentName `
  --subscription $subscription `
  --location ukwest `
  --template-file infra/main.bicep `
  --parameters infra/environments/dev.bicepparam
```

See [Azure development deployment](azure-development-deployment.md) for the full manual path including publishing the API.

## Verifying an unchanged template

Run what-if (locally or via a pull request) and confirm the result reports no changes — the workflow summary prints `No changes detected` when the change list is empty. Any `Modify` or `Create` entries indicate drift between the template and the deployed resources and should be reviewed before merging.

## First run after merging

Merging a pull request that changes `infra/**` or this workflow triggers a real `deploy` run on the merge push to `main`. Since issue #35, that run creates the Azure SQL logical server and free-offer database and sets `ConnectionStrings__TradingEngine` on the Web App, in addition to reconciling the existing paid B1 App Service Plan and Web App in `rg-tradingengine-dev`. Review the merge what-if output on the pull request before merging to confirm the expected change set, and obtain Jason's explicit cost approval immediately before the provisioning deployment.

## Revocation and teardown

Remove the infrastructure permissions without affecting application deployment:

```powershell
az role assignment delete `
  --assignee $infraSpObjectId `
  --role 'TradingEngine infrastructure deployer' `
  --scope "/subscriptions/$subscription"

az role assignment delete `
  --assignee $infraSpObjectId `
  --role 'Contributor' `
  --scope "/subscriptions/$subscription/resourceGroups/$resourceGroupName"

az role definition delete --name 'TradingEngine infrastructure deployer'
```

To revoke the infrastructure identity entirely without touching application deployment, delete its Entra application (this also removes its service principal and both federated credentials):

```powershell
az ad app delete --id $infraAppId
```

To remove the deployed resources, delete only the development resource group:

```powershell
az group delete --name $resourceGroupName --subscription $subscription --yes --no-wait
```

To revoke the application-deployment identity (which stops application deployments), delete its federated credential or Entra application as described in [GitHub Actions development deployment](github-actions-development-deployment.md).
