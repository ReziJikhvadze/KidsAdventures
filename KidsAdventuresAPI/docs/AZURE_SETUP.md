# Azure setup — configuration in appsettings

All API settings live in **JSON appsettings files** (not Azure Portal Application settings).

| File | When loaded | Git |
|------|-------------|-----|
| `appsettings.json` | Always (logging, hosts) | ✅ Committed |
| `appsettings.Development.json` | `ASPNETCORE_ENVIRONMENT=Development` | ✅ Committed |
| `appsettings.Production.json` | `ASPNETCORE_ENVIRONMENT=Production` | ❌ **Gitignored** (secrets) |
| `appsettings.Production.example.json` | Never (template only) | ✅ Committed |

## First-time setup

1. Copy the example file (required — the API will not start without this file):
   ```powershell
   cd KidsAdventuresAPI
   Copy-Item appsettings.Production.example.json appsettings.Production.json
   ```
2. Edit **`appsettings.Production.json`** and fill every `REPLACE_WITH_...` value (SQL connection string, `Jwt:SecretKey`, OpenAI, Azure blob, etc.).
   - `Jwt:SecretKey` must be at least 32 characters (use a long random string).
   - `appsettings.json` includes a **local-only** JWT fallback; Production settings override it when present.
3. Your Azure SQL connection string goes under (catalog = **`adventuresapi-database`**):
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Server=tcp:adventuresapi-server.database.windows.net,1433;Initial Catalog=adventuresapi-database;User ID=...;Password=...;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
   }
   ```

## Azure App Service

1. Set these in the portal (Configuration → Application settings):
   - **`ASPNETCORE_ENVIRONMENT`** = `Production`
   - **`WEBSITE_NODE_DEFAULT_VERSION`** = `~20` (Node sidecar for hosted frontend)
2. **General settings** → **Always On** = **On** (required for Hangfire / long story jobs)
3. **Do not** duplicate JWT/OpenAI/DodoPayments/Stripe/SQL in portal settings — the app reads `appsettings.Production.json` from the deployed package.
4. Ensure `appsettings.Production.json` is included when you publish (it is on your machine; `.gitignore` only blocks git, not deploy).
5. Replace every `YOUR-API.azurewebsites.net` in `appsettings.Production.json` with your real App Service hostname before publish.

## Production file sections

```json
{
  "ConnectionStrings": { "DefaultConnection": "..." },
  "Jwt": { "SecretKey": "...", "Issuer": "...", "Audience": "..." },
  "OpenAI": { "ApiKey": "...", "Model": "gpt-4.1-mini" },
  "AzureBlobStorage": { "ConnectionString": "...", "ContainerName": "adventurepacks" },
  "Cors": {
    "AllowLocalhostFallback": false,
    "AllowedOrigins": [ "https://your-frontend.azurewebsites.net", "http://localhost:5173" ]
  },
  "Seed": { "Enabled": true, "DemoEmail": "...", "DemoPassword": "..." },
  "DodoPayments": { "Enabled": true, "ApiKey": "...", "WebhookSecret": "...", "Books3ProductId": "...", "SuccessUrl": "https://your-frontend/billing/success" },
  "Stripe": { "Enabled": false }
}
```

### Seed (first deploy)

- `Seed:Enabled`: `true` on first start → creates demo users.
- Then set `Seed:Enabled`: `false`.
- Demo login: `demo@adventurepacks.com` / `Adventure123!`

### OpenAI (text + story illustrations)

The API uses the [Responses API](https://developers.openai.com/api/docs/api-reference/responses) for:

- **Story JSON** — `POST /v1/responses` with your `OpenAI:Model` (default `gpt-4.1-mini`)
- **Story page images** — same API with `tools: [{ "type": "image_generation" }]` (see [images & vision guide](https://developers.openai.com/api/docs/guides/images-vision))

Recommended `OpenAI` section:

```json
"OpenAI": {
  "ApiKey": "...",
  "Model": "gpt-4.1-mini",
  "BaseUrl": "https://api.openai.com/v1",
  "ImageGenerationProvider": "responses",
  "ImageModel": "",
  "EnableStoryImages": true
}
```

- `ImageGenerationProvider`: `"responses"` (default) or `"dall-e"` to force the legacy Images API only.
- `ImageModel`: leave empty to reuse `Model` for Responses image generation; set `dall-e-3` only if using `"dall-e"` provider.
- If Responses image generation fails, the app **automatically falls back** to `images/generations` (DALL·E 3).

Set `"EnableStoryImages": false` to skip images and reduce cost.

### Database schema

On startup the API runs `Data/Scripts/001_InitialSchema.sql` and `002_...` automatically. No manual SQL required if the connection string is correct.

## Run locally (scripts)

From the repo root, use PowerShell scripts in **`scripts/`**:

```powershell
cd scripts
.\run-dev.ps1          # API + frontend in two windows
# or separately:
.\run-backend.ps1
.\run-frontend.ps1
```

See `scripts/README.md` for URLs and demo login.

## Run the API (Azure only)

This project is configured for **Azure SQL** (`adventuresapi-database`). There is no local database.

- All settings: **`appsettings.Production.json`**
- Run with Production environment:

```powershell
dotnet run --launch-profile Production
```

Or in Visual Studio, choose the **Production** launch profile.

`appsettings.Development.json` only disables seed when accidentally run in Development mode.

## Frontend (separate Node App Service — recommended on Linux)

Create a **second** App Service: **Linux**, **Node 22 LTS** (Poland Central, same region as API).

### Build + zip

```powershell
cd scripts
.\publish-frontend-azure.ps1
```

Produces `KidsAdventuresAPI/frontend-deploy.zip` (Nitro `node-server` output). Upload this file only — not copies under `wwwroot/`.

`wwwroot/.env.production` must point at the API:

```env
VITE_API_BASE_URL=https://adventuresapi-guajeacbcucsbwau.polandcentral-01.azurewebsites.net
```

### Frontend App Service settings

| Setting | Value |
|---------|--------|
| **Startup Command** | `node server/index.mjs` |
| `SCM_DO_BUILD_DURING_DEPLOYMENT` | `false` |
| `WEBSITE_NODE_DEFAULT_VERSION` | `~22` |

Deploy: **Advanced Tools (Kudu)** → **Zip Push Deploy** → upload `frontend-deploy.zip`.

### API must allow the frontend origin

After you know the frontend URL (e.g. `https://your-frontend.polandcentral-01.azurewebsites.net`), update **`appsettings.Production.json`** on the API:

- `Cors:AllowedOrigins` → add the frontend URL
- `Email:BaseUrl` → frontend URL
- `Email:ApiBaseUrl` → API URL (unchanged)
- `DodoPayments:SuccessUrl` / `CancelUrl` → frontend billing paths (`/billing/success`, `/billing/cancel`)
- Dodo Dashboard webhook URL: `POST https://your-api.azurewebsites.net/api/subscriptions/webhook` (event: `payment.succeeded`)
- Set `Stripe:Enabled` to `false` while using Dodo
- `Frontend:EnableHostedNode` → `false` (API-only)

Republish the API after CORS/URL changes.

### Local dev

`wwwroot/.env`:

```env
VITE_API_BASE_URL=http://localhost:5071
```

## Deploy without a pipeline

**Visual Studio:** Right-click `KidsAdventuresAPI` → **Publish** → Azure App Service → **Release**.

One Publish does two things:

1. **Deploys the API** to your `adventuresapi` App Service (as today).
2. **Builds the frontend** and writes `KidsAdventuresAPI/frontend-deploy.zip` for `adventuresfront` (upload via ZipDeployUI — not auto-deployed).

To skip the npm build (API-only publish): set MSBuild property `SkipFrontendBuild=true` in the publish profile or run `dotnet publish -p:SkipFrontendBuild=true`.

**PowerShell:**

```powershell
cd scripts
.\publish-azure.ps1 -WebAppName "your-api-app" -ResourceGroup "your-rg"
```

Or zip-only (manual Kudu deploy): `.\publish-azure.ps1 -SkipAzDeploy`

**After first deploy (Azure Portal):**

- `ASPNETCORE_ENVIRONMENT` = `Production`
- **Always On** = On (required for Hangfire / story generation)
- Node.js 18+ on the app (default on Windows App Service)

Browse `https://your-api.azurewebsites.net` for the site and `/swagger` for the API.

## Security

- Never commit `appsettings.Production.json`.
- If a password was ever committed to git, **rotate** the Azure SQL password.
- Use `appsettings.Production.example.json` only as a template with placeholders.
