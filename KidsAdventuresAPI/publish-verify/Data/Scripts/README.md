# AdventurePacks SQL Scripts

Run these against your **SQL Server** database (local or Azure SQL).

Connection string is in `appsettings.Production.json` → `ConnectionStrings:DefaultConnection`.

**Azure catalog:** `adventuresapi-database` on server `adventuresapi-server.database.windows.net`.

## Which script set to use?

| Situation | Scripts to run |
|-----------|------------------|
| **Brand new database** (never had EF) | `001_InitialSchema.sql` only |
| **Upgrading from EF / AspNetUsers** | `001` (if app tables missing) → `002` → verify → `003` (optional cleanup) |
| **Already ran `dotnet ef database update` before** | `002` → `Manual/004_VerifyMigration.sql` → `Manual/003` (optional) |

## Recommended order (EF → Dapper upgrade)

1. **Back up** the database.
2. Run `001_InitialSchema.sql`  
   - Creates `Users`, `Children`, etc. if they do not exist yet.  
   - Safe if EF already created `Children` / `AdventurePacks` (skips existing tables).
3. Run `002_MigrateFromAspNetIdentityToUsers.sql`  
   - Copies `AspNetUsers` → `Users` (same `Id`, keeps passwords).  
   - Repoints FKs from `AspNetUsers` to `Users`.
4. Run `Manual/004_VerifyMigration.sql`  
   - Check row counts and orphan rows (should return empty result sets for problems).
5. Test the API: register/login, list children, generate a pack.
6. **Optional:** Run `Manual/003_CleanupAspNetIdentityTables.sql`  
   - Drops `AspNetUsers` and related Identity tables. **Only after login works.**

## How to run (SSMS / Azure Data Studio)

1. Connect to your server.
2. Open the `.sql` file.
3. Ensure the correct database is selected in the toolbar.
4. Execute (F5).

Scripts use `GO` batch separators — run the **entire file**, not line by line.

### Command line (sqlcmd)

```powershell
sqlcmd -S localhost -d AdventurePacksDb -E -i "Data\Scripts\001_InitialSchema.sql"
sqlcmd -S localhost -d AdventurePacksDb -E -i "Data\Scripts\002_MigrateFromAspNetIdentityToUsers.sql"
sqlcmd -S localhost -d AdventurePacksDb -E -i "Data\Scripts\Manual\004_VerifyMigration.sql"
# After verification:
sqlcmd -S localhost -d AdventurePacksDb -E -i "Data\Scripts\Manual\003_CleanupAspNetIdentityTables.sql"
```

Azure SQL example (your catalog: **adventuresapi-database**):

```powershell
sqlcmd -S adventuresapi-server.database.windows.net -d adventuresapi-database -U adventuresapi-server-admin -P "YOUR_PASSWORD" -i "Data\Scripts\001_InitialSchema.sql"
```

## Auto-run on app startup

`001` and `002` in `Data/Scripts/` are also applied when you `dotnet run` or deploy to Azure (see `SqlDatabaseMigrator`).

**Manual folder scripts are NOT auto-run** — you execute those yourself.

## Seed demo users (Azure / local)

Demo accounts use **BCrypt** passwords — seed via API, not raw SQL.

1. Set in `appsettings.json` or Azure App Settings:
   - `Seed:Enabled` = `true` (Azure: `Seed__Enabled`)
   - Optional: `Seed:DemoEmail`, `Seed:DemoPassword`, etc.
2. Start / restart the API.
3. Log in with:
   - `demo@adventurepacks.com` / `Adventure123!` (Free)
   - `premium@adventurepacks.com` / `Adventure123!` (Premium)
4. Set `Seed:Enabled` = `false` after first run in production.

Full Azure connection string list: **`docs/AZURE_SETUP.md`**

## Passwords after migration

Users copied from `AspNetUsers` keep the same `PasswordHash`.  
- If you used **ASP.NET Identity** password hashes, login may fail because the API now uses **BCrypt**.  
- In that case: have users **register again** on a fresh DB, or reset passwords via a one-time admin script.

If you only ever registered **after** the Dapper auth change, no action is needed.

## Fresh database (no EF history)

```powershell
sqlcmd -S localhost -d AdventurePacksDb -E -i "Data\Scripts\001_InitialSchema.sql"
```

Then start the API.
