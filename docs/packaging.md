# MCP server NuGet packaging

The package project is src/TXTextControl.McpServer/TXTextControl.McpServer.csproj.
Package ID: TXTextControl.McpServer. Initial preview: 0.1.0-alpha.2.

The existing web project is non-packable and consumes this library by ProjectReference.
Its admin pages, static assets, credentials, and deployment configuration remain host-owned.
Only the library compiles Models, Options, Services, and Tools from their existing folders.
The library assembly is named TXTextControl.McpServer.Core to avoid collisions with the existing host executable.

The buildTransitive target deploys the restored TX SDK's font content to consuming apps.
It does not bundle or relicense the SDK fonts. Both build output and published output need these assets.

## Build and verify

Provision the approved private strong-name key securely at build/signing/textcontrol.snk,
or supply -TextControlSigningKeyFile to scripts/pack.ps1. Never commit the key.
The public key is available at build/signing/textcontrol.publickey.

```powershell
./scripts/pack.ps1
# Alternatively:
./scripts/pack.ps1 -Version 0.1.0-alpha.2 -TextControlSigningKeyFile /secure/path/textcontrol.snk
```

Packages and symbol packages are written to artifacts/packages. The script verifies archive
metadata, dependency declarations, license/readme/icon hashes, the packaged assembly hash,
its expected public key token, and symbols. It rejects private keys and host content.
For cryptographic strong-name verification on Windows, additionally run the Windows SDK
sn.exe -vf against the Release TXTextControl.McpServer.Core.dll.
No package feed publishing or NuGet certificate signing is performed.

## Consumer smoke test

On Windows, after packing, run ./scripts/smoke-package.ps1. It restores the minimal host
with a real PackageReference using the local package output and configured dependency feeds.
It checks tool discovery, preset-styled Markdown creation, PDF conversion/download, and
worker shutdown on a temporary loopback endpoint. Test PDFs and logs remain in
artifacts/package-smoke. The test launches and stops only its own host and children.

The minimal host normally uses a ProjectReference for repository development.
Use -p:UsePackageReferences=true to exercise the NuGet package. Its appsettings.json
contains document presentation presets but no administration credentials.

The included LICENSE.txt is the approved Text Control AI Source Available License 1.0,
reused unchanged from the AI packages. This is a file-based custom NuGet license, not an
SPDX license expression. The same TX 34 icon and full assembly signing key are used.

The test project is also signed to retain access to library internals. The standalone
web host and benchmark executable are not packages and do not require signing.

See the package README for host integration, worker dispatch, configuration, native runtime
requirements, and authentication/session-isolation responsibilities.
