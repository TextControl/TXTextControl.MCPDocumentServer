[CmdletBinding()]
param([string]$Version = "0.1.0-alpha.2")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packagePath = Join-Path $repositoryRoot "artifacts/packages/TXTextControl.McpServer.$Version.nupkg"
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $names = @($archive.Entries.FullName)
    foreach ($required in @("README.md", "LICENSE.txt", "tx34.png",
        "buildTransitive/TXTextControl.McpServer.targets",
        "lib/net10.0/TXTextControl.McpServer.Core.dll",
        "lib/net10.0/TXTextControl.McpServer.Core.xml")) {
        if ($required -notin $names) { throw "Missing package asset: $required" }
    }
    foreach ($name in $names) {
        if ($name -match '(?i)(\.snk$|appsettings|(^|/)(wwwroot|Pages|MyDocuments|sessions)/)') {
            throw "Unexpected private or host asset: $name"
        }
    }
    foreach ($asset in @{
        "LICENSE.txt" = "LICENSE.txt"
        "README.md" = "src/TXTextControl.McpServer/README.md"
        "tx34.png" = "build/assets/tx34.png"
        "lib/net10.0/TXTextControl.McpServer.Core.dll" = "src/TXTextControl.McpServer/bin/Release/net10.0/TXTextControl.McpServer.Core.dll"
    }.GetEnumerator()) {
        $stream = $archive.GetEntry($asset.Key).Open()
        try { $actual = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash }
        finally { $stream.Dispose() }
        $expected = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot $asset.Value) -Algorithm SHA256).Hash
        if ($actual -ne $expected) { throw "Package asset differs from source/build: $($asset.Key)" }
    }
    $manifestEntry = $archive.Entries | Where-Object FullName -like "*.nuspec" | Select-Object -First 1
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $metadata = $manifest.package.metadata
    if ($metadata.id -ne "TXTextControl.McpServer" -or $metadata.version -ne $Version) {
        throw "Incorrect package identity."
    }
    if ($metadata.license.type -ne "file" -or $metadata.license.InnerText -ne "LICENSE.txt" -or
        $metadata.icon -ne "tx34.png" -or $metadata.readme -ne "README.md" -or
        $metadata.requireLicenseAcceptance -ne "true") { throw "Incorrect package metadata." }
    $dependencies = @($metadata.dependencies.group.dependency.id)
    foreach ($dependency in @("ModelContextProtocol.AspNetCore", "TXTextControl.Markdown.Core",
        "TXTextControl.TextControl.Core.SDK")) {
        if ($dependency -notin $dependencies) { throw "Missing dependency: $dependency" }
    }
    $assemblyPath = Join-Path $repositoryRoot "src/TXTextControl.McpServer/bin/Release/net10.0/TXTextControl.McpServer.Core.dll"
    $identity = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
    $token = ($identity.GetPublicKeyToken() | ForEach-Object { $_.ToString("x2") }) -join ""
    if ($token -ne "6b83fe9a75cfb638") { throw "Incorrect strong-name public key token: $token" }
} finally { $archive.Dispose() }
$symbols = [IO.Compression.ZipFile]::OpenRead(
    (Join-Path $repositoryRoot "artifacts/packages/TXTextControl.McpServer.$Version.snupkg"))
try {
    if ("lib/net10.0/TXTextControl.McpServer.Core.pdb" -notin $symbols.Entries.FullName) {
        throw "Missing package symbols."
    }
    if (@($symbols.Entries | Where-Object FullName -match '(?i)\.snk$').Count -gt 0) {
        throw "Signing key must never be packaged."
    }
} finally { $symbols.Dispose() }
Write-Host "Verified MCP package metadata, assets, dependencies, assembly token, and symbols: $packagePath"
