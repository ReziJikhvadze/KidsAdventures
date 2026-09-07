# Production configuration — complete file (2026-09-07)

One file, every key the API reads, with the values that make production behave like the working local
setup. Secrets are `<SET-IN-APP-SERVICE>`; URLs with `<…>` need the real host. Source of truth for the
keys: `KidsAdventuresAPI/Configuration/Options/*.cs` and `appsettings.json` at commit `abcb57f`.

**Why production behaved differently:** `appsettings.Production.example.json` never had a `Beki`
section (nor `Gemini`, `BekiPrintLayout`, `LocalBlobStorage`, `Admin`), so `Beki:Enabled`,
`Beki:BookFormatEnabled` and `Beki:CompositePipelineEnabled` stayed at their committed default
`false` on the server, and the story/provider models differed from the local user-secrets.

## How to apply

The deploy deletes `appsettings.Production.json` from the artifact, so production values live in the
App Service. Two equivalent forms below:

- **A. App Service → Configuration → Advanced edit**: paste section B's JSON array (it is the complete
  set; entries replace existing ones by name). Keys use `__` as the separator; arrays are `Key__0`, `Key__1`.
- **B. Hierarchical JSON** (for review, or for a self-hosted server that keeps an
  `appsettings.Production.json`): section C.

Non-negotiable differences from local: `Seed:Enabled=false`, `Stripe:BypassPayment=false`,
`LocalBlobStorage:Enabled=false`, `Swagger:Enabled=false`, `Cors:AllowLocalhostFallback=false`,
`PasswordlessAuth:ExposeSecretsInResponse=false`, `OpenAI:LogPrompts=false`.
Host dependencies (not settings): `gs`, `pdftoppm`, `pdffonts` on the API host; Node 22 only if
`Frontend:EnableHostedNode=true`.

## A. App Service settings (Advanced edit JSON) — 237 entries

```json
[
  {
    "name": "ConnectionStrings__DefaultConnection",
    "value": "Server=tcp:adventuresapi-server.database.windows.net,1433;Initial Catalog=adventuresapi-database;User ID=<SET>;Password=<SET>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;",
    "slotSetting": false
  },
  {
    "name": "Swagger__Enabled",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "RateLimiting__PermitLimitPerMinute",
    "value": "500",
    "slotSetting": false
  },
  {
    "name": "RateLimiting__DisableForLocalhost",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "Cors__AllowLocalhostFallback",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "Cors__AllowedOrigins__0",
    "value": "https://beki.ge",
    "slotSetting": false
  },
  {
    "name": "Cors__AllowedOrigins__1",
    "value": "https://www.beki.ge",
    "slotSetting": false
  },
  {
    "name": "Cors__AllowedOrigins__2",
    "value": "https://<frontend-app>.polandcentral-01.azurewebsites.net",
    "slotSetting": false
  },
  {
    "name": "Jwt__Issuer",
    "value": "AdventurePacks.Api",
    "slotSetting": false
  },
  {
    "name": "Jwt__Audience",
    "value": "AdventurePacks.Client",
    "slotSetting": false
  },
  {
    "name": "Jwt__SecretKey",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "Jwt__ExpirationMinutes",
    "value": "120",
    "slotSetting": false
  },
  {
    "name": "ClientIp__TrustedProxyHops",
    "value": "1",
    "slotSetting": false
  },
  {
    "name": "Admin__SuperAdminEmails__0",
    "value": "<admin-email@beki.ge>",
    "slotSetting": false
  },
  {
    "name": "Providers__Story",
    "value": "Gemini",
    "slotSetting": false
  },
  {
    "name": "Providers__Images",
    "value": "OpenAI",
    "slotSetting": false
  },
  {
    "name": "Providers__StoryPolish",
    "value": "OpenAI",
    "slotSetting": false
  },
  {
    "name": "OpenAI__ApiKey",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "OpenAI__Model",
    "value": "gpt-4.1-mini",
    "slotSetting": false
  },
  {
    "name": "OpenAI__MasterStoryModel",
    "value": "gpt-5.6-sol",
    "slotSetting": false
  },
  {
    "name": "OpenAI__MasterStoryReasoningEffort",
    "value": "high",
    "slotSetting": false
  },
  {
    "name": "OpenAI__StoryPromptVersion",
    "value": "v1",
    "slotSetting": false
  },
  {
    "name": "OpenAI__LogPrompts",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "OpenAI__BaseUrl",
    "value": "https://api.openai.com/v1",
    "slotSetting": false
  },
  {
    "name": "OpenAI__ImageGenerationProvider",
    "value": "responses",
    "slotSetting": false
  },
  {
    "name": "OpenAI__ImageModel",
    "value": "gpt-image-2",
    "slotSetting": false
  },
  {
    "name": "OpenAI__ImageEditModel",
    "value": "gpt-image-2",
    "slotSetting": false
  },
  {
    "name": "OpenAI__ImageSize",
    "value": "1024x1536",
    "slotSetting": false
  },
  {
    "name": "OpenAI__ImageQuality",
    "value": "medium",
    "slotSetting": false
  },
  {
    "name": "OpenAI__ImagePhotoQuality",
    "value": "medium",
    "slotSetting": false
  },
  {
    "name": "OpenAI__ImageInputFidelity",
    "value": "high",
    "slotSetting": false
  },
  {
    "name": "OpenAI__EnableStoryImages",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "OpenAI__IllustrationPacingSeconds",
    "value": "5",
    "slotSetting": false
  },
  {
    "name": "OpenAI__IllustrationMaxParallel",
    "value": "2",
    "slotSetting": false
  },
  {
    "name": "OpenAI__IllustrationStaggerSeconds",
    "value": "2",
    "slotSetting": false
  },
  {
    "name": "OpenAI__ImageRetryAttempts",
    "value": "3",
    "slotSetting": false
  },
  {
    "name": "OpenAI__ImageRetryBackoffSeconds",
    "value": "3",
    "slotSetting": false
  },
  {
    "name": "OpenAI__TimeoutMinutes",
    "value": "6",
    "slotSetting": false
  },
  {
    "name": "OpenAI__ImageTimeoutMinutes",
    "value": "3",
    "slotSetting": false
  },
  {
    "name": "OpenAI__StoryRetryAttempts",
    "value": "3",
    "slotSetting": false
  },
  {
    "name": "OpenAI__StoryRetryBackoffSeconds",
    "value": "4",
    "slotSetting": false
  },
  {
    "name": "Gemini__ApiKey",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "Gemini__BaseUrl",
    "value": "https://generativelanguage.googleapis.com/v1beta",
    "slotSetting": false
  },
  {
    "name": "Gemini__StoryModel",
    "value": "gemini-3.6-flash",
    "slotSetting": false
  },
  {
    "name": "Gemini__ImageModel",
    "value": "gemini-3.1-flash-image",
    "slotSetting": false
  },
  {
    "name": "Gemini__VisionModel",
    "value": "gemini-3.6-flash",
    "slotSetting": false
  },
  {
    "name": "Gemini__ImageSize",
    "value": "2K",
    "slotSetting": false
  },
  {
    "name": "Gemini__TimeoutMinutes",
    "value": "6",
    "slotSetting": false
  },
  {
    "name": "Gemini__ImageTimeoutMinutes",
    "value": "4",
    "slotSetting": false
  },
  {
    "name": "Gemini__RetryAttempts",
    "value": "3",
    "slotSetting": false
  },
  {
    "name": "Gemini__RetryBackoffSeconds",
    "value": "5",
    "slotSetting": false
  },
  {
    "name": "Beki__Enabled",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "Beki__BookFormatEnabled",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "Beki__CompositePipelineEnabled",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "Beki__ContinuationBaseUrl",
    "value": "https://beki.ge",
    "slotSetting": false
  },
  {
    "name": "Beki__VisualScenarioModel",
    "value": "",
    "slotSetting": false
  },
  {
    "name": "Beki__StoryGeneratorModel",
    "value": "gpt-5.6-luna",
    "slotSetting": false
  },
  {
    "name": "Beki__StoryReviewerModel",
    "value": "gpt-5.6-luna",
    "slotSetting": false
  },
  {
    "name": "Beki__StoryRepairModel",
    "value": "gpt-5.6-luna",
    "slotSetting": false
  },
  {
    "name": "Beki__MaxRepairAttempts",
    "value": "1",
    "slotSetting": false
  },
  {
    "name": "Beki__StoryTimeoutSeconds",
    "value": "300",
    "slotSetting": false
  },
  {
    "name": "Beki__IdentityAnalyzerModel",
    "value": "gpt-5.6-luna",
    "slotSetting": false
  },
  {
    "name": "Beki__PortraitGateEnabled",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "Beki__PortraitGateModel",
    "value": "",
    "slotSetting": false
  },
  {
    "name": "Beki__PortraitGateTimeoutSeconds",
    "value": "10",
    "slotSetting": false
  },
  {
    "name": "Beki__VisualBibleModel",
    "value": "gpt-5.6-luna",
    "slotSetting": false
  },
  {
    "name": "Beki__VisualPromptModel",
    "value": "gpt-5.6-luna",
    "slotSetting": false
  },
  {
    "name": "Beki__VisualReviewerModel",
    "value": "gpt-5.6-luna",
    "slotSetting": false
  },
  {
    "name": "Beki__ImageModel",
    "value": "gpt-image-2",
    "slotSetting": false
  },
  {
    "name": "Beki__InteriorAspectRatio",
    "value": "2:3",
    "slotSetting": false
  },
  {
    "name": "Beki__CoverAspectRatio",
    "value": "2:3",
    "slotSetting": false
  },
  {
    "name": "Beki__InteriorImageSize",
    "value": "1024x1536",
    "slotSetting": false
  },
  {
    "name": "Beki__CoverImageSize",
    "value": "1024x1536",
    "slotSetting": false
  },
  {
    "name": "Beki__SpreadImageSize",
    "value": "1536x1024",
    "slotSetting": false
  },
  {
    "name": "Beki__CoverWrapImageSize",
    "value": "1536x1024",
    "slotSetting": false
  },
  {
    "name": "Beki__AllowExperimentalImageSizes",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "Beki__AnchorImageQuality",
    "value": "high",
    "slotSetting": false
  },
  {
    "name": "Beki__CoverImageQuality",
    "value": "high",
    "slotSetting": false
  },
  {
    "name": "Beki__PageImageQuality",
    "value": "medium",
    "slotSetting": false
  },
  {
    "name": "Beki__PageConcurrency",
    "value": "2",
    "slotSetting": false
  },
  {
    "name": "Beki__PageStaggerSeconds",
    "value": "2",
    "slotSetting": false
  },
  {
    "name": "Beki__MaxPageRepairAttempts",
    "value": "1",
    "slotSetting": false
  },
  {
    "name": "Beki__MaxPageRegenerationAttempts",
    "value": "1",
    "slotSetting": false
  },
  {
    "name": "Beki__SpreadConcurrency",
    "value": "4",
    "slotSetting": false
  },
  {
    "name": "Beki__SpreadRegenerationAttempts",
    "value": "0",
    "slotSetting": false
  },
  {
    "name": "Beki__GenerationBudgetMinutes",
    "value": "45",
    "slotSetting": false
  },
  {
    "name": "Beki__PressBudgetMinutes",
    "value": "15",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__Mode",
    "value": "deterministic_lanczos",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__UpscalerPath",
    "value": "",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__UpscalerArgsTemplate",
    "value": "",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__GhostscriptPath",
    "value": "gs",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__PopplerPdftoppmPath",
    "value": "pdftoppm",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__PopplerPdffontsPath",
    "value": "pdffonts",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__RenderDpi",
    "value": "120",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__RequireAllCmyk",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__OutputIntentIccPath",
    "value": "Assets/BekiComposite/print/BEKI_Coated_FOGRA39_OutputIntent.icc",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__OutputIntentIccSha256",
    "value": "b35713ef7eff09349d4c3249e5f377736d06d8a2671c54712971a3546bf17c57",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__OutputConditionIdentifier",
    "value": "FOGRA39",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__OutputConditionInfo",
    "value": "FOGRA39L Coated",
    "slotSetting": false
  },
  {
    "name": "Beki__PrintPrep__RegistryName",
    "value": "https://www.color.org",
    "slotSetting": false
  },
  {
    "name": "Beki__ReviewThresholds__HeroIdentityMatch",
    "value": "0.8",
    "slotSetting": false
  },
  {
    "name": "Beki__ReviewThresholds__HeroAgeMatch",
    "value": "0.9",
    "slotSetting": false
  },
  {
    "name": "Beki__ReviewThresholds__HeroOutfitMatch",
    "value": "0.9",
    "slotSetting": false
  },
  {
    "name": "Beki__ReviewThresholds__BekiDesignMatch",
    "value": "0.9",
    "slotSetting": false
  },
  {
    "name": "Beki__ReviewThresholds__CharacterCountCorrect",
    "value": "0.95",
    "slotSetting": false
  },
  {
    "name": "Beki__ReviewThresholds__ChildVisualDominance",
    "value": "0.85",
    "slotSetting": false
  },
  {
    "name": "Beki__ReviewThresholds__SceneActionMatch",
    "value": "0.85",
    "slotSetting": false
  },
  {
    "name": "Beki__ReviewThresholds__TextSafeArea",
    "value": "0.8",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__SpreadWidthMm",
    "value": "440",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__SpreadHeightMm",
    "value": "200",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__BleedMm",
    "value": "5",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__SafeMarginMm",
    "value": "12",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__GutterZoneMm",
    "value": "30",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__FoldSafetyMm",
    "value": "10",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__WashPaddingMm",
    "value": "7",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__WashCornerRadiusMm",
    "value": "4",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__StoryPanelInkHex",
    "value": "FFF8EB",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__StoryPanelOpacity",
    "value": "0.86",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__MaxPrintUpscale",
    "value": "1.05",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__TextColumnShare",
    "value": "0.33",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__StoryFontSize",
    "value": "18",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__StoryLeadingPt",
    "value": "27",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__MaxTextWidthMm",
    "value": "170",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__PrintEnglishToo",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__TextOutlineWidth",
    "value": "0",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__TextOutlineWidthFactor",
    "value": "0",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__TextOutlineSteps",
    "value": "1",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__CoverTitleOutlineWidthPt",
    "value": "1.5",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__PrintCropTolerance",
    "value": "0.04",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__ContinuationQrCaption",
    "value": "ამბავი გრძელდება",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__ReviewQrUrl",
    "value": "",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__EndingLine",
    "value": "",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__EndingQrCaption",
    "value": "",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__IntroBelongsTemplate",
    "value": "ეს წიგნი ეკუთვნის {name_dative}",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__IntroAgeTemplate",
    "value": "{age} წლის",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__IntroThemeTemplate",
    "value": "„{world}“",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__CreditsLine",
    "value": "BEKI · beki.ge",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__StoryFontSizeAges2To4",
    "value": "20",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__StoryFontSizeAges5To8",
    "value": "18",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__StoryFontSizeLadderPt__0",
    "value": "20",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__StoryFontSizeLadderPt__1",
    "value": "18",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__StoryFontSizeLadderPt__2",
    "value": "16",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__StoryFontSizeLadderPt__3",
    "value": "14",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__CoverTitleSizeLadderPt__0",
    "value": "36",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__CoverTitleSizeLadderPt__1",
    "value": "32",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__CoverTitleSizeLadderPt__2",
    "value": "28",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__CoverTitleSizeLadderPt__3",
    "value": "24",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__CoverTitleSizeLadderPt__4",
    "value": "20",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__PrintTargetPpi",
    "value": "300",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__PrintAssetJpegQuality",
    "value": "95",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__ScreenTargetPpi",
    "value": "150",
    "slotSetting": false
  },
  {
    "name": "BekiPrintLayout__ScreenAssetJpegQuality",
    "value": "90",
    "slotSetting": false
  },
  {
    "name": "PrintLayout__TrimWidthMm",
    "value": "148",
    "slotSetting": false
  },
  {
    "name": "PrintLayout__TrimHeightMm",
    "value": "210",
    "slotSetting": false
  },
  {
    "name": "PrintLayout__BleedMm",
    "value": "3",
    "slotSetting": false
  },
  {
    "name": "PrintLayout__SafeMarginMm",
    "value": "10",
    "slotSetting": false
  },
  {
    "name": "PrintLayout__GutterMm",
    "value": "8",
    "slotSetting": false
  },
  {
    "name": "PrintLayout__BindingMultiple",
    "value": "4",
    "slotSetting": false
  },
  {
    "name": "PrintLayout__IncludeCoverInInterior",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "PrintLayout__IncludeBackCover",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "AzureBlobStorage__ConnectionString",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "AzureBlobStorage__ContainerName",
    "value": "adventurepacks",
    "slotSetting": false
  },
  {
    "name": "LocalBlobStorage__Enabled",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "LocalBlobStorage__RootPath",
    "value": ".localblob",
    "slotSetting": false
  },
  {
    "name": "Frontend__EnableHostedNode",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "Frontend__NodePort",
    "value": "3099",
    "slotSetting": false
  },
  {
    "name": "Frontend__OutputRelativePath",
    "value": "wwwroot/azure-ssr",
    "slotSetting": false
  },
  {
    "name": "Email__Enabled",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "Email__FromAddress",
    "value": "info@beki.ge",
    "slotSetting": false
  },
  {
    "name": "Email__FromName",
    "value": "Beki",
    "slotSetting": false
  },
  {
    "name": "Email__SmtpHost",
    "value": "smtp.gmail.com",
    "slotSetting": false
  },
  {
    "name": "Email__SmtpPort",
    "value": "587",
    "slotSetting": false
  },
  {
    "name": "Email__SmtpUser",
    "value": "info@beki.ge",
    "slotSetting": false
  },
  {
    "name": "Email__SmtpPassword",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "Email__BaseUrl",
    "value": "https://beki.ge",
    "slotSetting": false
  },
  {
    "name": "Email__ApiBaseUrl",
    "value": "https://adventuresapi-guajeacbcucsbwau.polandcentral-01.azurewebsites.net",
    "slotSetting": false
  },
  {
    "name": "Email__ContactToAddress",
    "value": "info@beki.ge",
    "slotSetting": false
  },
  {
    "name": "Email__AdminNotificationAddress",
    "value": "<admin-email@beki.ge>",
    "slotSetting": false
  },
  {
    "name": "PasswordlessAuth__Enabled",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "PasswordlessAuth__OtpLength",
    "value": "4",
    "slotSetting": false
  },
  {
    "name": "PasswordlessAuth__OtpLifetimeMinutes",
    "value": "10",
    "slotSetting": false
  },
  {
    "name": "PasswordlessAuth__MagicLinkLifetimeMinutes",
    "value": "20",
    "slotSetting": false
  },
  {
    "name": "PasswordlessAuth__MaxVerifyAttempts",
    "value": "5",
    "slotSetting": false
  },
  {
    "name": "PasswordlessAuth__ResendCooldownSeconds",
    "value": "45",
    "slotSetting": false
  },
  {
    "name": "PasswordlessAuth__MaxRequestsPerDestinationPerHour",
    "value": "6",
    "slotSetting": false
  },
  {
    "name": "PasswordlessAuth__MaxRequestsPerIpPerHour",
    "value": "30",
    "slotSetting": false
  },
  {
    "name": "PasswordlessAuth__ExposeSecretsInResponse",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "PasswordlessAuth__SigningKey",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "PasswordlessAuth__MagicLinkPath",
    "value": "/auth/magic",
    "slotSetting": false
  },
  {
    "name": "GoogleAuth__Enabled",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "GoogleAuth__ClientId",
    "value": "",
    "slotSetting": false
  },
  {
    "name": "GoogleMaps__Enabled",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "GoogleMaps__ApiKey",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "Recaptcha__Enabled",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "Recaptcha__SiteKey",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "Recaptcha__SecretKey",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "Recaptcha__MinimumScore",
    "value": "0.5",
    "slotSetting": false
  },
  {
    "name": "Seed__Enabled",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "Seed__DemoEmail",
    "value": "demo@adventurepacks.com",
    "slotSetting": false
  },
  {
    "name": "Seed__DemoPassword",
    "value": "<unused-when-disabled>",
    "slotSetting": false
  },
  {
    "name": "Seed__CreatePremiumDemoUser",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "Seed__PremiumDemoEmail",
    "value": "premium@adventurepacks.com",
    "slotSetting": false
  },
  {
    "name": "Stripe__Enabled",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "Stripe__BypassPayment",
    "value": "false",
    "slotSetting": false
  },
  {
    "name": "Stripe__SecretKey",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "Stripe__PublishableKey",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "Stripe__WebhookSecret",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "Stripe__DigitalPriceId",
    "value": "<price_id>",
    "slotSetting": false
  },
  {
    "name": "Stripe__PrintPriceId",
    "value": "<price_id>",
    "slotSetting": false
  },
  {
    "name": "Stripe__PrintUpgradePriceId",
    "value": "<price_id>",
    "slotSetting": false
  },
  {
    "name": "Stripe__SiteBaseUrl",
    "value": "https://beki.ge",
    "slotSetting": false
  },
  {
    "name": "Stripe__SuccessPath",
    "value": "/create",
    "slotSetting": false
  },
  {
    "name": "Stripe__CancelPath",
    "value": "/create",
    "slotSetting": false
  },
  {
    "name": "Stripe__EnableWallets",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "Stripe__AllowAdHocAmounts",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "Stripe__SessionExpiryMinutes",
    "value": "60",
    "slotSetting": false
  },
  {
    "name": "Bog__Enabled",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "Bog__ClientId",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "Bog__SecretKey",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "Bog__AuthUrl",
    "value": "https://oauth2.bog.ge/auth/realms/bog/protocol/openid-connect/token",
    "slotSetting": false
  },
  {
    "name": "Bog__ApiBaseUrl",
    "value": "https://api.bog.ge/payments/v1",
    "slotSetting": false
  },
  {
    "name": "Bog__SiteBaseUrl",
    "value": "https://beki.ge",
    "slotSetting": false
  },
  {
    "name": "Bog__CallbackUrl",
    "value": "https://adventuresapi-guajeacbcucsbwau.polandcentral-01.azurewebsites.net/api/payments/bog/webhook",
    "slotSetting": false
  },
  {
    "name": "Bog__SuccessPath",
    "value": "/create",
    "slotSetting": false
  },
  {
    "name": "Bog__CancelPath",
    "value": "/create",
    "slotSetting": false
  },
  {
    "name": "Bog__TtlMinutes",
    "value": "15",
    "slotSetting": false
  },
  {
    "name": "Bog__Language",
    "value": "ka",
    "slotSetting": false
  },
  {
    "name": "Bog__VerifyCallbackSignature",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "WifisherSms__Enabled",
    "value": "true",
    "slotSetting": false
  },
  {
    "name": "WifisherSms__BaseUrl",
    "value": "https://sms-api.wifisher.com/api/v2/",
    "slotSetting": false
  },
  {
    "name": "WifisherSms__ApiKey",
    "value": "<SET-IN-APP-SERVICE>",
    "slotSetting": false
  },
  {
    "name": "WifisherSms__Sender",
    "value": "<sender-id>",
    "slotSetting": false
  },
  {
    "name": "WifisherSms__TimeoutSeconds",
    "value": "20",
    "slotSetting": false
  },
  {
    "name": "Logging__LogLevel__Default",
    "value": "Information",
    "slotSetting": false
  },
  {
    "name": "Logging__LogLevel__Microsoft.AspNetCore",
    "value": "Warning",
    "slotSetting": false
  },
  {
    "name": "Logging__LogLevel__Hangfire",
    "value": "Information",
    "slotSetting": false
  },
  {
    "name": "AllowedHosts",
    "value": "*",
    "slotSetting": false
  }
]
```

## C. The same as appsettings.Production.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=tcp:adventuresapi-server.database.windows.net,1433;Initial Catalog=adventuresapi-database;User ID=<SET>;Password=<SET>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
  },
  "Swagger": {
    "Enabled": false
  },
  "RateLimiting": {
    "PermitLimitPerMinute": 500,
    "DisableForLocalhost": false
  },
  "Cors": {
    "AllowLocalhostFallback": false,
    "AllowedOrigins": [
      "https://beki.ge",
      "https://www.beki.ge",
      "https://<frontend-app>.polandcentral-01.azurewebsites.net"
    ]
  },
  "Jwt": {
    "Issuer": "AdventurePacks.Api",
    "Audience": "AdventurePacks.Client",
    "SecretKey": "<SET-IN-APP-SERVICE>",
    "ExpirationMinutes": 120
  },
  "ClientIp": {
    "TrustedProxyHops": 1
  },
  "Admin": {
    "SuperAdminEmails": [
      "<admin-email@beki.ge>"
    ]
  },
  "Providers": {
    "Story": "Gemini",
    "Images": "OpenAI",
    "StoryPolish": "OpenAI"
  },
  "OpenAI": {
    "ApiKey": "<SET-IN-APP-SERVICE>",
    "Model": "gpt-4.1-mini",
    "MasterStoryModel": "gpt-5.6-sol",
    "MasterStoryReasoningEffort": "high",
    "StoryPromptVersion": "v1",
    "LogPrompts": false,
    "BaseUrl": "https://api.openai.com/v1",
    "ImageGenerationProvider": "responses",
    "ImageModel": "gpt-image-2",
    "ImageEditModel": "gpt-image-2",
    "ImageSize": "1024x1536",
    "ImageQuality": "medium",
    "ImagePhotoQuality": "medium",
    "ImageInputFidelity": "high",
    "EnableStoryImages": true,
    "IllustrationPacingSeconds": 5,
    "IllustrationMaxParallel": 2,
    "IllustrationStaggerSeconds": 2,
    "ImageRetryAttempts": 3,
    "ImageRetryBackoffSeconds": 3,
    "TimeoutMinutes": 6,
    "ImageTimeoutMinutes": 3,
    "StoryRetryAttempts": 3,
    "StoryRetryBackoffSeconds": 4
  },
  "Gemini": {
    "ApiKey": "<SET-IN-APP-SERVICE>",
    "BaseUrl": "https://generativelanguage.googleapis.com/v1beta",
    "StoryModel": "gemini-3.6-flash",
    "ImageModel": "gemini-3.1-flash-image",
    "VisionModel": "gemini-3.6-flash",
    "ImageSize": "2K",
    "TimeoutMinutes": 6,
    "ImageTimeoutMinutes": 4,
    "RetryAttempts": 3,
    "RetryBackoffSeconds": 5
  },
  "Beki": {
    "Enabled": true,
    "BookFormatEnabled": true,
    "CompositePipelineEnabled": true,
    "ContinuationBaseUrl": "https://beki.ge",
    "VisualScenarioModel": "",
    "StoryGeneratorModel": "gpt-5.6-luna",
    "StoryReviewerModel": "gpt-5.6-luna",
    "StoryRepairModel": "gpt-5.6-luna",
    "MaxRepairAttempts": 1,
    "StoryTimeoutSeconds": 300,
    "IdentityAnalyzerModel": "gpt-5.6-luna",
    "PortraitGateEnabled": false,
    "PortraitGateModel": "",
    "PortraitGateTimeoutSeconds": 10,
    "VisualBibleModel": "gpt-5.6-luna",
    "VisualPromptModel": "gpt-5.6-luna",
    "VisualReviewerModel": "gpt-5.6-luna",
    "ImageModel": "gpt-image-2",
    "InteriorAspectRatio": "2:3",
    "CoverAspectRatio": "2:3",
    "InteriorImageSize": "1024x1536",
    "CoverImageSize": "1024x1536",
    "SpreadImageSize": "1536x1024",
    "CoverWrapImageSize": "1536x1024",
    "AllowExperimentalImageSizes": false,
    "AnchorImageQuality": "high",
    "CoverImageQuality": "high",
    "PageImageQuality": "medium",
    "PageConcurrency": 2,
    "PageStaggerSeconds": 2,
    "MaxPageRepairAttempts": 1,
    "MaxPageRegenerationAttempts": 1,
    "SpreadConcurrency": 4,
    "SpreadRegenerationAttempts": 0,
    "GenerationBudgetMinutes": 45,
    "PressBudgetMinutes": 15,
    "PrintPrep": {
      "Mode": "deterministic_lanczos",
      "UpscalerPath": "",
      "UpscalerArgsTemplate": "",
      "GhostscriptPath": "gs",
      "PopplerPdftoppmPath": "pdftoppm",
      "PopplerPdffontsPath": "pdffonts",
      "RenderDpi": 120,
      "RequireAllCmyk": true,
      "OutputIntentIccPath": "Assets/BekiComposite/print/BEKI_Coated_FOGRA39_OutputIntent.icc",
      "OutputIntentIccSha256": "b35713ef7eff09349d4c3249e5f377736d06d8a2671c54712971a3546bf17c57",
      "OutputConditionIdentifier": "FOGRA39",
      "OutputConditionInfo": "FOGRA39L Coated",
      "RegistryName": "https://www.color.org"
    },
    "ReviewThresholds": {
      "HeroIdentityMatch": 0.8,
      "HeroAgeMatch": 0.9,
      "HeroOutfitMatch": 0.9,
      "BekiDesignMatch": 0.9,
      "CharacterCountCorrect": 0.95,
      "ChildVisualDominance": 0.85,
      "SceneActionMatch": 0.85,
      "TextSafeArea": 0.8
    }
  },
  "BekiPrintLayout": {
    "SpreadWidthMm": 440,
    "SpreadHeightMm": 200,
    "BleedMm": 5,
    "SafeMarginMm": 12,
    "GutterZoneMm": 30,
    "FoldSafetyMm": 10,
    "WashPaddingMm": 7,
    "WashCornerRadiusMm": 4,
    "StoryPanelInkHex": "FFF8EB",
    "StoryPanelOpacity": 0.86,
    "MaxPrintUpscale": 1.05,
    "TextColumnShare": 0.33,
    "StoryFontSize": 18,
    "StoryLeadingPt": 27,
    "MaxTextWidthMm": 170,
    "PrintEnglishToo": false,
    "TextOutlineWidth": 0,
    "TextOutlineWidthFactor": 0,
    "TextOutlineSteps": 1,
    "CoverTitleOutlineWidthPt": 1.5,
    "PrintCropTolerance": 0.04,
    "ContinuationQrCaption": "ამბავი გრძელდება",
    "ReviewQrUrl": "",
    "EndingLine": "",
    "EndingQrCaption": "",
    "IntroBelongsTemplate": "ეს წიგნი ეკუთვნის {name_dative}",
    "IntroAgeTemplate": "{age} წლის",
    "IntroThemeTemplate": "„{world}“",
    "CreditsLine": "BEKI · beki.ge",
    "StoryFontSizeAges2To4": 20,
    "StoryFontSizeAges5To8": 18,
    "StoryFontSizeLadderPt": [
      20,
      18,
      16,
      14
    ],
    "PrintTargetPpi": 300,
    "PrintAssetJpegQuality": 95,
    "ScreenTargetPpi": 150,
    "ScreenAssetJpegQuality": 90
  },
  "PrintLayout": {
    "TrimWidthMm": 148,
    "TrimHeightMm": 210,
    "BleedMm": 3,
    "SafeMarginMm": 10,
    "GutterMm": 8,
    "BindingMultiple": 4,
    "IncludeCoverInInterior": true,
    "IncludeBackCover": true
  },
  "AzureBlobStorage": {
    "ConnectionString": "<SET-IN-APP-SERVICE>",
    "ContainerName": "adventurepacks"
  },
  "LocalBlobStorage": {
    "Enabled": false,
    "RootPath": ".localblob"
  },
  "Frontend": {
    "EnableHostedNode": false,
    "NodePort": 3099,
    "OutputRelativePath": "wwwroot/azure-ssr"
  },
  "Email": {
    "Enabled": true,
    "FromAddress": "info@beki.ge",
    "FromName": "Beki",
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": 587,
    "SmtpUser": "info@beki.ge",
    "SmtpPassword": "<SET-IN-APP-SERVICE>",
    "BaseUrl": "https://beki.ge",
    "ApiBaseUrl": "https://adventuresapi-guajeacbcucsbwau.polandcentral-01.azurewebsites.net",
    "ContactToAddress": "info@beki.ge",
    "AdminNotificationAddress": "<admin-email@beki.ge>"
  },
  "PasswordlessAuth": {
    "Enabled": true,
    "OtpLength": 4,
    "OtpLifetimeMinutes": 10,
    "MagicLinkLifetimeMinutes": 20,
    "MaxVerifyAttempts": 5,
    "ResendCooldownSeconds": 45,
    "MaxRequestsPerDestinationPerHour": 6,
    "MaxRequestsPerIpPerHour": 30,
    "ExposeSecretsInResponse": false,
    "SigningKey": "<SET-IN-APP-SERVICE>",
    "MagicLinkPath": "/auth/magic"
  },
  "GoogleAuth": {
    "Enabled": false,
    "ClientId": ""
  },
  "GoogleMaps": {
    "Enabled": true,
    "ApiKey": "<SET-IN-APP-SERVICE>"
  },
  "Recaptcha": {
    "Enabled": true,
    "SiteKey": "<SET-IN-APP-SERVICE>",
    "SecretKey": "<SET-IN-APP-SERVICE>",
    "MinimumScore": 0.5
  },
  "Seed": {
    "Enabled": false,
    "DemoEmail": "demo@adventurepacks.com",
    "DemoPassword": "<unused-when-disabled>",
    "CreatePremiumDemoUser": false,
    "PremiumDemoEmail": "premium@adventurepacks.com"
  },
  "Stripe": {
    "Enabled": true,
    "BypassPayment": false,
    "SecretKey": "<SET-IN-APP-SERVICE>",
    "PublishableKey": "<SET-IN-APP-SERVICE>",
    "WebhookSecret": "<SET-IN-APP-SERVICE>",
    "DigitalPriceId": "<price_id>",
    "PrintPriceId": "<price_id>",
    "PrintUpgradePriceId": "<price_id>",
    "SiteBaseUrl": "https://beki.ge",
    "SuccessPath": "/create",
    "CancelPath": "/create",
    "EnableWallets": true,
    "AllowAdHocAmounts": true,
    "SessionExpiryMinutes": 60
  },
  "Bog": {
    "Enabled": true,
    "ClientId": "<SET-IN-APP-SERVICE>",
    "SecretKey": "<SET-IN-APP-SERVICE>",
    "AuthUrl": "https://oauth2.bog.ge/auth/realms/bog/protocol/openid-connect/token",
    "ApiBaseUrl": "https://api.bog.ge/payments/v1",
    "SiteBaseUrl": "https://beki.ge",
    "CallbackUrl": "https://adventuresapi-guajeacbcucsbwau.polandcentral-01.azurewebsites.net/api/payments/bog/webhook",
    "SuccessPath": "/create",
    "CancelPath": "/create",
    "TtlMinutes": 15,
    "Language": "ka",
    "VerifyCallbackSignature": true
  },
  "WifisherSms": {
    "Enabled": true,
    "BaseUrl": "https://sms-api.wifisher.com/api/v2/",
    "ApiKey": "<SET-IN-APP-SERVICE>",
    "Sender": "<sender-id>",
    "TimeoutSeconds": 20
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Hangfire": "Information"
    }
  },
  "AllowedHosts": "*"
}
```

## D. Secrets to set (never commit)

`ConnectionStrings__DefaultConnection`, `Jwt__SecretKey`, `PasswordlessAuth__SigningKey` (different
from JWT), `OpenAI__ApiKey`, `Gemini__ApiKey` (required at startup because `Providers__Story=Gemini`),
`AzureBlobStorage__ConnectionString`, `Email__SmtpPassword`, `Stripe__SecretKey`,
`Stripe__PublishableKey`, `Stripe__WebhookSecret`, `Bog__ClientId`, `Bog__SecretKey`,
`WifisherSms__ApiKey`, `Recaptcha__SiteKey`, `Recaptcha__SecretKey`, `GoogleMaps__ApiKey`.
Rotation procedure: `KidsAdventuresAPI/docs/SECRETS_ROTATION.md`.

## E. Values chosen to mirror local (change only on purpose)

| Key | Value | Why |
|---|---|---|
| `Beki__Enabled` / `BookFormatEnabled` / `CompositePipelineEnabled` | true | the BEKI product; committed defaults are false |
| `Providers__StoryPolish` | OpenAI | local user-secrets |
| `Gemini__StoryModel` | gemini-3.6-flash | local user-secrets (committed default is gemini-3.1-pro-preview) |
| `OpenAI__MasterStoryModel` / `MasterStoryReasoningEffort` | gpt-5.6-sol / high | local user-secrets |
| `Beki__PrintPrep__Mode` | deterministic_lanczos | default; no external upscaler needed |
| `Beki__SpreadImageSize` / `CoverWrapImageSize` | 1536x1024 | default; larger frames are opt-in (see docs/CONFIGURATION.md §4) |
| `BekiPrintLayout__PrintAssetJpegQuality` | 95 | press rasters q95 4:4:4 |
| `BekiPrintLayout__CoverTitleOutlineWidthPt` | 1.5 | cover title rim |
| `Email__BaseUrl` / `Stripe__SiteBaseUrl` / `Bog__SiteBaseUrl` | https://beki.ge | public web origin (local is localhost) |
| `Email__ApiBaseUrl` / `Bog__CallbackUrl` | the API App Service host | from wwwroot/.env.production |
| `Stripe__Enabled` / `Bog__Enabled` / `WifisherSms__Enabled` / `Recaptcha__Enabled` / `GoogleMaps__Enabled` | true | as in the production example; set false for any provider not in use |

Frontend build variables (GitHub Actions, not App Service): `VITE_API_BASE_URL` = the API host,
`VITE_SITE_URL=https://beki.ge` (already in `wwwroot/.env.production`).
