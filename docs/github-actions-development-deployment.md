# GitHub Actions development deployment

The workflow `.github/workflows/deploy-development.yml` builds, tests and deploys `TradingEngine.Api` to the existing development Azure App Service. It uses GitHub-to-Azure OpenID Connect (OIDC); no client secret, publish profile or other long-lived credential is stored anywhere.

## Workflow behaviour

- **Pull requests targeting `main`**: restore, build and test only.
- **Pushes to `main`** (including merged pull requests): restore, build, test, publish, package and deploy, then verify `/health` and `/version`.
- **Manual `workflow_dispatch`**: recovery option that runs the same pipeline. The deployment job still requires `refs/heads/main`, so a manual run only deploys when started from `main`.

The `build` job holds only `contents: read`. The `deploy` job adds `id-token: write` for OIDC, runs against the `development` GitHub environment and uses a `deploy-development` concurrency group with `cancel-in-progress: false` so development deployments never overlap.

The build stamps the assembly with the workflow commit via `-p:SourceRevisionId=${{ github.sha }}`. After deployment the workflow polls `/health` until it reports `Healthy` (retrying while the app restarts), then requires `/version` to return `commit` equal to the workflow SHA and an informational `version` that identifies the same commit.

## One-time bootstrap

All commands use placeholders. Do not commit real subscription IDs, tenant IDs, client IDs, the generated Web App name or the live hostname to this repository.

```powershell
$subscription = '<subscription-id>'
$tenantId = '<tenant-id>'
$resourceGroupName = '<web-app-resource-group>'
$webAppName = '<web-app-name>'
$appDisplayName = 'tradingengine-github-deploy-development'
```

Sign in to the intended tenant, select the subscription and verify the account before creating anything:

```powershell
az login --tenant $tenantId
az account set --subscription $subscription
az account show --output table
```

Confirm the displayed tenant and subscription are the intended development targets before continuing.

### 1. Create a dedicated Microsoft Entra application

Create an application used only for GitHub deployment, plus its service principal. **No client secret is created** — OIDC federated credentials replace secrets entirely.

```powershell
$appId = az ad app create --display-name $appDisplayName --query appId -o tsv
$servicePrincipalObjectId = az ad sp create --id $appId --query id -o tsv
```

`$appId` is the application (client) ID used for `AZURE_CLIENT_ID` and for application and federated-credential operations. `$servicePrincipalObjectId` is the service principal object ID used for role assignment operations.

### 2. Add the federated credential

The credential trusts GitHub OIDC tokens for the `development` environment of this repository only:

- **Issuer**: `https://token.actions.githubusercontent.com`
- **Audience**: `api://AzureADTokenExchange`
- **Subject**: `repo:carndog/TradingEngine:environment:development`

Write the credential definition to a temporary JSON file and pass the file path to `--parameters`. Passing a file avoids the Windows PowerShell/native-command quoting problem where Azure CLI receives the inline JSON with its quotation marks stripped.

```powershell
$federatedCredentialPath = Join-Path `
  $env:TEMP `
  'tradingengine-github-development-federated-credential.json'

@{
    name = 'github-development'
    issuer = 'https://token.actions.githubusercontent.com'
    subject = 'repo:carndog/TradingEngine:environment:development'
    audiences = @('api://AzureADTokenExchange')
} |
    ConvertTo-Json -Depth 3 |
    Set-Content `
        -Path $federatedCredentialPath `
        -Encoding ascii

az ad app federated-credential create `
  --id $appId `
  --parameters $federatedCredentialPath

Remove-Item $federatedCredentialPath
```

### 3. Assign Website Contributor on the Web App only

Grant the service principal **Website Contributor** scoped to the existing Web App resource. Do not grant Contributor at subscription or resource-group scope.

```powershell
$webAppId = az webapp show `
  --name $webAppName `
  --resource-group $resourceGroupName `
  --subscription $subscription `
  --query id -o tsv

az role assignment create `
  --assignee-object-id $servicePrincipalObjectId `
  --assignee-principal-type ServicePrincipal `
  --role "Website Contributor" `
  --scope $webAppId
```

### 4. Create the GitHub `development` environment

In the repository: **Settings → Environments → New environment** named `development`.

- Under **Deployment branches and tags**, restrict deployments to the `main` branch only.
- Add the environment secrets:

  | Secret | Value |
  | --- | --- |
  | `AZURE_CLIENT_ID` | The application (client) ID `$appId` |
  | `AZURE_TENANT_ID` | The Microsoft Entra tenant ID |
  | `AZURE_SUBSCRIPTION_ID` | The subscription containing the Web App |

- Add the environment variables:

  | Variable | Value |
  | --- | --- |
  | `AZURE_WEBAPP_NAME` | The existing Web App name |
  | `AZURE_WEBAPP_HOSTNAME` | The Web App default hostname, for example `<web-app-name>.azurewebsites.net` |

## Verify the configuration

Confirm the federated credential subject and the role assignment scope:

```powershell
az ad app federated-credential list --id $appId --output table

az role assignment list `
  --assignee $servicePrincipalObjectId `
  --scope $webAppId `
  --output table
```

The role assignment list must show `Website Contributor` scoped to the Web App resource ID and nothing broader. Then push to `main` (or run the workflow manually from `main`) and confirm the `Deploy development` workflow completes, including the health and version verification steps.

## Revoke access

Any one of the following stops GitHub deployments:

```powershell
# Remove the role assignment
az role assignment delete `
  --assignee $servicePrincipalObjectId `
  --role "Website Contributor" `
  --scope $webAppId

# Or delete the federated credential (list first to get its ID)
az ad app federated-credential list --id $appId --query "[].id" -o tsv
az ad app federated-credential delete --id $appId --federated-credential-id <credential-id>

# Or delete the dedicated Entra application entirely
az ad app delete --id $appId
```

Deleting the application also removes its service principal and all federated credentials.

## Notes

- SCM basic authentication is disabled on the Web App, so `azure/webapps-deploy` authenticates with the OIDC token from `azure/login`.
- The published package is passed between jobs as a workflow artifact with a one-day retention period.
- Infrastructure provisioning and Bicep validation are handled separately; this workflow deploys application code only and uses only the `AZURE_CLIENT_ID` application-deployment identity. The infrastructure workflow uses a separate `AZURE_INFRA_CLIENT_ID` identity; see [GitHub Actions infrastructure deployment](github-actions-infrastructure-deployment.md) for the automated path and [Azure development deployment](azure-development-deployment.md) for the manual path.
