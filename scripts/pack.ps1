[CmdletBinding()]
param(
    [string]$Version = "0.1.0-alpha.2",
    [string]$TextControlSigningKeyFile
)
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packageProject = Join-Path $repositoryRoot "src/TXTextControl.McpServer/TXTextControl.McpServer.csproj"
$arguments = @("pack", $packageProject, "-c", "Release", "-o",
    (Join-Path $repositoryRoot "artifacts/packages"), "-p:Version=$Version", "--nologo")
if ($TextControlSigningKeyFile) {
    $arguments += "-p:TextControlSigningKeyFile=$TextControlSigningKeyFile"
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "MCP package build failed ($LASTEXITCODE)." }
& (Join-Path $PSScriptRoot "verify-package.ps1") -Version $Version
