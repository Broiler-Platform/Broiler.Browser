# Build the unpublished HTTP/2 dependencies from sibling checkouts without replacing released packages.
[CmdletBinding()]
param(
    [string] $NetPath = (Join-Path $PSScriptRoot '../../Broiler.Net'),
    [string] $BridgePath = (Join-Path $PSScriptRoot '../../Broiler.HtmlBridge')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$browserRoot = Split-Path $PSScriptRoot -Parent
$feed = Join-Path $browserRoot 'artifacts/http2/packages'
[xml] $versions = Get-Content (Join-Path $browserRoot 'Directory.Packages.props')
$netVersion = ($versions.Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Broiler.Net').Version
$bridgeVersion = $versions.Project.PropertyGroup.BroilerHtmlBridgeVersion
if ($netVersion -notmatch '-http2\.' -and $bridgeVersion -notmatch '-http2\.') {
    throw 'No local -http2.N versions are selected. Never reuse a published preview.N version for a local package: NuGet will cache it instead of the published assembly.'
}
New-Item -ItemType Directory -Path $feed -Force | Out-Null

function Pack-Http2Project([string] $Project, [string] $Version) {
    $id = [IO.Path]::GetFileNameWithoutExtension($Project)
    if (Test-Path (Join-Path $feed "$id.$Version.nupkg")) {
        throw "$id $Version already exists. Use a new local version after editing dependency sources; do not overwrite cached packages."
    }
    & dotnet pack $Project -c Release "-p:Version=$Version" "-p:PackageVersion=$Version" --output $feed --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "Packing $Project failed ($LASTEXITCODE)." }
}

if ($netVersion -match '-http2\.') {
    Pack-Http2Project (Join-Path $NetPath 'src/Broiler.Net/Broiler.Net.csproj') $netVersion
}
if ($bridgeVersion -match '-http2\.') {
    foreach ($id in @('Broiler.HtmlBridge.Core', 'Broiler.HtmlBridge.DomBridgeUtils', 'Broiler.HtmlBridge.Dom', 'Broiler.HtmlBridge.Scripting', 'Broiler.HtmlBridge.Scripting.Vm')) {
        Pack-Http2Project (Join-Path $BridgePath "src/$id/$id.csproj") $bridgeVersion
    }
}
Write-Host "HTTP/2 packages are ready in $feed."
