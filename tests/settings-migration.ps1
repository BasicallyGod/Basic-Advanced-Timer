# Regression tests for portable preference consolidation and legacy migration.
# Windows PowerShell 5.1. Uses disposable synthetic values, never personal files.
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'AccountTimer2.ps1'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if (@($errors).Count -gt 0) { throw "Application PowerShell parse errors: $($errors -join '; ')" }
$names = @('Save-SettingsStore', 'Test-SettingSection', 'Read-SettingSection',
           'Read-SettingFirst', 'Write-SettingSections', 'Write-SettingSection', 'Initialize-SettingsStore')
foreach ($name in $names) {
    $fn = @($ast.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($fn.Count -ne 1) { throw "Expected exactly one function $name, found $($fn.Count)" }
    Invoke-Expression $fn[0].Extent.Text
}
function Assert([bool]$condition, [string]$reason) { if (-not $condition) { throw $reason } }
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('basics-settings-test-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $fixtureRoot -Force)
try {
    $script:SettingsNonInteractive = $true
    $script:AppRoot = $fixtureRoot
    $script:SettingsStoreFile = Join-Path $fixtureRoot 'basics-advanced-timer.settings.json'
    $script:SettingsStoreVersion = 1
    $script:SettingsSections = @{}
    $script:LegacySettings = [ordered]@{
        size = 'account-timer-size.txt'
        accounts = 'account-timer-accounts.txt'
        workingHours = 'account-timer-settings.txt'
        dayOverrides = 'account-timer-days.txt'
        theme = 'account-timer-theme.txt'
    }
    $accountLines = @('First synthetic client', "Another account`thidden")
    $hours = @('start=09:30','end=18:00','days=Mon,Tue,Wed,Thu,Fri', 'shift.fri=10:00,16:00')
    Set-Content -Path (Join-Path $fixtureRoot 'account-timer-accounts.txt') -Value $accountLines
    Set-Content -Path (Join-Path $fixtureRoot 'account-timer-settings.txt') -Value $hours
    Set-Content -Path (Join-Path $fixtureRoot 'account-timer-days.txt') -Value @('2026-10-08' + "`toff")
    Set-Content -Path (Join-Path $fixtureRoot 'account-timer-theme.txt') -Value 'Lavender'
    Set-Content -Path (Join-Path $fixtureRoot 'account-timer-size.txt') -Value '1.25'
    Write-Host 'Fixture: importing legacy preferences'
    Initialize-SettingsStore
    Write-Host 'Fixture: legacy preferences imported'
    Assert (Test-Path $script:SettingsStoreFile) 'Migration did not create the JSON settings file.'
    Assert ((Read-SettingFirst 'theme') -eq 'Lavender') 'Theme selection was lost on migration.'
    Assert ((Read-SettingFirst 'size') -eq '1.25') 'Interface scale was lost on migration.'
    Assert ((@(Read-SettingSection 'accounts') -join '|') -eq ($accountLines -join '|')) 'Accounts or hidden flags were lost.'
    Assert ((@(Read-SettingSection 'workingHours') -join '|') -eq ($hours -join '|')) 'Working hours and shifts were lost.'
    $backup = Join-Path $fixtureRoot 'Timesheets\legacy-settings-backup'
    Assert (Test-Path (Join-Path $backup 'account-timer-accounts.txt')) 'Original account settings were not backed up.'
    Assert (Test-Path (Join-Path $backup 'account-timer-settings.txt')) 'Original working-hours settings were not backed up.'
    Assert (-not (Test-Path (Join-Path $fixtureRoot 'account-timer-accounts.txt'))) 'Legacy account settings were not archived.'
    $current = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($script:SettingsStoreFile))
    Assert ($current.schemaVersion -eq 1) 'Settings schema version is incorrect.'
    Write-Host 'Fixture: updating preferences'
    Write-SettingSections @{ accounts = @('Account X', "Account Y`thidden"); theme = @('Midnight') }
    Assert ((Read-SettingFirst 'theme') -eq 'Midnight') 'New theme selection was not saved.'
    Write-Host 'Fixture: update written'
    Assert (Test-Path "$script:SettingsStoreFile.bak") 'Atomic update must leave a previous JSON backup.'
    $script:SettingsSections = @{}
    Write-Host 'Fixture: reloading consolidated preferences'
    Initialize-SettingsStore
    Assert ((Read-SettingFirst 'theme') -eq 'Midnight') 'Saved settings did not survive a restart.'
    Assert ((@(Read-SettingSection 'accounts') -join '|') -eq ('Account X|Account Y' + "`thidden")) 'Updated accounts were not loaded.'
    Write-Host 'Fixture: updating independent section'
    Write-SettingSection 'blur' @('1')
    Assert ((Read-SettingFirst 'blur') -eq '1') 'Individual setting updates must persist.'
    Assert ((Read-SettingFirst 'workingHours') -eq $hours[0]) 'Updating unrelated settings lost working hours.'
    $script:SettingsSections = @{}
    Initialize-SettingsStore
    Assert ((Read-SettingFirst 'blur') -eq '1') 'Blur setting lost after reload.'
    Write-Host 'Fixture: completed settings verification'
    # Source content must not contain old per-preference write calls.
    $appText = [IO.File]::ReadAllText($source)
    Assert (-not $appText.Contains('Set-Content -Path $AccountsFile')) 'Application still writes fragmented account settings.'
    Assert (-not $appText.Contains('Set-Content -Path $SettingsFile')) 'Application still writes fragmented working-hour settings.'
    Assert ($appText.Contains('Basics.AdvancedTimer')) 'The Windows taskbar ID was not updated.'
    Write-Host 'Settings migration, atomic update and branding tests passed.'
} finally {
    Remove-Item $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
