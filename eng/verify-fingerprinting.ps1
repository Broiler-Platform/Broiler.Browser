[CmdletBinding()]
param(
    [string] $Cli = 'artifacts/compatibility-completion/final-cli/Broiler.Cli.dll',
    [string] $Output = 'artifacts/compatibility-completion/final',
    [switch] $SkipRun
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$cliPath = [IO.Path]::GetFullPath($Cli, $root)
$outputPath = [IO.Path]::GetFullPath($Output, $root)
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$fixture = [Uri]::new((Join-Path $root 'docs/repros/fingerprinting-graphics-layout.html')).AbsoluteUri
$pages = [ordered]@{ fixture = $fixture; creepjs = 'https://abrahamjuliot.github.io/creepjs/'; sannysoft = 'https://bot.sannysoft.com/' }
function Require($condition, [string] $message) { if (!$condition) { throw $message } }
function ReadProbe([string] $html) {
    $match = [regex]::Match($html, '<pre id="result"[^>]*>(.*?)</pre>', 'Singleline')
    Require $match.Success 'Fixture did not produce its result element.'
    return [Net.WebUtility]::HtmlDecode($match.Groups[1].Value) | ConvertFrom-Json
}
$evidence = [ordered]@{ observedUtc = [DateTime]::UtcNow.ToString('o'); pages = [ordered]@{} }
foreach ($name in $pages.Keys) {
    $directory = Join-Path $outputPath $name
    $width = if ($name -eq 'fixture') { 400 } else { 1280 }
    $height = if ($name -eq 'fixture') { 700 } else { 900 }
    if (!$SkipRun) {
        & dotnet $cliPath --analyze $pages[$name] --output-dir $directory --width $width --height $height --no-window *> (Join-Path $outputPath "$name-run.log")
        Require ($LASTEXITCODE -eq 0) "$name analysis failed; see $name-run.log."
        & dotnet $cliPath --capture-image $pages[$name] --output (Join-Path $outputPath "$name-capture.png") --width $width --height $height *> (Join-Path $outputPath "$name-capture.log")
        Require ($LASTEXITCODE -eq 0) "$name capture failed."
    }
    $report = Get-Content (Join-Path $directory 'report.json') -Raw | ConvertFrom-Json
    Require (Test-Path (Join-Path $outputPath "$name-capture.png")) "$name ordinary capture is missing."
    $html = Get-Content (Join-Path $directory 'document-after-scripts.html') -Raw
    Require ($report.scripting.failures -eq 0) "$name has script failures."
    Require ($report.scripting.unhandledRejections -eq 0) "$name has unhandled rejections."
    Require ($report.layout.invariantViolations.Count -eq 0) "$name has layout invariant violations."
    $details = [ordered]@{
        url = $pages[$name]; viewport = @($width,$height); failures = $report.scripting.failures
        analysisStarted = $report.startedAt
        unhandledRejections = $report.scripting.unhandledRejections
        invariantViolations = $report.layout.invariantViolations.Count
        loadedAssemblies = $report.environment.components
    }
    if ($name -eq 'fixture') {
        $probes = ReadProbe $html
        Require (@($probes.PSObject.Properties).Count -eq 7) 'Unexpected fixture result count.'
        foreach ($property in $probes.PSObject.Properties) { Require ($property.Value -eq $true) "Fixture failed: $($property.Name)=$($property.Value)" }
        $details.probes = $probes
        Require ($html.Contains('data-broiler-canvas-bitmap="data:image/png;base64,')) 'Canvas bitmap missing from render projection.'
    } elseif ($name -eq 'creepjs') {
        $console = Get-Content (Join-Path $directory 'console.log') -Raw
        $collectors = @('canvas 2d','svg','rects','dedicated worker','navigator','headless','media')
        foreach ($collector in $collectors) { Require ($console.Contains("$collector passed")) "CreepJS collector did not pass: $collector" }
        $text = [Net.WebUtility]::HtmlDecode([regex]::Replace($html, '<[^>]*>', ''))
        Require ($text -match 'FP ID:\s*[a-f0-9]{32}') 'CreepJS fingerprint result missing.'
        $details.collectorsPassed = $collectors
    } else {
        $table = [regex]::Match($html, '<table id="fp2"[^>]*>(.*?)</table>', 'Singleline')
        $rows = [regex]::Matches($table.Groups[1].Value, '<tr>').Count
        Require ($rows -eq 20) "Sannysoft scanner row count changed: $rows; review site version."
        $hashes = [regex]::Matches($html, 'Hash: -?\d+').Count
        Require ($hashes -eq 5) "Sannysoft canvas result count changed: $hashes."
        $details.scannerRows = $rows; $details.canvasHashes = $hashes
        $fingerprint = [regex]::Match($html, '<pre id="fp"[^>]*>(.*?)</pre>', 'Singleline')
        Require $fingerprint.Success 'Sannysoft fingerprint JSON missing.'
        $data = [Net.WebUtility]::HtmlDecode($fingerprint.Groups[1].Value) | ConvertFrom-Json
        $details.fingerprintKeys = @($data.PSObject.Properties).Count
    }
    $details.sources = @(Get-ChildItem (Join-Path $directory 'resources') -File | Where-Object Extension -eq '.js' | ForEach-Object {
        @{ file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $details.documentSha256 = (Get-FileHash (Join-Path $directory 'document-as-fetched.html') -Algorithm SHA256).Hash
    $evidence.pages[$name] = $details
}
$evidence | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $outputPath 'observed.json') -Encoding utf8
Write-Host 'Verified fixture, live collectors, result DOM, layout invariants and ordinary image captures.'
