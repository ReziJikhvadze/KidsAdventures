# Deployment

Two GitHub Actions workflows replace the manual PowerShell scripts.

- `.github/workflows/ci.yml` — every push and PR. Secret scan, API build, migrations
  applied against a throwaway SQL Server container, frontend typecheck/lint/build.
- `.github/workflows/deploy.yml` — on a `v*` tag, or run by hand. Builds both apps,
  deploys them, and smoke-tests each one.

Deploys are not automatic on push to `master`. Migrations apply during API startup
against the live Azure SQL, so a deploy changes the schema — that should be a decision,
not a side effect of merging.

## One-time setup

### 1. Federated credentials (no stored password)

The workflow authenticates to Azure with OIDC. GitHub presents a short-lived token that
Azure trusts for this repository only; nothing long-lived is stored in GitHub.

```bash
az ad app create --display-name "github-kidsadventures-deploy"
# note the appId
az ad sp create --id <appId>

az role assignment create \
  --assignee <appId> \
  --role Contributor \
  --scope /subscriptions/<subscriptionId>/resourceGroups/<resourceGroup>
```

Add one federated credential per trusted ref. The `subject` must match exactly:

```bash
# tag pushes (v1.2.3)
az ad app federated-credential create --id <appId> --parameters '{
  "name": "github-tags",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:ReziJikhvadze/KidsAdventures:ref:refs/tags/v1.0.0",
  "audiences": ["api://AzureADTokenExchange"]
}'

# the production environment (covers workflow_dispatch)
az ad app federated-credential create --id <appId> --parameters '{
  "name": "github-env-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:ReziJikhvadze/KidsAdventures:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

Tag subjects are literal — a credential for `refs/tags/v1.0.0` does not match `v1.0.1`.
Use the `environment:production` credential as the general path and let the workflow's
environment gate do the controlling.

### 2. Repository secrets and variables

Settings → Secrets and variables → Actions.

| Kind | Name | Value |
| --- | --- | --- |
| Secret | `AZURE_CLIENT_ID` | the `appId` above |
| Secret | `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| Secret | `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |
| Variable | `AZURE_RESOURCE_GROUP` | your resource group |
| Variable | `AZURE_API_APP_NAME` | `adventuresapi-…` (App Service name, not hostname) |
| Variable | `AZURE_FRONTEND_APP_NAME` | `adventuresfront-…` |

These are not credentials in the old sense — `AZURE_CLIENT_ID` is an identifier, and it
is useless without the federation trust.

### 3. Approval gate

Settings → Environments → `production` → add yourself as a required reviewer. Both deploy
jobs then pause for approval before touching Azure.

### 4. Application configuration

The deploy **deletes `appsettings.Production.json` from the artifact** on purpose. A
deployed copy would shadow App Service configuration and could reintroduce a committed
secret. Every value must live in App Service configuration instead:

```
ConnectionStrings__DefaultConnection
Jwt__SecretKey
OpenAI__ApiKey
AzureBlobStorage__ConnectionString
Email__SmtpPassword
Stripe__SecretKey
Stripe__WebhookSecret
Recaptcha__SecretKey
```

See `KidsAdventuresAPI/docs/SECRETS_ROTATION.md`. Rotate before the first deploy — the old
values are still in git history.

## Releasing

```bash
git tag v1.0.0
git push origin v1.0.0
```

Or Actions → Deploy → Run workflow, with checkboxes to deploy one side only.

## When a deploy fails

The API smoke test polls `/api/auth/config` for five minutes. If it never returns 200 the
usual cause is a migration that failed at startup, which blocks the app from listening.
Check the App Service log stream:

```bash
az webapp log tail --resource-group <rg> --name <api-app-name>
```

App Service keeps the previous deployment, so rolling back is a redeploy of the last good
tag rather than a restore.

## Known gap

There is no automated test suite yet, so CI proves the code compiles and the migrations
apply — not that the behaviour is correct. Until that exists, treat the approval gate as
the real safety mechanism and deploy when you can watch it.
