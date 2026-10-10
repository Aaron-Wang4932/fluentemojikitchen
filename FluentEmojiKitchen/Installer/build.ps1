[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string] $Version = '1.0.0',
    [string] $IsccPath,
    [switch] $SkipInstaller
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Build this WinUI application on Windows.' }
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'FluentEmojiKitchen.csproj'
$publish = Join-Path $PSScriptRoot 'publish\win-x64'
$output = Join-Path $PSScriptRoot 'output'
if (!(Test-Path $project)) { throw "Project not found: $project" }
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (!$dotnet) { throw 'Install the .NET 10 SDK and the Visual Studio WinUI build prerequisites first.' }
if (!$SkipInstaller) {
    if (!$IsccPath) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $IsccPath = $command.Source }
        else {
            foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, $env:LOCALAPPDATA)) {
                if (!$base) { continue }
                foreach ($relative in @('Inno Setup 6\ISCC.exe', 'Inno Setup 7\ISCC.exe', 'Programs\Inno Setup 6\ISCC.exe', 'Programs\Inno Setup 7\ISCC.exe')) {
                    $candidate = Join-Path $base $relative
                    if (Test-Path $candidate) { $IsccPath = $candidate; break }
                }
                if ($IsccPath) { break }
            }
        }
    }
    if (!$IsccPath -or !(Test-Path $IsccPath)) {
        throw 'Install Inno Setup 6.3 or later from https://jrsoftware.org/isdl.php, or supply -IsccPath. Use -SkipInstaller to publish only.'
    }
}
Push-Location $projectRoot
try {
    # Only this script-owned publish directory is cleared to prevent stale release files.
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    New-Item $publish -ItemType Directory -Force | Out-Null
    $arguments = @('publish', $project, '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true', '-p:Platform=x64',
        '-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=true',
        '-p:PublishProfile=', '-p:PublishTrimmed=false', '-p:PublishSingleFile=false',
        "-p:Version=$Version", '-o', $publish)
    & $dotnet.Source @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
    foreach ($file in @('FluentEmojiKitchen.exe', 'Microsoft.UI.Xaml.dll', 'coreclr.dll')) {
        if (!(Test-Path (Join-Path $publish $file))) { throw "Self-contained publish is missing $file. Installer was not built." }
    }
    if ($SkipInstaller) { Write-Host "Published application: $publish"; return }
    New-Item $output -ItemType Directory -Force | Out-Null
    $installer = Join-Path $output "FluentEmojiKitchen-Setup-$Version-x64.exe"
    if (Test-Path $installer) { Remove-Item $installer -Force }
    & $IsccPath "/DAppVersion=$Version" "/DPublishDir=$publish" "/O$output" (Join-Path $PSScriptRoot 'FluentEmojiKitchen.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
    if (!(Test-Path $installer)) { throw 'Compiler finished but the expected installer was not found.' }
    $hash = (Get-FileHash $installer -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$installer.sha256" -Value "$hash  $([IO.Path]::GetFileName($installer))" -Encoding ASCII
    Write-Host "Installer ready: $installer"
    Write-Host 'Test it on a Windows machine without Visual Studio before distributing.'
}
finally { Pop-Location }
