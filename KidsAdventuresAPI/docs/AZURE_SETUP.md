# Azure setup — configuration in appsettings

All API settings live in **JSON appsettings files** (not Azure Portal Application settings).

| File | When loaded | Git |
|------|-------------|-----|
| `appsettings.json` | Always (logging, hosts) | ✅ Committed |
| `appsettings.Development.json` | `ASPNETCORE_ENVIRONMENT=Development` | ✅ Committed |
| `appsettings.Production.json` | `ASPNETCORE_ENVIRONMENT=Production` | ❌ **Gitignored** (secrets) |
| `appsettings.Production.example.json` | Never (template only) | ✅ Committed |

## First-time setup

1. Copy the example file:
   ```powershell
   cd KidsAdventuresAPI
   copy appsettings.Production.example.json appsettings.Production.json
   ```
2. Edit **`appsettings.Production.json`** and fill every `REPLACE_WITH_...` value.
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
  "Cors": { "AllowedOrigins": [ "https://your-frontend.azurewebsites.net" ] },
  "Seed": { "Enabled": true, "DemoEmail": "...", "DemoPassword": "..." },
  "Stripe": { "SecretKey": "...", "SuccessUrl": "...", "CancelUrl": "..." }
}
```

### Seed (first deploy)

- `Seed:Enabled`: `true` on first start → creates demo users.
- Then set `Seed:Enabled`: `false`.
- Demo login: `demo@adventurepacks.com` / `Adventure123!`

### Database schema

On startup the API runs `Data/Scripts/001_InitialSchema.sql` and `002_...` automatically. No manual SQL required if the connection string is correct.

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
