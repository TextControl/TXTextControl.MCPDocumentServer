[CmdletBinding()]
param()
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sampleDirectory = Join-Path $repositoryRoot "samples/MinimalMcpHost"
$sampleProject = Join-Path $sampleDirectory "MinimalMcpHost.csproj"
$packageDirectory = Join-Path $repositoryRoot "artifacts/packages"
& dotnet restore $sampleProject -p:UsePackageReferences=true "-p:RestoreAdditionalProjectSources=$packageDirectory" --nologo
if ($LASTEXITCODE -ne 0) { throw "Package consumer restore failed." }
& dotnet build $sampleProject -c Release -p:UsePackageReferences=true --no-restore --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Package consumer build failed." }

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$baseUrl = "http://127.0.0.1:$port"
$runDirectory = Join-Path $repositoryRoot ("artifacts/package-smoke/" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$entryPoint = Join-Path $sampleDirectory "bin/Release/net10.0/MinimalMcpHost.dll"
$hostProcess = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(
    ('"' + $entryPoint + '"'), "--urls", $baseUrl,
    "--McpServer:BasePath", ('"' + (Join-Path $runDirectory "documents") + '"')
) -WorkingDirectory $sampleDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (
    Join-Path $runDirectory "host.log") -RedirectStandardError (Join-Path $runDirectory "host-error.log")
$workerIds = @()
function Invoke-SmokeMcp([string]$Method, [hashtable]$Parameters) {
    $payload = @{ jsonrpc = "2.0"; id = 1; method = $Method; params = $Parameters } | ConvertTo-Json -Depth 12
    $response = Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/mcp" -Method Post -ContentType "application/json" -Headers @{
        Accept = "application/json, text/event-stream"
    } -Body $payload -TimeoutSec 45
    $content = [string]$response.Content
    if ($content -match '(?m)^data:') {
        $content = (($content -split "`n" | Where-Object { $_.StartsWith("data:") } | Select-Object -First 1).Substring(5)).Trim()
    }
    $rpc = $content | ConvertFrom-Json
    if ($rpc.error -or $rpc.result.isError) { throw "MCP call failed: $content" }
    return $rpc.result
}
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        if ($hostProcess.HasExited) { throw "Consumer exited before startup. Inspect $runDirectory." }
        try {
            $tools = Invoke-SmokeMcp "tools/list" @{}
            $ready = $true
            break
        } catch {
            Start-Sleep -Milliseconds 250
        }
    }
    if (-not $ready) { throw "Consumer did not start. Inspect $runDirectory." }
    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $($hostProcess.Id)")
    $workerIds = @($children | Select-Object -ExpandProperty ProcessId)
    $documentWorkers = @($children | Where-Object { $_.CommandLine -match '--document-worker' })
    if ($documentWorkers.Count -ne 2) { throw "Expected two persistent document workers, got $($documentWorkers.Count)." }
    $created = Invoke-SmokeMcp "tools/call" @{
        name = "create_document_from_markdown"
        arguments = @{ request = @{ markdown = "# NuGet package smoke test`n`nCreated using the reusable MCP server package." } }
    }
    $document = $created.structuredContent
    if (-not $document.sessionId) { throw "Creation did not return a session: $($created | ConvertTo-Json -Depth 8)" }
    if ($document.paragraphsStyled -lt 1) { throw "Default presets were not applied." }
    $converted = Invoke-SmokeMcp "tools/call" @{
        name = "convert_document"
        arguments = @{ request = @{ sessionId = $document.sessionId; outputFormat = "pdf"; fileName = "package-smoke.pdf" } }
    }
    $download = $converted.structuredContent.downloadUri
    if (-not $download -or -not $download.StartsWith("$baseUrl/exports/")) { throw "Export did not return a local download URI." }
    $pdfPath = Join-Path $runDirectory "package-smoke.pdf"
    Invoke-WebRequest -UseBasicParsing -Uri $download -OutFile $pdfPath -TimeoutSec 30
    $stream = [IO.File]::OpenRead($pdfPath)
    try {
        $signature = New-Object byte[] 5
        [void]$stream.Read($signature, 0, 5)
        if ([Text.Encoding]::ASCII.GetString($signature) -ne "%PDF-") { throw "Download is not a PDF." }
    } finally { $stream.Dispose() }
    Write-Host "Verified $($tools.tools.Count) tools, preset-styled creation, PDF conversion and download: $pdfPath"
} finally {
    if (-not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id }
    $hostProcess.WaitForExit(5000) | Out-Null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $remaining = @($workerIds | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
        if ($remaining.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    }
    if ($remaining.Count -gt 0) {
        # Only processes captured as children of this test host are cleaned up.
        $remaining | ForEach-Object { Stop-Process -Id $_ -ErrorAction SilentlyContinue }
        throw "Document workers did not exit when their package host stopped."
    }
    $hostProcess.Dispose()
}
Write-Host "Verified package-host worker shutdown."
