# Microsoft Store packaging (development)

This is a **prototype** for Windows x64 MSIX distribution, separate from the current portable ZIP. Publishing to the Store requires a developer account and a reserved product identity.

## Builds

The dedicated GitHub Actions workflow `.github/workflows/microsoft-store-msix.yml` is configured to build the unsigned Store candidate on a successful push to `main`. On pull requests targeting `main`, it tests the code without producing a release package. **This workflow is staged on experimental until the stable-promotion PR is approved and merged.** Development channels use a separate portable ZIP workflow instead.

- `BasicsAdvancedTimer-MSIX-Store` contains **only** `BasicsAdvancedTimer-Store-unsigned.msix`, an unsigned package using the Partner Center identity. This is the file to upload under **Packages** in the Store submission.
- The test-only signed `BasicsAdvancedTimer-Dev.msix` and public certificate may be built locally for development as described below. Neither belongs in Partner Center. The private signing key is never exported.

## Safe local installation test

1. Make a backup of your existing `migration-data` folder (including `timer.db`, settings, and `Backups`) while the portable timer is closed.
2. On a Windows development PC, run `./migration/packaging/build-msix.ps1 -BuildNumber 1` and use the signed development files it creates. This needs .NET 8 and the Windows SDK.
3. On a Windows test machine, in an **administrator** PowerShell terminal (Windows only trusts a sideloaded package whose certificate is in the machine store) run:
   ```powershell
   Import-Certificate -FilePath .\BasicsAdvancedTimer-Dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   Add-AppxPackage -Path .\BasicsAdvancedTimer-Dev.msix
   ```
4. Launch **Basic's Advanced Timer** from the Start menu. Confirm the tray icon, account switching, persistence across restart, timeline editing, undo, CSV export, and data backups.
5. Check **Settings > Apps > Startup** (or Task Manager's Startup apps tab) after launching once. The packaged startup task is disabled by default. The right-click menu's **Start with Windows** checkbox turns that task on and off through the Windows startup-task API (the registry entry used by the portable build would be virtualised and never run). If you switch it off in Windows Settings, the app cannot switch it back on and says so; turn it on there instead.
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

The packaging script publishes the Avalonia app with a production-style executable name, creates a manifest and temporary logo assets, assembles the MSIX with MakeAppx, and checks that required SQLite DLLs are packaged. It signs with a temporary certificate only for local dev builds. Experimental and alpha ZIP workflows remain independent.

## Moving to the Microsoft Store

When a developer account is available:

1. The Partner Center **Product identity** for this app is already configured in the CI workflow:
   - `Package/Identity/Name`: `Basically.BasicsAdvancedTimer`
   - `Package/Identity/Publisher`: `CN=BF422802-B4CA-4B00-A09A-2EDA3315C0DE`
   - `Package/Properties/PublisherDisplayName`: `Basically`
2. Following approved promotion into `main`, download `BasicsAdvancedTimer-MSIX-Store` from a **successful main GitHub Actions run**; extract its ZIP and upload **only** `BasicsAdvancedTimer-Store-unsigned.msix` to the Store submission's **Packages** section. Do not upload `.cer` files.
3. To build that candidate locally on Windows (the Store signs the submitted MSIX):
   ```powershell
   ./migration/packaging/build-msix.ps1 -BuildNumber 2 -OutputDirectory artifacts/msix -UnsignedForStore -PackageName 'Basically.BasicsAdvancedTimer' -Publisher 'CN=BF422802-B4CA-4B00-A09A-2EDA3315C0DE' -PublisherDisplayName 'Basically'
   ```
4. Replace the generated **placeholder** icons with production artwork and run the Windows App Certification Kit. Confirm display-name reservation, screenshots, listing text, privacy/support details, licensing, data portability and update behaviour before submission.
5. Submit the final package in Partner Center only after testing. The Store signs Store-distributed MSIX packages; it **does not sign** unsigned portable EXEs.

The `runFullTrust` restricted capability warning may continue to appear during Partner Center validation: the packaged full-trust Avalonia desktop app needs this capability, which Microsoft may require you to justify during certification.

### Outstanding before release

- Install and run a development MSIX on Windows (the CI runner validates packaging but **cannot** replace a user-level launch and interaction test).
- Verify the startup task (the menu checkbox, the Windows Settings switch and a real sign-in), filesystem redirection, exports and automatic updates on the supported Windows version.
- Decide on an in-app **Import portable data** workflow before encouraging existing users to switch, with consent, backup, and collision checks.
- Replace prototype icon artwork and check the Store certification report; the actual Store identity is now configured in CI.
