# Microsoft Store packaging (development)

This is a **prototype** for Windows x64 MSIX distribution, separate from the current portable ZIP. Publishing to the Store requires a developer account and a reserved product identity.

## Builds

The GitHub Actions workflow `.github/workflows/microsoft-store-msix.yml` runs unit tests on Windows and builds the development package on every run. The Store candidate is built only when you start the workflow by hand (Actions, Microsoft Store MSIX packaging, Run workflow, tick the Store candidate box), so choose the branch or commit you have tested. It creates two **separate** artifacts:

- `BasicsAdvancedTimer-MSIX-dev` contains a temporary self-signed testing MSIX **and** the public development certificate. **Do not upload either file to Partner Center.**
- `BasicsAdvancedTimer-MSIX-Store` contains **only** `BasicsAdvancedTimer-Store-unsigned.msix`, an unsigned package with the Store product identity (see below). This is the file to upload under **Packages** in the Store submission.

The development artifact contains:

- `BasicsAdvancedTimer-Dev.msix`, a temporary **self-signed** installable development package.
- `BasicsAdvancedTimer-Dev.cer`, the corresponding **public** test certificate. The private key is never uploaded.

This MSIX uses the **development-only** package identity `BasicsAdvancedTimer.Dev`. It is not a Store submission and is not signed by Microsoft. A unique temporary certificate is created for each CI run, so an update may require trusting that run's certificate. Do not treat temporary developer certificates as production signing.

## Safe local installation test

1. Make a backup of your existing `migration-data` folder (including `timer.db`, settings, and `Backups`) while the portable timer is closed.
2. Download and extract the `BasicsAdvancedTimer-MSIX-dev` artifact from a **successful** workflow run.
3. On a Windows test machine, in an **administrator** PowerShell terminal (Windows only trusts a sideloaded package whose certificate is in the machine store) run:
   ```powershell
   Import-Certificate -FilePath .\BasicsAdvancedTimer-Dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   Add-AppxPackage -Path .\BasicsAdvancedTimer-Dev.msix
   ```
4. Launch **Basic's Advanced Timer** from the Start menu. Confirm the tray icon, account switching, persistence across restart, timeline editing, undo, CSV export, and data backups.
5. Check **Settings > Apps > Startup** (or Task Manager's Startup apps tab) after launching once. The packaged startup task is disabled by default. In-app registry startup is intentionally hidden in MSIX builds.
6. Back up any data created in the installed MSIX **before uninstalling it**; Windows may delete package-local state on uninstall. When done testing, uninstall the test package and remove the *temporary* certificate from the Local Computer Trusted People store (`certlm.msc`). Do not leave temporary development certificates trusted unnecessarily.

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

1. The Partner Center **Product identity** for this app is already configured in the CI workflow:
   - `Package/Identity/Name`: `Basically.BasicsAdvancedTimer`
   - `Package/Identity/Publisher`: `CN=BF422802-B4CA-4B00-A09A-2EDA3315C0DE`
   - `Package/Properties/PublisherDisplayName`: `Basically`
2. Download the `BasicsAdvancedTimer-MSIX-Store` artifact from a **successful** GitHub Actions workflow run; extract its ZIP and upload **only** `BasicsAdvancedTimer-Store-unsigned.msix` to the Store submission's **Packages** section. Do not upload `.cer` files.
3. To build that candidate locally on Windows (the Store signs the submitted MSIX):
   ```powershell
   ./migration/packaging/build-msix.ps1 -BuildNumber 2 -OutputDirectory artifacts/msix -UnsignedForStore -PackageName 'Basically.BasicsAdvancedTimer' -Publisher 'CN=BF422802-B4CA-4B00-A09A-2EDA3315C0DE' -PublisherDisplayName 'Basically'
   ```
4. Replace the generated **placeholder** icons with production artwork and run the Windows App Certification Kit. Confirm display-name reservation, screenshots, listing text, privacy/support details, licensing, data portability and update behaviour before submission.
5. Submit the final package in Partner Center only after testing. The Store signs Store-distributed MSIX packages; it **does not sign** unsigned portable EXEs.

The `runFullTrust` restricted capability warning may continue to appear during Partner Center validation: the packaged full-trust Avalonia desktop app needs this capability, which Microsoft may require you to justify during certification.

### Outstanding before release

- Install and run a development MSIX on Windows (the CI runner validates packaging but **cannot** replace a user-level launch and interaction test).
- Verify startup task display/enablement, filesystem redirection, exports and automatic updates on the supported Windows version.
- Decide on an in-app **Import portable data** workflow before encouraging existing users to switch, with consent, backup, and collision checks.
- Replace prototype icon artwork and check the Store certification report; the actual Store identity is now configured in CI.
