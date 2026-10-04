<#
.SYNOPSIS
  Builds every bridge version and the runner, and lays bridges out as artifacts\bridges\<host>\<year>\.
  With -Package, also produces artifacts\HicasTest-<version>-win-x64.zip for other machines (see install.ps1).

  Building a year does not need that host installed (API references come from NuGet).
  Supported years: build\HostVersions.props. 2027 needs the .NET 10 SDK.

.EXAMPLE
  ./build.ps1                                  # every supported Revit and AutoCAD year, Release
  ./build.ps1 -Package                         # + self-contained zip to hand to QA / other devs
  ./build.ps1 -Revit 2026 -AutoCAD @() -Configuration Debug
#>
param(
    [int[]] $Revit = @(2021, 2022, 2023, 2024, 2025, 2026, 2027),
    [int[]] $AutoCAD = @(2021, 2022, 2023, 2024, 2025, 2026, 2027),
    [string] $Configuration = 'Release',
    [switch] $Package
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$bridges = Join-Path $root 'artifacts\bridges'

function Build-Bridge([string] $project, [string] $property, [int] $year, [string] $tag, [string] $hostName) {
    Write-Host "== $hostName $year" -ForegroundColor Cyan
    dotnet build (Join-Path $root "src\$project\$project.csproj") -c $Configuration "-p:$property=$year"
    if ($LASTEXITCODE -ne 0) { throw "$project $year failed" }

    $out = Join-Path $root "src\$project\bin\$tag$year\$Configuration"
    $target = Join-Path $bridges "$hostName\$year"
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $out '*') $target -Recurse
}

foreach ($year in $Revit)   { Build-Bridge 'HicasTest.Bridge.Revit'   'RevitVersion'   $year 'R' 'revit' }
foreach ($year in $AutoCAD) { Build-Bridge 'HicasTest.Bridge.AutoCAD' 'AutoCADVersion' $year 'A' 'autocad' }

Write-Host "== runner, cli, mcp, tests" -ForegroundColor Cyan
foreach ($p in 'src\HicasTest.Cli\HicasTest.Cli.csproj', 'src\HicasTest.Mcp\HicasTest.Mcp.csproj', 'tests\HicasTest.Runner.Tests\HicasTest.Runner.Tests.csproj') {
    dotnet build (Join-Path $root $p) -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "$p failed" }
}
Write-Host "Bridges: $bridges" -ForegroundColor Green

if (-not $Package) { return }

# Self-contained: target machines need no .NET runtime, only Revit/AutoCAD.
Write-Host "== package" -ForegroundColor Cyan
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup[0].Version
$pkg = Join-Path $root 'artifacts\package\HicasTest'
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }

# Separate folders: the MCP server pulls newer System.Text.Json / Microsoft.Extensions.* than the CLI's runtime,
# and publishing both into one folder overwrites one app's dependencies with the other's.
$publish = @{ 'src\HicasTest.Cli\HicasTest.Cli.csproj' = $pkg; 'src\HicasTest.Mcp\HicasTest.Mcp.csproj' = (Join-Path $pkg 'mcp') }
foreach ($p in $publish.Keys) {
    dotnet publish (Join-Path $root $p) -c Release -r win-x64 --self-contained -o $publish[$p]
    if ($LASTEXITCODE -ne 0) { throw "publish $p failed" }
}

Copy-Item $bridges (Join-Path $pkg 'bridges') -Recurse
foreach ($item in 'install.ps1', 'README.md', 'docs', 'examples', 'integration') {
    Copy-Item (Join-Path $root $item) $pkg -Recurse
}

$zip = Join-Path $root "artifacts\HicasTest-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip
Write-Host "Package: $zip" -ForegroundColor Green
