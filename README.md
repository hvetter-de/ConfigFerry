# ConfigFerry

WinUI 3 desktop tool (.NET 10) that ferries the configuration of an Azure App Service / Function App down to your
machine, as an `appsettings.json` or an Azure Functions `local.settings.json`.

## Features

- **Microsoft Entra ID sign-in** (system browser, persisted token cache, silent re-sign-in on next start).
- **Source:** tenant dropdown (switch directories; one cached session per tenant) → subscription dropdown → dropdown of all Web Apps and Function Apps in that subscription.
- **Target 1 – JSON:** generate the file content and copy it to the clipboard.
- **Target 2 – existing file:** pick a file from disk; Azure values are merged in (**Azure wins**), everything else in the
  file is kept. Preview first, then *Save to file* (optional `.bak`, refuses to save if the file changed meanwhile).
- **Format selection** (radio buttons): `appsettings.json` (nested) vs. `local.settings.json` (flat `Values`, `ConnectionStrings`, `Host`
  and `IsEncrypted` preserved). The format is suggested from the app kind / file name.
- **Key Vault references** (`@Microsoft.KeyVault(SecretUri=…)` / `(VaultName=…;SecretName=…)`) are resolved with the
  signed-in user's token. Unreadable secrets stay as the reference and are reported as warnings.
- Azure setting keys (`A:B`, `A__B`, `A__0`) become nested objects/arrays for appsettings and `A__B` for functions.
- Platform settings (`WEBSITE_*`, `SCM_*`, …) are excluded by default.

### Merge semantics

| Situation | Result |
|---|---|
| Key only local | kept |
| Key only in Azure | added |
| Same key (case-insensitive) | Azure value wins, local casing kept |
| Local value is a number/boolean and Azure string parses as such | local JSON type kept |
| Arrays | merged by index (like .NET configuration layering) |
| Comments / trailing commas in the existing file | not preserved (a warning is shown) |

## Requirements

- Windows 10 (1809+) or Windows 11; .NET 10 SDK to build.

### Signed-in account

- Read access to the app's configuration (`Microsoft.Web/sites/config/list/action`, e.g. *Website Contributor*).
- For Key Vault references: `get` permission on secrets (RBAC *Key Vault Secrets User* or an access policy).

## Build & test

```powershell
dotnet build ConfigFerry.slnx
dotnet test  tests/ConfigFerry.Core.Tests
dotnet run   --project src/ConfigFerry.App
```

## Structure

| Project / namespace | Purpose |
|---|---|
| `ConfigFerry.Core` | UI-free logic: `.Models`, `.Configuration` (tree builder, merger, generator), `.Azure` (auth, ARM, Key Vault), `.Abstractions`, `.ViewModels` |
| `ConfigFerry.App` | WinUI 3 shell (unpackaged, self-contained): views, file picker / clipboard services, DI composition root |
| `ConfigFerry.Core.Tests` | xUnit + NSubstitute tests for the merge engine, Key Vault resolution and the view model |

Known limit: deployment slots are not listed.

## Security note

The generated files contain real secrets (app settings, connection strings, resolved Key Vault values). Do not commit
them: `local.settings.json` and `*.bak` files are git-ignored by default, but check any `appsettings*.json` you merge
into. Sign-in tokens are kept in a per-user, DPAPI-protected cache; the app stores no credentials itself.

## License

[GNU Affero General Public License v3.0](LICENSE)

