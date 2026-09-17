# GitHub Actions infrastructure deployment

The workflow `.github/workflows/deploy-infrastructure-development.yml` validates and deploys the subscription-scope Bicep in `infra/` to the development environment. It uses the same GitHub-to-Azure OpenID Connect (OIDC) identity as the application workflow; no client secret, publish profile or other long-lived credential is stored anywhere.

Application build, test and deployment remain the responsibility of `.github/workflows/deploy-development.yml`. The infrastructure workflow never builds or deploys application binaries.

## Workflow behaviour

The workflow triggers only when `infra/**` or the workflow file itself changes:

- **Pull requests targeting `main`**: the `validate` job runs `az bicep build --stdout` and `az bicep lint` against `infra/main.bicep`. For pull requests originating from this repository, a `whatif` job then previews the subscription-scope deployment and writes the change list to the workflow summary.
- **Pushes to `main`** (including merged pull requests): `validate` runs, then `deploy` runs `az deployment sub create` and records the commit, deployment name and Bicep outputs in the workflow summary.
- **Manual `workflow_dispatch`**: recovery option. The `deploy` job still requires `refs/heads/main`, so a manual run only deploys when started from `main`.

The `validate` job holds only `contents: read` and receives no Azure OIDC token or environment secrets, so it is safe on fork pull requests. The `whatif` job is additionally gated on `github.event.pull_request.head.repo.full_name == github.repository`, so fork pull requests never reach the `development` environment or receive an OIDC token. The `deploy` job uses a `deploy-infrastructure-development` concurrency group with `cancel-in-progress: false` so overlapping deployments queue rather than cancel halfway through.

Deployment names are unique per run (`tradingengine-dev-<run-id>` for deployments, `tradingengine-dev-whatif-<run-id>` for previews) so each run is identifiable in the subscription deployment history.

## Required GitHub environment configuration

The workflow reuses the existing `development` environment. No new secrets are required.

Existing secrets (already configured):

| Secret | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | Application (client) ID of the GitHub deployment identity |
| `AZURE_TENANT_ID` | Microsoft Entra tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Target development subscription |

Existing variables (already configured, used by the application workflow):

| Variable | Value |
| --- | --- |
| `AZURE_WEBAPP_NAME` | Existing Web App name |
| `AZURE_WEBAPP_HOSTNAME` | Web App default hostname |

New variable to add:

| Variable | Value |
| --- | --- |
| `AZURE_DEPLOYMENT_LOCATION` | `ukwest` — the Azure region used as the subscription-scope deployment location |

Keep the environment's deployment branch restriction limited to `main` and retain any required reviewers; the environment is the trust boundary that prevents untrusted code from obtaining an Azure token.

## OIDC and Azure permissions

The existing federated credential on the deployment application trusts `repo:carndog/TradingEngine:environment:development`. Because the infrastructure workflow uses the same `development` environment, **no new federated credential is needed**.

The current role assignment — **Website Contributor scoped to the Web App** — is sufficient for application deployment but cannot run a subscription-scope deployment. Jason must perform the following bootstrap manually; the workflow does not change Azure RBAC.

### Narrowest viable permissions

A subscription-scope `az deployment sub create` that manages `rg-tradingengine-dev` needs:

- `Microsoft.Resources/deployments/*` at **subscription scope** — create deployments, run what-if and read outputs.
- `Microsoft.Resources/subscriptions/resourcegroups/read` and `Microsoft.Resources/subscriptions/resourcegroups/write` at **subscription scope** — the template declares the resource group itself, so the deployment must be able to create and reconcile it.
- **Contributor on `rg-tradingengine-dev`** — create and reconcile the App Service Plan and Web App inside the group, and read existing resources during what-if.

**Bootstrap decision, stated explicitly:** `Microsoft.Resources/subscriptions/resourcegroups/write` at subscription scope allows the identity to create *any* resource group in the subscription, not only `rg-tradingengine-dev`. This is unavoidable while the template manages the resource group at subscription scope. It is still far narrower than subscription-wide Contributor, which must not be granted. If that breadth is unacceptable, the alternative is to keep resource-group creation as a manual step and remove it from the template — a template design change that is out of scope for this issue.

### Bootstrap steps

All commands use placeholders. Do not commit real IDs to this repository.

```powershell
$subscription = '<subscription-id>'
$servicePrincipalObjectId = '<deployment-sp-object-id>'
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
  --assignee-object-id $servicePrincipalObjectId `
  --assignee-principal-type ServicePrincipal `
  --role 'TradingEngine infrastructure deployer' `
  --scope "/subscriptions/$subscription"

az role assignment create `
  --assignee-object-id $servicePrincipalObjectId `
  --assignee-principal-type ServicePrincipal `
  --role 'Contributor' `
  --scope "/subscriptions/$subscription/resourceGroups/$resourceGroupName"
```

The existing Website Contributor assignment on the Web App remains in place for the application workflow; the assignments are additive.

## Trusted versus fork pull requests

| Pull request source | `validate` | `whatif` | `deploy` |
| --- | --- | --- | --- |
| This repository | Runs | Runs | Never on PRs |
| Fork | Runs | Skipped | Never on PRs |

Fork pull requests execute only the unprivileged validation job: no OIDC token is issued, no environment secrets are exposed and no Azure operation is attempted.

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

Merging the pull request that introduces this workflow changes a file under the workflow's own path filter, so the merge push to `main` triggers a real `deploy` run. That run reconciles the existing paid B1 App Service Plan and Web App in `rg-tradingengine-dev` against the template — expected changes are limited to tag or configuration drift. The template contains no database resources; the deployment must not create Azure SQL or any other new resource type. Review the what-if output on the pull request before merging to confirm the expected change set.

## Revocation and teardown

Remove the infrastructure permissions without affecting application deployment:

```powershell
az role assignment delete `
  --assignee $servicePrincipalObjectId `
  --role 'TradingEngine infrastructure deployer' `
  --scope "/subscriptions/$subscription"

az role assignment delete `
  --assignee $servicePrincipalObjectId `
  --role 'Contributor' `
  --scope "/subscriptions/$subscription/resourceGroups/$resourceGroupName"

az role definition delete --name 'TradingEngine infrastructure deployer'
```

To remove the deployed resources, delete only the development resource group:

```powershell
az group delete --name $resourceGroupName --subscription $subscription --yes --no-wait
```

To revoke the identity entirely (which also stops application deployments), delete the federated credential or the Entra application as described in [GitHub Actions development deployment](github-actions-development-deployment.md).
