<#
.SYNOPSIS
  Smoke-tests a HicasTest package zip without Revit/AutoCAD: unzip, CLI hosts, validate, MCP handshake.
  Catches packaging mistakes (missing bridges, broken publish layout, MCP server not starting).

.EXAMPLE
  ./build/smoke-test.ps1 -Zip artifacts\HicasTest-0.2.0-win-x64.zip
#>
param(
    [Parameter(Mandatory = $true)][string] $Zip,
    [int] $ExpectedBridges = 14,
    [int] $McpTimeoutSec = 60
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()
function Check([bool] $ok, [string] $what) {
    if ($ok) { Write-Host "  ok    $what" -ForegroundColor Green }
    else { Write-Host "  FAIL  $what" -ForegroundColor Red; $failures.Add($what) }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("hicastest-smoke-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$pkg = Join-Path $work 'pkg'
Expand-Archive -Path $Zip -DestinationPath $pkg
$cli = Join-Path $pkg 'hicastest.exe'
$mcp = Join-Path $pkg 'mcp\hicastest-mcp.exe'

Write-Host "== layout"
Check (Test-Path $cli) 'hicastest.exe at package root'
Check (Test-Path $mcp) 'mcp\hicastest-mcp.exe'
Check (-not (Test-Path (Join-Path $pkg 'hicastest-mcp.exe'))) 'no MCP server in the root (would share CLI dependencies)'
Check (Test-Path (Join-Path $pkg 'install.ps1')) 'install.ps1'
Check (Test-Path (Join-Path $pkg 'contracts\HicasTest.Contracts.dll')) 'contracts\HicasTest.Contracts.dll (for add-in test assemblies)'
Check (Test-Path (Join-Path $pkg 'samples\SampleEntries\LevelEntries.cs')) 'samples\SampleEntries (reference implementation)'
$bridgeDlls = @(Get-ChildItem (Join-Path $pkg 'bridges') -Recurse -Filter 'HicasTest.Bridge.*.dll' |
    Where-Object { $_.Name -ne 'HicasTest.Bridge.Core.dll' })
Check ($bridgeDlls.Count -eq $ExpectedBridges) "bridges: $($bridgeDlls.Count) of $ExpectedBridges"

Write-Host "== cli hosts"
Push-Location $work   # not the repo: the CLI must find bridges next to itself, not in ./artifacts
try {
    $hosts = & $cli hosts
    Check ($LASTEXITCODE -eq 0) 'hicastest hosts exits 0'
    $built = @($hosts | Where-Object { $_ -match '\bbuilt\b' })
    Check ($built.Count -eq $ExpectedBridges) "hosts reports $($built.Count) built bridges"

    Write-Host "== cli validate"
    $cases = Join-Path $work 'cases'
    New-Item -ItemType Directory -Force (Join-Path $cases 'ok'), (Join-Path $cases 'bad') | Out-Null
    Set-Content (Join-Path $cases 'model.rvt') 'not a real model' -Encoding ascii
    $yaml = @'
id: SMOKE-{0}
host: {{ app: revit, version: 2024 }}
model: ../model.rvt
run: {{ mode: postcommand, command: ID_SMOKE }}
expect:
  - {{ kind: added, category: OST_Views, count: 1{1} }}
'@
    Set-Content (Join-Path $cases 'ok\case.yaml') ($yaml -f 'OK', ', source: "smoke"') -Encoding utf8
    Set-Content (Join-Path $cases 'bad\case.yaml') ($yaml -f 'BAD', '') -Encoding utf8
    & $cli validate (Join-Path $cases 'ok') | Out-Null
    Check ($LASTEXITCODE -eq 0) 'valid case passes validation'
    New-Item -ItemType Directory -Force (Join-Path $cases 'entries') | Out-Null
    $entriesYaml = @'
id: SMOKE-ENTRIES
host: {{ app: revit, version: 2024 }}
model: ../model.rvt
run: {{ mode: entries, assembly: ../My.Testing.dll }}
calls:
  - entry: demo.make
    expectResult:
      - {{ path: status, equals: ok{0} }}
'@
    Set-Content (Join-Path $cases 'entries\case.yaml') ($entriesYaml -f ', source: "smoke"') -Encoding utf8
    & $cli validate (Join-Path $cases 'entries') | Out-Null
    Check ($LASTEXITCODE -eq 0) 'entries case passes validation'
    Set-Content (Join-Path $cases 'entries\case.yaml') ($entriesYaml -f '') -Encoding utf8
    $badEntries = & $cli validate (Join-Path $cases 'entries')
    Check ($LASTEXITCODE -eq 2 -and ($badEntries -join "`n") -match 'source') 'entries check without oracle source is rejected'
    $bad = & $cli validate (Join-Path $cases 'bad')
    Check ($LASTEXITCODE -eq 2 -and ($bad -join "`n") -match 'source') 'case without oracle source is rejected'
}
finally {
    Pop-Location
}

Write-Host "== mcp handshake"
$psi = [Diagnostics.ProcessStartInfo]::new($mcp)
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.StandardOutputEncoding = [Text.Encoding]::UTF8
$psi.WorkingDirectory = $work
$proc = [Diagnostics.Process]::Start($psi)
$stderr = $proc.StandardError.ReadToEndAsync()
try {
    $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}')
    $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list"}')
    $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_hosts","arguments":{}}}')
    $proc.StandardInput.Flush()

    $deadline = [DateTime]::UtcNow.AddSeconds($McpTimeoutSec)
    $responses = @{}
    while ($responses.Count -lt 3 -and [DateTime]::UtcNow -lt $deadline -and -not $proc.HasExited) {
        $read = $proc.StandardOutput.ReadLineAsync()
        if (-not $read.Wait([Math]::Max(1, ($deadline - [DateTime]::UtcNow).TotalMilliseconds))) { break }
        if ($null -eq $read.Result) { break }
        try { $msg = $read.Result | ConvertFrom-Json } catch { continue }
        if ($null -ne $msg.id) { $responses[[int]$msg.id] = $msg }
    }

    Check ($responses.ContainsKey(1) -and $null -ne $responses[1].result) 'initialize answered'
    $tools = @()
    if ($responses.ContainsKey(2)) { $tools = @($responses[2].result.tools | ForEach-Object name) }
    Check ($tools.Count -ge 20) "tools/list returns $($tools.Count) tools"
    foreach ($t in 'run_test_case', 'qa_session_start', 'list_hosts', 'list_entries', 'call_entry', 'qa_session_list_entries', 'qa_session_call_entry') { Check ($tools -contains $t) "tool $t" }
    $hostsText = ''
    if ($responses.ContainsKey(3)) { $hostsText = ($responses[3].result.content | ForEach-Object text) -join "`n" }
    Check (([regex]::Matches($hostsText, 'bridge=built')).Count -eq $ExpectedBridges) 'list_hosts sees every bridge from mcp\'
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
    $proc.WaitForExit(5000) | Out-Null
    if ($failures.Count -gt 0) {
        Write-Host "--- mcp stderr ---"
        if ($stderr.Wait(2000)) { Write-Host $stderr.Result }
    }
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
if ($failures.Count -gt 0) {
    Write-Host "`nSmoke test FAILED: $($failures.Count) check(s)" -ForegroundColor Red
    exit 1
}
Write-Host "`nSmoke test passed" -ForegroundColor Green
exit 0   # the deliberate failing 'validate' above leaves $LASTEXITCODE = 2
