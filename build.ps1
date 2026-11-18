param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\DarkShot\DarkShot.csproj'
$output = Join-Path $root 'dist\DarkShot'

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

Write-Host "Building DarkShot ($Configuration, framework-dependent)..." -ForegroundColor Cyan
dotnet publish $project -c $Configuration -r win-x64 --self-contained false -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Join-Path $output 'DarkShot.exe'
if (-not (Test-Path $exe)) { throw "Publish did not produce $exe" }

$size = (Get-ChildItem $output -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("Published: {0}  ({1:N1} MB)" -f $exe, ($size / 1MB)) -ForegroundColor Green
Write-Host "Requires the .NET Desktop Runtime 10 (x64)."

if ($SelfContained) {
    $portable = Join-Path $root 'dist\DarkShot-portable'
    if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
    Write-Host "Building self-contained single-file portable build..." -ForegroundColor Cyan
    dotnet publish $project -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $portable --nologo
    if ($LASTEXITCODE -ne 0) { throw "self-contained publish failed" }
    $portableSize = (Get-ChildItem $portable -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Host ("Portable: {0}  ({1:N1} MB)" -f (Join-Path $portable 'DarkShot.exe'), ($portableSize / 1MB)) -ForegroundColor Green
}
