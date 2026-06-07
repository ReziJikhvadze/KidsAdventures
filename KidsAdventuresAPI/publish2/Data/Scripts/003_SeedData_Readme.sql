/*
  003_SeedData_Readme.sql
  -----------------------
  Demo users are NOT inserted here (passwords use BCrypt in the API).

  Use App Settings instead:
    Seed__Enabled = true
    Seed__DemoEmail = demo@adventurepacks.com
    Seed__DemoPassword = Adventure123!

  Restart the API once. Then set Seed__Enabled = false.

  See: docs/AZURE_SETUP.md
*/

PRINT 'Use API Seed settings (Seed__Enabled) — see docs/AZURE_SETUP.md';
GO
