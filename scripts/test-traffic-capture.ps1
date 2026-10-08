[CmdletBinding()]
param(
    [string] $Tshark = 'C:\Program Files\Wireshark\tshark.exe'
)

# Bounded integration test: capture only this script's loopback fixture ports.
# No Internet traffic, payload files, or elevated helper windows are needed.
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assembly = Join-Path $repositoryRoot 'src/Broiler.Traffic/bin/Release/net10.0/Broiler.Traffic.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw 'Build Broiler.Traffic in Release first.' }
$outputDirectory = Join-Path $repositoryRoot ('artifacts/traffic-smoke-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$outputPath = Join-Path $outputDirectory 'traffic.jsonl'
$resources = [Collections.Generic.List[IDisposable]]::new()
$fixtures = @()
$capture = $null
try {
    foreach ($address in @([Net.IPAddress]::Loopback, [Net.IPAddress]::IPv6Loopback)) {
        $listener = [Net.Sockets.TcpListener]::new($address, 0)
        $listener.Start()
        $resources.Add($listener)
        $client = [Net.Sockets.TcpClient]::new($address.AddressFamily)
        $resources.Add($client)
        $client.Connect($listener.LocalEndpoint)
        $server = $listener.AcceptTcpClient()
        $resources.Add($server)
        $udp = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new($address, 0))
        $resources.Add($udp)
        $receiver = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new($address, 0))
        $resources.Add($receiver)
        $fixtures += [pscustomobject]@{ Client = $client; Server = $server; Udp = $udp; Receiver = $receiver }
    }
    $clauses = foreach ($fixture in $fixtures) {
        'tcp port ' + $fixture.Server.Client.LocalEndPoint.Port
        'udp port ' + $fixture.Receiver.Client.LocalEndPoint.Port
    }
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @($assembly, '-i', '\Device\NPF_Loopback', '-f', ($clauses -join ' or '),
        '--seconds', '5', '--pid', [string][Environment]::ProcessId, '--label', 'local-smoke',
        '--output', $outputPath, '--tshark', $Tshark)) {
        $start.ArgumentList.Add($argument)
    }
    $capture = [Diagnostics.Process]::Start($start)
    $stdout = $capture.StandardOutput.ReadToEndAsync()
    $errors = [Collections.Generic.List[string]]::new()
    $ready = $false
    while (-not $ready) {
        $line = $capture.StandardError.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(15)).GetAwaiter().GetResult()
        if ($null -eq $line) { throw ('Capture ended before readiness: ' + ($errors -join "`n")) }
        $errors.Add($line)
        $ready = $line.Contains('Capturing on')
    }
    $stderr = $capture.StandardError.ReadToEndAsync()
    $request = [Text.Encoding]::ASCII.GetBytes("GET /search?q=private-smoke-token HTTP/1.1`r`nHost: www.google.com`r`n`r`n")
    $response = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 429 Too Many Requests`r`nContent-Length: 0`r`n`r`n")
    $datagram = [Text.Encoding]::ASCII.GetBytes('local UDP fixture')
    for ($round = 0; $round -lt 12; $round++) {
        foreach ($fixture in $fixtures) {
            $fixture.Client.GetStream().Write($request)
            $fixture.Server.GetStream().Write($response)
            $fixture.Udp.Send($datagram, $datagram.Length, $fixture.Receiver.Client.LocalEndPoint) | Out-Null
        }
        Start-Sleep -Milliseconds 150
    }
    if (-not $capture.WaitForExit(15000)) { throw 'Capture did not terminate after its time limit.' }
    $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $outputDirectory 'stdout.txt')
    $errors.Add($stderr.GetAwaiter().GetResult())
    $errors -join "`n" | Set-Content -LiteralPath (Join-Path $outputDirectory 'stderr.txt')
    if ($capture.ExitCode -ne 0) { throw ('Capture failed: ' + ($errors -join "`n")) }
    $raw = Get-Content -Raw -LiteralPath $outputPath
    $records = @(Get-Content -LiteralPath $outputPath | ForEach-Object { $_ | ConvertFrom-Json })
    if ($records.Count -eq 0) { throw 'No attributed packets captured.' }
    foreach ($transport in @('TCP', 'UDP')) {
        foreach ($address in @('127.0.0.1', '::1')) {
            $matching = @($records | Where-Object { $_.transport -eq $transport -and $_.source -eq $address })
            if ($matching.Count -eq 0) { throw "Missing $transport $address traffic." }
            foreach ($record in $matching) {
                if ([Environment]::ProcessId -notin @($record.owners.pid)) { throw 'Incorrect fixture owner.' }
                if ($record.userLabel -ne 'local-smoke') { throw 'Label missing.' }
            }
        }
    }
    if ($raw.Contains('private-smoke-token')) { throw 'Query text leaked into metadata.' }
    if (-not @($records | Where-Object { $_.httpStatus -eq 429 }).Count) { throw 'Visible HTTP 429 not decoded.' }
    if (-not @($records | Where-Object { $_.useCase -eq 'Google Search (visible HTTP path)' }).Count) { throw 'Visible search path not classified.' }
    Write-Output "PASS: $($records.Count) packets; IPv4/IPv6 TCP+UDP owners, HTTP 429, classification, label and query omission. $outputDirectory"
}
finally {
    if ($null -ne $capture) {
        if (-not $capture.HasExited) { $capture.Kill($true); $capture.WaitForExit() }
        $capture.Dispose()
    }
    foreach ($resource in $resources) { $resource.Dispose() }
}
