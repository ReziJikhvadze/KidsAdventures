# Exposed credentials — rotation checklist

`KidsAdventuresAPI/appsettings.Production.json` was committed to this repository with live
credentials in it. It has now been removed from git tracking (`git rm --cached`) and added to
`.gitignore`, and the file itself is untouched on disk so local runs and deploys keep working.

**Untracking does not undo the exposure.** Every value below still sits in the git history and in
any clone, fork, or CI cache made from it. Treat all of them as compromised and rotate.

## What was exposed

| Credential | Where it lives | Rotate by |
| --- | --- | --- |
| Azure SQL admin password (`adventuresapi-server-admin`) | `ConnectionStrings:DefaultConnection` | Azure Portal → SQL server → Reset password |
| OpenAI API key (`sk-proj-…`) | `OpenAI:ApiKey` | platform.openai.com → API keys → revoke + create |
| Azure Storage account key | `AzureBlobStorage:ConnectionString` | Azure Portal → Storage account → Access keys → Rotate key1, then key2 |
| Gmail app password | `Email:SmtpPassword` | Google Account → Security → App passwords → revoke + regenerate |
| JWT signing key | `Jwt:SecretKey` | Generate a new 64-char random string. Note: this invalidates every issued token, so all users are signed out once. |
| Stripe secret key (`sk_test_…`) | `Stripe:SecretKey` | Test-mode key, lower risk, but roll it anyway in the Stripe dashboard |
| reCAPTCHA secret | `Recaptcha:SecretKey` | Google reCAPTCHA admin console |

Google OAuth `ClientId` and the Stripe `PublishableKey` are public by design — no action needed.

## Where the values should live instead

Azure App Service configuration, using the double-underscore form the .NET configuration binder
already understands:

```
ConnectionStrings__DefaultConnection
OpenAI__ApiKey
AzureBlobStorage__ConnectionString
Email__SmtpPassword
Jwt__SecretKey
Stripe__SecretKey
Stripe__WebhookSecret
Recaptcha__SecretKey
```

App settings set there override anything in `appsettings.Production.json`, so once they are in
place the on-disk file can be reduced to non-secret values only.

`appsettings.Production.example.json` stays tracked and is the template to copy from.

## Purging the history (optional, and disruptive)

Rotating is what actually protects you; purging only removes the old values from the repo. If you
also want them gone from history, `git filter-repo --path KidsAdventuresAPI/appsettings.Production.json --invert-paths`
rewrites every commit — which means force-pushing and every existing clone becoming invalid. Rotate
first regardless; decide on the purge afterwards.

## Also worth fixing

`Stripe:WebhookSecret` is currently empty. `OrderService.HandleStripeWebhookAsync` refuses to
process any webhook while it is blank, so today **no Stripe payment is confirmed by webhook** —
confirmation depends entirely on the customer's browser reaching the success page. Set the webhook
secret from the Stripe dashboard before taking real card payments.
