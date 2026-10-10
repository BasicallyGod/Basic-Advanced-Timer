param(
    [ValidateRange(1, 65535)][int]$BuildNumber = 1,
    [string]$OutputDirectory = 'artifacts/msix',
    [string]$PackageName = 'BasicsAdvancedTimer.Dev',
    [string]$Publisher = 'CN=BasicsAdvancedTimerDevelopment',
    [string]$PublisherDisplayName = "Basic's Advanced Timer (Development)",
    [switch]$UnsignedForStore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'MSIX packaging requires Windows and the Windows SDK.' }
if ($UnsignedForStore -and ($PackageName -eq 'BasicsAdvancedTimer.Dev' -or $Publisher -eq 'CN=BasicsAdvancedTimerDevelopment')) {
    throw 'A Store candidate must use the real Identity Name and Publisher from Partner Center.'
}
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$output = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
$temporaryRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$stage = Join-Path $temporaryRoot 'basics-msix-stage'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage, $output | Out-Null
$version = "0.1.$BuildNumber.0"

# Store-only AssemblyName keeps the portable executable's existing filename.
dotnet publish (Join-Path $repoRoot 'migration/src/Timer.Desktop/Timer.Desktop.csproj') --configuration Release --runtime win-x64 --self-contained true -p:StorePackage=true -p:PublishSingleFile=false "-p:Version=$version" -o $stage
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
if (-not (Test-Path (Join-Path $stage 'BasicsAdvancedTimer.exe'))) { throw 'Missing packaged EXE.' }

$manifest = Get-Content (Join-Path $PSScriptRoot 'AppxManifest.template.xml') -Raw -Encoding utf8
$replacements = @{
    '{{PACKAGE_NAME}}' = [System.Security.SecurityElement]::Escape($PackageName)
    '{{PUBLISHER}}' = [System.Security.SecurityElement]::Escape($Publisher)
    '{{PUBLISHER_DISPLAY_NAME}}' = [System.Security.SecurityElement]::Escape($PublisherDisplayName)
    '{{VERSION}}' = $version
}
foreach ($entry in $replacements.GetEnumerator()) { $manifest = $manifest.Replace($entry.Key, $entry.Value) }
if ($manifest.Contains('{{')) { throw 'An MSIX manifest placeholder is unresolved.' }
[xml]$parsedManifest = $manifest
[System.IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, [System.Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'New-StoreIcons.ps1') -Destination (Join-Path $stage 'Assets')

$sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
if (-not (Test-Path $sdkBin)) { throw 'Windows SDK tools are missing.' }
$makeAppx = Get-ChildItem $sdkBin -Recurse -Filter makeappx.exe -File | Where-Object { $_.DirectoryName -match '[\\/]x64$' } | Sort-Object FullName -Descending | Select-Object -First 1
$signTool = Get-ChildItem $sdkBin -Recurse -Filter signtool.exe -File | Where-Object { $_.DirectoryName -match '[\\/]x64$' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeAppx -or (-not $UnsignedForStore -and -not $signTool)) { throw 'Windows SDK MakeAppx/SignTool not found.' }

$baseName = if ($UnsignedForStore) { 'BasicsAdvancedTimer-Store-unsigned' } else { 'BasicsAdvancedTimer-Dev' }
$package = Join-Path $output "$baseName.msix"
& $makeAppx.FullName pack /d $stage /p $package /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx pack failed.' }

# Verify actual package contents without relying on runner sideloading privileges.
$inspect = Join-Path ([System.IO.Path]::GetTempPath()) "basics-msix-inspect-$([guid]::NewGuid().ToString('N'))"
try {
    & $makeAppx.FullName unpack /p $package /d $inspect /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'MakeAppx unpack failed.' }
    foreach ($item in @('AppxManifest.xml', 'BasicsAdvancedTimer.exe', 'Microsoft.Data.Sqlite.dll', 'SQLitePCLRaw.core.dll', 'e_sqlite3.dll')) {
        if (-not (Test-Path (Join-Path $inspect $item))) { throw "Missing packaged file: $item" }
    }
}
finally { if (Test-Path $inspect) { Remove-Item -Recurse -Force $inspect } }

if (-not $UnsignedForStore) {
    # Disposable developer signing certificate. Never export or upload a private key.
    $cert = New-SelfSignedCertificate -Type Custom -KeyUsage DigitalSignature -Subject $Publisher -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -FriendlyName 'Basic Advanced Timer temporary MSIX test signing'
    try {
        Export-Certificate -Cert $cert -FilePath (Join-Path $output 'BasicsAdvancedTimer-Dev.cer') -Force | Out-Null
        & $signTool.FullName sign /fd SHA256 /s My /sha1 $cert.Thumbprint $package
        if ($LASTEXITCODE -ne 0) { throw 'SignTool could not sign the developer MSIX.' }
    }
    finally { Remove-Item -Path ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -Force -ErrorAction SilentlyContinue }
}
Write-Host "Created $package ($version)"
