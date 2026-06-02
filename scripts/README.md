# Local dev scripts (PowerShell)

Run from **any** folder, or right-click → **Run with PowerShell**.

| Script | What it does |
|--------|----------------|
| `run-backend.ps1` | Starts .NET API (`dotnet run --launch-profile Production`) |
| `run-frontend.ps1` | Installs npm deps if needed, starts `npm run dev` |
| `run-dev.ps1` | Opens **two** terminals (API + frontend) |

## First-time setup

1. Create `KidsAdventuresAPI\appsettings.Production.json` from `appsettings.Production.example.json` and add secrets.
2. Ensure `KidsAdventuresAPI\wwwroot\.env` has:
   ```env
   VITE_API_BASE_URL=http://localhost:5000
   ```
   (Match the URL printed when the API starts.)

## Examples

```powershell
cd C:\Users\Relloran\source\repos\ReziJikhvadze\KidsAdventures\scripts
.\run-backend.ps1
```

```powershell
.\run-frontend.ps1
```

```powershell
.\run-dev.ps1
```

If execution is blocked:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

## URLs

| Service | Typical URL |
|---------|-------------|
| API / Swagger | http://localhost:5000/swagger |
| Frontend | http://localhost:5173 (see Vite output) |
| Demo login | `demo@adventurepacks.com` / `Adventure123!` |
