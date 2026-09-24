# Entra ID Easy Auth for the deployed API

Issue #38 protects the deployed administration API with App Service Authentication ("Easy Auth") and Microsoft Entra ID. Easy Auth is platform middleware that runs **in front of** the ASP.NET Core application inside the App Service sandbox: a request must pass Easy Auth before Kestrel ever sees it. The `AllowAnonymous()` calls in `Program.cs` only affect ASP.NET's own authorization pipeline — they do **not** bypass Easy Auth, and no application code change is required.

## The four identities involved

These are separate principals with separate jobs. None can substitute for another.

- **Entra app registration (`tradingengine-api-dev-auth`)** — the OAuth2/OIDC identity of the API itself. Easy Auth uses it to sign the owner in and to validate tokens. It is the only identity a caller ever authenticates against.
- **Easy Auth managed identity (`id-tradingengine-easyauth-dev`)** — a dedicated user-assigned managed identity that replaces the app registration's client secret. The Web App presents a token from this identity to Entra as a federated credential when completing sign-ins. It must never be assigned to any other resource: anything holding this identity can authenticate as the app registration.
- **Web App system-assigned managed identity** — the API's own Azure identity, used only for the passwordless Azure SQL connection (`Authentication=Active Directory Default`). Unrelated to sign-in.
- **GitHub deployment identities** (`tradingengine-github-deploy-development`, `tradingengine-github-infrastructure-development`) — OIDC workload identities that let GitHub Actions deploy code and infrastructure. They cannot call the API and are not in the Easy Auth allowlist.

## Access policy

`configureEntraAuth = true` in `dev.bicepparam` turns on `authsettingsV2` in `infra/modules/app-service.bicep`:

- `requireAuthentication: true` with `unauthenticatedClientAction: 'Return401'` — every request needs a valid token or Easy Auth session; anonymous callers get HTTP 401, not a login redirect, because this is an API.
- `openIdIssuer` is the **single-tenant** v2.0 issuer (`https://login.microsoftonline.com/<tenant-id>/v2.0`), so tokens from any other tenant fail issuer validation.
- `defaultAuthorizationPolicy.allowedPrincipals.identities` is the **owner allowlist** — an explicit list of Entra object IDs supplied at deploy time. A successful tenant sign-in alone does **not** grant access: a non-allowlisted user from the same tenant is rejected with HTTP 403. The portal does not expose this setting; it exists only through `authsettingsV2` in Bicep.
- `allowedAudiences` accepts `api://<client-id>` and the bare client ID.

### Endpoint exposure policy

| Endpoint | Policy | Reason |
| --- | --- | --- |
| `GET /health` | Anonymous (`excludedPaths`) | Explicitly available to outside smoke checks. App Service Health Check integrates with built-in authentication and does not require this public exception. Response contains only `{"status":"Healthy"}` and never touches SQL. |
| `GET /version` | Anonymous (`excludedPaths`) | The deploy workflow verifies the deployed commit with an unauthenticated `curl`. Response is application name, version and commit SHA — all public-repository facts. |
| `GET /health/database` | Anonymous (`excludedPaths`) **plus** probe key | Kept anonymous so readiness probes do not need an Entra token, but still gated by `X-Database-Probe-Key`: without the header it returns 404, with it only `{"status":"..."}`. The key stays a deploy-time secret. |
| `GET /auth-check` and future administration endpoints from #4 | Owner allowlist | `/auth-check` returns 204 with no data when an owner request reaches the API. Every non-excluded path requires an allowed principal. |

`excludedPaths` entries are listed explicitly rather than relying on prefix matching, so the policy does not depend on undocumented matching behaviour.

## Secretless credential: managed identity federation

Easy Auth normally stores a client secret in the `MICROSOFT_PROVIDER_AUTHENTICATION_SECRET` app setting. This deployment instead uses Microsoft's supported **federated identity credential** pattern:

1. `infra/modules/app-service.bicep` creates `id-tradingengine-easyauth-dev` (user-assigned) and assigns it to the Web App alongside the existing system-assigned identity.
2. The slot-sticky app setting `OVERRIDE_USE_MI_FIC_ASSERTION_CLIENTID` holds that identity's **client ID** (an identifier, not a secret).
3. `authsettingsV2` sets `clientSecretSettingName` to `OVERRIDE_USE_MI_FIC_ASSERTION_CLIENTID`, telling Easy Auth to authenticate as the app registration with a managed-identity assertion instead of a secret.
4. A **federated identity credential** on the app registration (added manually in the portal, after the identity exists) trusts `id-tradingengine-easyauth-dev`.

There is no secret to store, rotate or leak. If a client secret were ever required instead, it would go in Azure Key Vault with a least-privilege `Get` grant to the Web App identity and a `@Microsoft.KeyVault(...)` app-setting reference — never a plaintext setting, parameter file or commit.

## Azure configuration (portal)

Steps 1–6 were performed in the Azure portal. Step 7 is optional and has not been enabled in development. None of these portal settings is committed.

1. **App registration** — Entra → App registrations → New registration. Single tenant ("Accounts in this organizational directory only"). Web redirect URI: `https://<web-app-default-hostname>/.auth/login/aad/callback`.
2. **Enable ID tokens** — In the app registration, open Authentication → Settings → Implicit grant and hybrid flows. Select **ID tokens** and Save; leave **Access tokens** unselected. During live verification, Easy Auth requested `response_type=id_token`; without this setting Entra returned AADSTS700054 and login could not complete.
3. **Application ID URI** — Expose an API → Set → accept `api://<application-client-id>`.
4. **Owner object ID** — Entra → Users → the owner → Object ID. Goes into the `AZURE_ENTRA_AUTH_ALLOWED_PRINCIPALS` GitHub secret as a JSON array, e.g. `["<object-id>"]`.
5. **GitHub environment secrets** — `AZURE_ENTRA_AUTH_CLIENT_ID` (the app registration's client ID) and `AZURE_ENTRA_AUTH_ALLOWED_PRINCIPALS` on both `development` and `development-infrastructure-preview`.
6. **Federated credential** — after the Bicep deployment creates `id-tradingengine-easyauth-dev`: app registration → Certificates & secrets → Federated credentials → Add credential → **Managed Identity** scenario → select `id-tradingengine-easyauth-dev`.
7. **Assignment required (optional extra Entra restriction; currently No)** — Entra → Enterprise applications → `tradingengine-api-dev-auth` → Properties → *Assignment required?*. The development application currently uses **No**; App Service still applies the owner allowlist and rejected a signed-in, non-allowlisted tenant user with HTTP 403. If you later set this to **Yes**, assign the owner under Users and groups and grant the application the required admin consent before testing sign-in. Entra then blocks unassigned users before they reach the App Service allowlist.

Between the deployment (step 6 prerequisite) and the federated credential, Easy Auth is enabled but cannot complete sign-ins — the API fails closed (401/403 for everything non-excluded), which is safe for the development environment.

## Local development

Easy Auth does not exist locally. `dotnet run --project src/TradingEngine.Api` serves every endpoint anonymously; `AllowAnonymous()` is accurate there and the probe-key middleware still gates `/health/database`. Local testing of the deployed policy is not possible — verification happens against the deployed app only.

## Verification (deployed)

After the Bicep deployment creates the managed identity, add its federated credential to the app registration. In a private browser window, open `https://<web-app-default-hostname>/auth-check` to confirm an anonymous request receives 401. Then open `https://<web-app-default-hostname>/.auth/login/aad?post_login_redirect_uri=/auth-check`, sign in as the owner and confirm `/auth-check` returns 204. This uses the App Service browser sign-in endpoint and needs no Azure CLI command. A 204 proves the request reached the ASP.NET API without exposing claims or tokens.

For Rider, set `deployedHost`, `bearerToken` and `databaseProbeKey` in `src/TradingEngine.Api/http-client.private.env.json` (gitignored), then use `src/TradingEngine.Api/TradingEngine.Api.http`. The bearer token must be an access token for this API acquired by an approved client application. Configuring a native client and delegated API scope for that flow is a separate step; do not assume a generic Azure CLI token has the right audience or consent.

| Check | Expected |
| --- | --- |
| `GET /auth-check` anonymously | 401 — anonymous rejected |
| `GET /health`, `/version` anonymously | 200 — documented anonymous policy |
| `GET /health/database` without probe key | 404 — probe-key gate intact |
| `GET /auth-check` with owner session or API bearer token | 204 — request reached the API |
| Same-tenant non-owner on `/auth-check` | 403 — allowlist rejects |
| Token from another tenant | 401 — issuer rejected |

If a second same-tenant user, a second tenant or a supported Rider token flow is unavailable, record that check as unverified rather than claiming it passed.

## Rotation and revocation

- **Nothing to rotate** — there is no client secret or certificate. The managed-identity assertion is short-lived and platform-managed.
- **Revoke a person** — remove their object ID from `AZURE_ENTRA_AUTH_ALLOWED_PRINCIPALS` and redeploy (or remove their Enterprise application assignment).
- **Revoke the credential path** — delete the federated credential on the app registration; Easy Auth can then no longer complete sign-ins.
- **Emergency stop of all access** — in the Azure portal, stop the Web App. Remove the federated credential to prevent new sign-ins and revoke allowed assignments as appropriate before restarting. Existing sessions may continue until invalidated, so removing the credential alone is not an immediate all-access shutdown. Do not use `configureEntraAuth = false` as revocation: incremental deployment leaves the existing `authsettingsV2` resource in place, and turning authentication off directly would expose the API.
- **Compromise of the Easy Auth identity** — delete `id-tradingengine-easyauth-dev` and redeploy; the federated credential then points at nothing.

## What the Bicep changes

`infra/modules/app-service.bicep` adds, only when `configureEntraAuth` is true:

- `Microsoft.ManagedIdentity/userAssignedIdentities` — the dedicated Easy Auth identity.
- Web App `identity` becomes `SystemAssigned, UserAssigned` (system-assigned is preserved for SQL).
- `Microsoft.Web/sites/config` `authsettingsV2` — the policy described above.
- `OVERRIDE_USE_MI_FIC_ASSERTION_CLIENTID` app setting (marked slot-sticky via `slotConfigNames`).
- Two outputs (`easyAuthIdentityName`, `easyAuthIdentityPrincipalId`) so the portal federated-credential step can find the identity.

The SQL connection string, probe-key setting, system-assigned identity, remaining app settings and the App Service Plan free-offer expiry are untouched; confirm that in the pull-request what-if before merging.
