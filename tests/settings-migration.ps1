# Synthetic portability, settings migration, and branding tests for Windows PowerShell 5.1.
# Never reads the user's timesheets, credentials, themes, or configuration files.
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'AccountTimer2.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if (@($parseErrors).Count -gt 0) { throw "Application has parse errors: $($parseErrors -join '; ')" }
$names = @('Save-SettingsStore', 'Test-SettingSection', 'Read-SettingSection',
           'Read-SettingFirst', 'Write-SettingSections', 'Write-SettingSection',
           'Initialize-SettingsStore')
foreach ($name in $names) {
    $found = @($ast.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($found.Count -ne 1) { throw "Expected one function $name, found $($found.Count)" }
    Invoke-Expression $found[0].Extent.Text
}
function Assert([bool]$result, [string]$why) { if (-not $result) { throw $why } }
$root = Join-Path ([IO.Path]::GetTempPath()) ('basics-timer-settings-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $root -Force)
try {
    $script:AppRoot = $root
    $script:SettingsStoreFile = Join-Path $root 'basics-advanced-timer.settings.json'
    $script:SettingsStoreVersion = 1
    $script:SettingsSections = @{}
    $script:LegacySettings = [ordered]@{
        size = 'account-timer-size.txt'
        accounts = 'account-timer-accounts.txt'
        workingHours = 'account-timer-settings.txt'
        dayOverrides = 'account-timer-days.txt'
        theme = 'account-timer-theme.txt'
        budgets = 'account-timer-budgets.txt'
    }
    $accounts = @('One synthetic account', "Another synthetic account`thidden")
    $hours = @('start=09:30', 'end=18:00', 'days=Mon,Tue,Wed,Thu,Fri', 'shift.fri=10:00,16:00')
    Set-Content -Path (Join-Path $root 'account-timer-accounts.txt') -Value $accounts
    Set-Content -Path (Join-Path $root 'account-timer-settings.txt') -Value $hours
    Set-Content -Path (Join-Path $root 'account-timer-days.txt') -Value ("2026-10-08`toff")
    Set-Content -Path (Join-Path $root 'account-timer-theme.txt') -Value 'Lavender'
    Set-Content -Path (Join-Path $root 'account-timer-size.txt') -Value '1.25'
    Set-Content -Path (Join-Path $root 'account-timer-budgets.txt') -Value ("One synthetic account`t12.5`tweek")
    Initialize-SettingsStore
    Assert (Test-Path $script:SettingsStoreFile) 'Migration failed to create settings JSON.'
    Assert ((Read-SettingFirst 'size') -eq '1.25') 'UI size lost during migration.'
    Assert ((Read-SettingFirst 'theme') -eq 'Lavender') 'Theme choice lost during migration.'
    Assert ((@(Read-SettingSection 'accounts') -join '|') -eq ($accounts -join '|')) 'Account order/visibility lost during migration.'
    Assert ((@(Read-SettingSection 'workingHours') -join '|') -eq ($hours -join '|')) 'Working hours or weekday shifts lost.'
    Assert ((Read-SettingFirst 'dayOverrides') -eq ("2026-10-08`toff")) 'Day override lost.'
    Assert ((Read-SettingFirst 'budgets') -eq ("One synthetic account`t12.5`tweek")) 'Budget lost.'
    $backup = Join-Path $root 'Timesheets\legacy-settings-backup'
    foreach ($name in @($script:LegacySettings.Values)) {
        Assert (Test-Path (Join-Path $backup $name)) "Legacy backup missing $name."
        Assert (-not (Test-Path (Join-Path $root $name))) "Legacy file was not archived: $name."
    }
    $json = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($script:SettingsStoreFile))
    Assert ($json.schemaVersion -eq 1) 'Missing JSON schema version.'
    Write-SettingSections @{ theme = @('Midnight'); accounts = @('Account X', "Account Y`thidden") }
    Assert ((Read-SettingFirst 'theme') -eq 'Midnight') 'New theme not immediately applied.'
    Assert (Test-Path "$script:SettingsStoreFile.bak") 'Atomic save did not retain a previous JSON backup.'
    $script:SettingsSections = @{}
    Initialize-SettingsStore
    Assert ((Read-SettingFirst 'theme') -eq 'Midnight') 'Updated theme lost after reload.'
    Assert ((@(Read-SettingSection 'accounts') -join '|') -eq ('Account X|Account Y' + "`thidden")) 'Updated account lines lost after reload.'
    Assert ((Read-SettingFirst 'workingHours') -eq $hours[0]) 'Unchanged working hours overwritten.'
    Write-SettingSection 'blur' @('1')
    Assert ((Read-SettingFirst 'blur') -eq '1') 'Single-setting write failed.'
    $script:SettingsSections = @{}
    Initialize-SettingsStore
    Assert ((Read-SettingFirst 'blur') -eq '1') 'Single-setting write lost after reload.'
    Assert ((Read-SettingFirst 'theme') -eq 'Midnight') 'Writing blur overwrote unrelated preferences.'
    $appText = [IO.File]::ReadAllText($source)
    Assert ($appText.Contains('Basics.AdvancedTimer')) 'Windows app identity was not updated.'
    Assert ($appText.Contains('basics-advanced-timer.settings.json')) 'Consolidated settings location not referenced.'
    Assert (-not $appText.Contains('Set-Content -Path $AccountsFile')) 'App still writes old account preference file.'
    Assert (-not $appText.Contains('Set-Content -Path $SettingsFile')) 'App still writes old working-hours preference file.'
    Write-Host 'Settings migration, backup, restart, and branding tests passed.'
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
