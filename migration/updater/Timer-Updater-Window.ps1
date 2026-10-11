#Requires -Version 5.1
# A small window for choosing which branch to update from and launch. The real work is done by Update-TestBuild.ps1,
# which opens in its own console so its prompts (such as asking you to close a running timer) stay visible.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$Repository = 'BasicallyGod/Basic-Advanced-Timer'
$Worker = Join-Path $PSScriptRoot 'Update-TestBuild.ps1'
$Root = Join-Path $PSScriptRoot 'test-install'
$ChoicePath = Join-Path $Root 'last-branch.txt'
# Each channel has its own update state and data folder.

function Get-Branches {
    if (!(Get-Command gh -ErrorAction SilentlyContinue)) { return @() }
    $names = New-Object System.Collections.Generic.List[string]
    for ($page = 1; $page -le 5; $page++) {
        $output = & gh api --hostname github.com "repos/$Repository/branches?per_page=100&page=$page" 2>$null
        if ($LASTEXITCODE -ne 0) { break }
        $found = @(($output -join "`n") | ConvertFrom-Json)
        foreach ($branch in $found) { $names.Add([string]$branch.name) }
        if ($found.Count -lt 100) { break }
    }
    return $names.ToArray()
}

function Get-InstalledText([string]$Name) {
    if ($Name -notin @('experimental', 'alpha')) { return 'Choose Experimental or Alpha.' }
    $statePath = Join-Path (Join-Path (Join-Path $Root 'channels') $Name) 'state.json'
    if (!(Test-Path -LiteralPath $statePath)) { return 'Nothing installed yet.' }
    try {
        $current = (Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).current
        if ($null -eq $current) { return 'Nothing installed yet.' }
        $date = ([datetimeoffset]$current.buildDate).ToLocalTime().ToString('d MMM yyyy HH:mm')
        return "Installed: $($current.branch), commit $($current.commitSha.Substring(0, 7)), built $date"
    } catch { return 'Installed build could not be read.' }
}

function Read-LastBranch {
    if (Test-Path -LiteralPath $ChoicePath) { return (Get-Content -LiteralPath $ChoicePath -Raw).Trim() }
    return 'experimental'
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Basic' + [char]0x2019 + 's Advanced Timer updater'
$form.StartPosition = 'CenterScreen'; $form.FormBorderStyle = 'FixedDialog'; $form.MaximizeBox = $false
$form.ClientSize = New-Object System.Drawing.Size(460, 250); $form.Font = New-Object System.Drawing.Font('Segoe UI', 10)

$label = New-Object System.Windows.Forms.Label
$label.Text = 'Development channel'; $label.Location = '16,16'; $label.AutoSize = $true
$combo = New-Object System.Windows.Forms.ComboBox
$combo.Location = '16,42'; $combo.Width = 428; $combo.DropDownStyle = 'DropDownList'
$status = New-Object System.Windows.Forms.Label
$status.Location = '16,80'; $status.Size = '428,44'; $status.Text = 'Select a channel to see its installed build.'
$hint = New-Object System.Windows.Forms.Label
$hint.Location = '16,124'; $hint.Size = '428,40'; $hint.ForeColor = [System.Drawing.Color]::DimGray

$update = New-Object System.Windows.Forms.Button; $update.Text = 'Update and launch'; $update.Location = '16,176'; $update.Size = '200,34'
$launch = New-Object System.Windows.Forms.Button; $launch.Text = 'Launch current'; $launch.Location = '228,176'; $launch.Size = '216,34'
$rollback = New-Object System.Windows.Forms.Button; $rollback.Text = 'Roll back and launch'; $rollback.Location = '16,212'; $rollback.Size = '200,30'
$refresh = New-Object System.Windows.Forms.Button; $refresh.Text = 'Refresh channels'; $refresh.Location = '228,212'; $refresh.Size = '216,30'
$form.Controls.AddRange(@($label, $combo, $status, $hint, $update, $launch, $rollback, $refresh))
$combo.Add_SelectedIndexChanged({ $status.Text = Get-InstalledText $combo.Text })

function Fill-Branches {
    $form.Cursor = 'WaitCursor'; $form.Refresh()
    $wanted = if ($combo.Text) { $combo.Text } else { Read-LastBranch }
    $names = @(Get-Branches)
    $combo.Items.Clear()
    $combo.Items.AddRange([string[]]@('experimental', 'alpha'))
    if ($names.Count -gt 0) {
        $hint.Text = 'Experimental is for daily testing; Alpha is for unstable changes.'
    } else {
        $hint.Text = 'Could not check GitHub branches. You can still launch an installed build offline.'
    }
    $combo.SelectedItem = if ($wanted -in @('experimental', 'alpha')) { $wanted } else { 'experimental' }
    $status.Text = Get-InstalledText $combo.Text
    $form.Cursor = 'Default'
}

function Invoke-Worker([string]$Mode) {
    $branch = $combo.Text.Trim()
    if ($branch -notin @('experimental', 'alpha')) { [void][System.Windows.Forms.MessageBox]::Show('That is not a usable branch name.', $form.Text); return }
    $null = New-Item -ItemType Directory -Path $Root -Force
    [IO.File]::WriteAllText($ChoicePath, $branch)
    # The worker's console stays open on an error so the message can be read, and closes by itself after a good launch.
    $workerPath = $Worker.Replace("'", "''")
    $command = "& '$workerPath' -Mode $Mode -Branch '$branch'; if (`$LASTEXITCODE) { Read-Host 'Press Enter to close' }"
    Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $command)
    $status.Text = 'Started. Watch the console window for progress.'
}

$update.Add_Click({ Invoke-Worker 'Update' })
$launch.Add_Click({ Invoke-Worker 'Launch' })
$rollback.Add_Click({ Invoke-Worker 'Rollback' })
$refresh.Add_Click({ Fill-Branches })
$form.Add_Shown({ Fill-Branches })
[void]$form.ShowDialog()
