<#
.SYNOPSIS
  Installs HicasTest from an unzipped package on this machine (no build tools needed).

.DESCRIPTION
  1. Copies the package to -Destination (default %LOCALAPPDATA%\Programs\HicasTest).
  2. Creates %LOCALAPPDATA%\HicasTest\config.json from a template if it does not exist
     (edit it when Revit/AutoCAD are not in C:\Program Files\Autodesk).
  3. Shows which Revit/AutoCAD years this machine can test.
  4. With -RegisterMcp, registers the MCP server "hicas-test" for Claude Code (user scope).
     Without it, prints the command so you can run it yourself.

.EXAMPLE
  ./install.ps1 -RegisterMcp
#>
param(
    [string] $Destination = (Join-Path $env:LOCALAPPDATA 'Programs\HicasTest'),
    [switch] $RegisterMcp
)

$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot

if (-not (Test-Path (Join-Path $source 'hicastest.exe'))) {
    throw "Run install.ps1 from an unzipped HicasTest package (hicastest.exe not found next to it). Developers: ./build.ps1 -Package."
}

Write-Host "Installing to $Destination" -ForegroundColor Cyan
if ((Resolve-Path $source).Path -ne $Destination) {
    New-Item -ItemType Directory -Force $Destination | Out-Null
    Copy-Item (Join-Path $source '*') $Destination -Recurse -Force
}

$configDir = Join-Path $env:LOCALAPPDATA 'HicasTest'
$config = Join-Path $configDir 'config.json'
if (-not (Test-Path $config)) {
    New-Item -ItemType Directory -Force $configDir | Out-Null
    @'
{
  // Only needed when hosts are not in C:\Program Files\Autodesk\<Revit|AutoCAD> <year>\
  "revit":   { },
  "autocad": { },
  // Revit UI language passed as /language (ENU, VIT, ...). "" = Revit default.
  "revitLanguage": "ENU"
  // "outputDir": "D:/qa-evidence"
}
'@ | Set-Content -Path $config -Encoding UTF8
    Write-Host "Created $config" -ForegroundColor Cyan
}

& (Join-Path $Destination 'hicastest.exe') hosts

$mcp = Join-Path $Destination 'hicastest-mcp.exe'
$command = "claude mcp add --scope user hicas-test -- `"$mcp`""
if ($RegisterMcp -and (Get-Command claude -ErrorAction SilentlyContinue)) {
    Invoke-Expression $command
    Write-Host "Registered MCP server 'hicas-test' for Claude Code." -ForegroundColor Green
} else {
    Write-Host "`nTo let Claude drive the tool, run:" -ForegroundColor Yellow
    Write-Host "  $command"
}
Write-Host "`nCLI: `"$(Join-Path $Destination 'hicastest.exe')`" --help"
