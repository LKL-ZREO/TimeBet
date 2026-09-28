[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '0.1.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$frontendRoot = Join-Path $repositoryRoot 'frontend'
$desktopProject = Join-Path $repositoryRoot 'src\TimeBet.Desktop\TimeBet.Desktop.csproj'
$publishDirectory = Join-Path $repositoryRoot 'output\TimeBet-win-x64-single'
$installerScript = Join-Path $PSScriptRoot 'TimeBet.iss'
$cacheDirectory = Join-Path $PSScriptRoot '.cache'
$webViewBootstrapper = Join-Path $cacheDirectory 'MicrosoftEdgeWebview2Setup.exe'
$webViewBootstrapperUri = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'

function Assert-LastExitCode {
    param([Parameter(Mandatory)][string]$Operation)

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Find-InnoSetupCompiler {
    $fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        return $fromPath.Source
    }

    $roots = @(
        $env:LOCALAPPDATA,
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)}
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($root in $roots) {
        $programRoot = if ($root -eq $env:LOCALAPPDATA) {
            Join-Path $root 'Programs'
        }
        else {
            $root
        }

        if (-not (Test-Path -LiteralPath $programRoot)) {
            continue
        }

        $compiler = Get-ChildItem -LiteralPath $programRoot -Directory -Filter 'Inno Setup*' -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'ISCC.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1

        if ($null -ne $compiler) {
            return $compiler
        }
    }

    throw @'
Inno Setup was not found. Install it, then run this script again:
winget install --id JRSoftware.InnoSetup -e -s winget -i
'@
}

function Assert-MicrosoftSignature {
    param([Parameter(Mandatory)][string]$Path)

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
        throw "The WebView2 bootstrapper does not have a valid Microsoft signature: $Path"
    }
}

foreach ($command in @('pnpm', 'dotnet')) {
    if ($null -eq (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command '$command' was not found."
    }
}

New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

if (-not (Test-Path -LiteralPath $webViewBootstrapper)) {
    Write-Host 'Downloading the Microsoft WebView2 Evergreen Bootstrapper...'
    Invoke-WebRequest -Uri $webViewBootstrapperUri -OutFile $webViewBootstrapper
}
Assert-MicrosoftSignature -Path $webViewBootstrapper

Write-Host 'Installing locked frontend dependencies...'
& pnpm --dir $frontendRoot install --frozen-lockfile
Assert-LastExitCode -Operation 'Frontend dependency installation'

Write-Host 'Building the frontend...'
& pnpm --dir $frontendRoot build
Assert-LastExitCode -Operation 'Frontend build'

Write-Host 'Publishing a self-contained Windows x64 executable...'
$publishArguments = @(
    'publish',
    $desktopProject,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:IncludeAllContentForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '--output', $publishDirectory
)
& dotnet @publishArguments
Assert-LastExitCode -Operation 'Desktop publish'

$publishedExecutable = Join-Path $publishDirectory 'TimeBet.Desktop.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw "Published executable was not found: $publishedExecutable"
}

$innoCompiler = Find-InnoSetupCompiler
Write-Host "Compiling the installer with $innoCompiler..."
& $innoCompiler "/DMyAppVersion=$Version" $installerScript
Assert-LastExitCode -Operation 'Installer compilation'

$setupExecutable = Join-Path $repositoryRoot "output\installer\TimeBet-Setup-$Version-win-x64.exe"
if (-not (Test-Path -LiteralPath $setupExecutable)) {
    throw "Installer output was not found: $setupExecutable"
}

Write-Host ''
Write-Host 'TimeBet installer created successfully:'
Write-Host $setupExecutable
