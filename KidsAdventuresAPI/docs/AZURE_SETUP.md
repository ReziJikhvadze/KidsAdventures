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

1. Set only this in the portal (Configuration → Application settings):
   - **`ASPNETCORE_ENVIRONMENT`** = `Production`
2. **Do not** duplicate JWT/OpenAI/Stripe/SQL in portal settings — the app reads `appsettings.Production.json` from the deployed package.
3. Ensure `appsettings.Production.json` is included when you publish (it is on your machine; `.gitignore` only blocks git, not deploy).

## Production file sections

```json
{
  "ConnectionStrings": { "DefaultConnection": "..." },
  "Jwt": { "SecretKey": "...", "Issuer": "...", "Audience": "..." },
  "OpenAI": { "ApiKey": "...", "Model": "gpt-4.1-mini" },
  "AzureBlobStorage": { "ConnectionString": "...", "ContainerName": "adventure-packs" },
  "Cors": {
    "AllowLocalhostFallback": false,
    "AllowedOrigins": [ "https://your-frontend.azurewebsites.net", "http://localhost:5173" ]
  },
  "Seed": { "Enabled": true, "DemoEmail": "...", "DemoPassword": "..." },
  "Stripe": { "SecretKey": "...", "SuccessUrl": "...", "CancelUrl": "..." }
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

## Frontend

Still uses `wwwroot/.env`:

```env
VITE_API_BASE_URL=https://your-api.azurewebsites.net
```

## Security

- Never commit `appsettings.Production.json`.
- If a password was ever committed to git, **rotate** the Azure SQL password.
- Use `appsettings.Production.example.json` only as a template with placeholders.
