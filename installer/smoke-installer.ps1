[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '0.1.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$setupExecutable = Join-Path $repositoryRoot "output\installer\TimeBet-Setup-$Version-win-x64.exe"
if (-not (Test-Path -LiteralPath $setupExecutable)) {
    throw "Installer output was not found: $setupExecutable"
}

$runId = [Guid]::NewGuid().ToString('N')
$smokeRoot = Join-Path $repositoryRoot "output\installer-smoke\$runId"
$installDirectory = Join-Path $smokeRoot 'app'
$dataDirectory = Join-Path $smokeRoot 'data'
$databasePath = Join-Path $dataDirectory 'timebet.db'
$screenshotPath = Join-Path $dataDirectory 'Screenshots'

New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null

Write-Host "Installing into the isolated smoke-test directory: $installDirectory"
$setupArguments = @(
    '/VERYSILENT',
    '/SUPPRESSMSGBOXES',
    '/NORESTART',
    '/SP-',
    '/MERGETASKS=!desktopicon',
    "/DIR=$installDirectory"
)
$setupProcess = Start-Process -FilePath $setupExecutable -ArgumentList $setupArguments -Wait -PassThru
if ($setupProcess.ExitCode -ne 0) {
    throw "Installer smoke test failed with setup exit code $($setupProcess.ExitCode)."
}

$installedExecutable = Join-Path $installDirectory 'TimeBet.Desktop.exe'
$uninstaller = Join-Path $installDirectory 'unins000.exe'
foreach ($requiredFile in @($installedExecutable, $uninstaller)) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Expected installed file was not found: $requiredFile"
    }
}

Write-Host 'Starting the installed application in smoke-test mode...'
try {
    $env:TIMEBET_SMOKE_TEST = '1'
    $env:TIMEBET_DATABASE_PATH = $databasePath
    $env:TIMEBET_SCREENSHOT_PATH = $screenshotPath

    $appProcess = Start-Process -FilePath $installedExecutable -WorkingDirectory $installDirectory -Wait -PassThru
    if ($appProcess.ExitCode -ne 0) {
        throw "Installed application smoke test failed with exit code $($appProcess.ExitCode)."
    }
}
finally {
    Remove-Item Env:TIMEBET_SMOKE_TEST -ErrorAction SilentlyContinue
    Remove-Item Env:TIMEBET_DATABASE_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:TIMEBET_SCREENSHOT_PATH -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $databasePath)) {
    throw "The application did not create its isolated database: $databasePath"
}

Write-Host 'Uninstalling the isolated application...'
$uninstallArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
$uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList $uninstallArguments -Wait -PassThru
if ($uninstallProcess.ExitCode -ne 0) {
    throw "Installer smoke test failed with uninstall exit code $($uninstallProcess.ExitCode)."
}

if (Test-Path -LiteralPath $installDirectory) {
    throw "The application installation directory remains after uninstall: $installDirectory"
}

if (-not (Test-Path -LiteralPath $databasePath)) {
    throw "Uninstall unexpectedly removed the user's local database: $databasePath"
}

Write-Host ''
Write-Host 'Installer smoke test passed:'
Write-Host '- setup completed successfully'
Write-Host '- the installed application started successfully'
Write-Host '- uninstall removed the program and WebView2 cache'
Write-Host "- local user data was retained at $dataDirectory"
