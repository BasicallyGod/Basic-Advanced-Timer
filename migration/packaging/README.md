# Microsoft Store packaging (development)

This is a **prototype** for Windows x64 MSIX distribution, separate from the current portable ZIP. No changes have been merged into `migration` or `main`; publishing to the Store requires a developer account and a reserved product identity.

## Builds

The GitHub Actions workflow `.github/workflows/microsoft-store-msix.yml` runs unit tests on Windows and creates `BasicsAdvancedTimer-MSIX-dev` containing:

- `BasicsAdvancedTimer-Dev.msix`, a temporary **self-signed** installable development package.
- `BasicsAdvancedTimer-Dev.cer`, the corresponding **public** test certificate. The private key is never uploaded.

This MSIX uses the **development-only** package identity `BasicsAdvancedTimer.Dev`. It is not a Store submission and is not signed by Microsoft. A unique temporary certificate is created for each CI run, so an update may require trusting that run's certificate. Do not treat temporary developer certificates as production signing.

## Safe local installation test

1. Make a backup of your existing `migration-data` folder (including `timer.db`, settings, and `Backups`) while the portable timer is closed.
2. Download and extract the `BasicsAdvancedTimer-MSIX-dev` artifact from a **successful** workflow run.
3. On a Windows test machine, in a PowerShell terminal run:
   ```powershell
   Import-Certificate -FilePath .\BasicsAdvancedTimer-Dev.cer -CertStoreLocation Cert:\CurrentUser\TrustedPeople
   Add-AppxPackage -Path .\BasicsAdvancedTimer-Dev.msix
   ```
4. Launch **Basic's Advanced Timer** from the Start menu. Confirm the tray icon, account switching, persistence across restart, timeline editing, undo, CSV export, and data backups.
5. Check **Settings > Apps > Startup** (or Task Manager's Startup apps tab) after launching once. The packaged startup task is disabled by default. In-app registry startup is intentionally hidden in MSIX builds.
6. When done testing, uninstall the test package and remove the *temporary* certificate from your Current User Trusted People store. Do not leave temporary development certificates trusted unnecessarily.

Do **not** expect a developer MSIX installation to be updated in place by the eventual Store version: Microsoft assigns a different package identity.

## Storage and migration

The portable ZIP **still** saves to `migration-data` next to its executable (or the `--data-dir` location you explicitly set).

An installed MSIX saves to its own writable per-user data area using `Environment.SpecialFolder.LocalApplicationData` and the subfolder `BasicsAdvancedTimer`. Windows may **virtualise** the physical path under `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\Local\`. The database, settings, backups, and CSV exports stay together.

**Data is not copied automatically.** Never point two running timers at the same SQLite database. Test with fresh data until the packaged app's exact data location has been confirmed, then copy your backed-up data *with both apps closed* if you deliberately want to migrate. Do not delete the old portable folder until you've confirmed all saved records and backups.

The standalone GitHub updater should **not** be used for an MSIX installation; updates will be distributed by the Store once published.

## Building on a Windows development machine

Requires the .NET 8 SDK and Windows 10/11 SDK tools `MakeAppx.exe` and `SignTool.exe`. From repository root:

```powershell
dotnet test migration/tests/Timer.Desktop.Tests/Timer.Desktop.Tests.csproj --configuration Release
./migration/packaging/build-msix.ps1 -BuildNumber 1 -OutputDirectory artifacts/msix
```

The packaging script publishes the Avalonia app with a production-style executable name, creates a manifest and temporary logo assets, assembles the MSIX with MakeAppx, checks that the database DLLs are packaged, and signs with a temporary certificate. The traditional preview workflow and published ZIP are unchanged.

## Moving to the Microsoft Store

When a developer account is available:

1. In [Partner Center](https://partner.microsoft.com/), create an **MSIX or PWA app**, reserve the product name, and open **Product management > Product identity**.
2. Copy the exact **Package/Identity/Name**, **Package/Identity/Publisher**, and **PublisherDisplayName**. These must match the submission's manifest; the current development values must not be used.
3. Build an **unsigned** Store candidate (the Store signs the submission). Example, replacing the placeholders with the exact Partner Center values:
   ```powershell
   ./migration/packaging/build-msix.ps1 -BuildNumber 2 -OutputDirectory artifacts/msix -UnsignedForStore -PackageName 'YOUR_PARTNER_CENTER_IDENTITY_NAME' -Publisher 'YOUR_PARTNER_CENTER_PUBLISHER' -PublisherDisplayName 'YOUR_PUBLISHER_DISPLAY_NAME'
   ```
4. Replace the generated **placeholder** icons with production artwork and run the Windows App Certification Kit. Confirm display-name reservation, screenshots, listing text, privacy/support details, licensing, data portability and update behaviour before submission.
5. Submit the final package in Partner Center. The Store signs Store-distributed MSIX packages, but it **does not sign** the self-signed development package or fix unsigned portable EXEs.

### Outstanding before release

- Install and run a development MSIX on Windows (the CI runner validates packaging but **cannot** replace a user-level launch and interaction test).
- Verify startup task display/enablement, filesystem redirection, exports and automatic updates on the supported Windows version.
- Decide on an in-app **Import portable data** workflow before encouraging existing users to switch, with consent, backup, and collision checks.
- Replace prototype icon artwork, supply the actual Store identity, and check the Store certification report.
