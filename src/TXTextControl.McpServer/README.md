# TX Text Control MCP Server

Host a document-focused Model Context Protocol (MCP) server in your own ASP.NET Core application. Create, inspect, edit, and convert documents using TX Text Control, independently of your AI provider or language model.

## Features

- HTTP MCP tools for document creation, focused inspection, editing, and conversion.
- Markdown import, configurable default styles and page layout, and document recipes.
- Native paragraph styles, tables, merge fields, form fields, sections, and headers/footers.
- Structured exports with download endpoints.
- Persistent document workers with bounded queues and session affinity.

This is the **server hosting library**, not an MCP client or a ready-to-run executable. It does not include the reference host's admin pages, authentication scheme, or an LLM. The separate TXTextControl.AI.Mcp package connects AI applications to MCP servers.

## Requirements and licensing

Requires .NET 10, the ASP.NET Core shared framework, and appropriately licensed TX Text Control products. Configure the Text Control NuGet feed and any required credentials for the commercial SDK dependencies.

Deployment must satisfy the native runtime and platform requirements of the selected TX Text Control SDK. A managed NuGet package does not remove these requirements. Install the fonts required by your documents on the server.

The included transitive build target copies the SDK's default fonts from the restored SDK package into build and publish output. It does not bundle fonts in this package; the SDK's license continues to apply. Additional fonts used by your documents remain your deployment responsibility.

Distributed under the included **Text Control AI Source Available License 1.0**. Commercial TX Text Control components and third-party dependencies retain their own licenses. See LICENSE.txt and https://www.textcontrol.com/ for licensing information.

## Install

```sh
dotnet add package TXTextControl.McpServer --version 0.1.0-alpha.2
```

Use the feed containing this preview package; building the repository does not publish it.

## Minimal host

```csharp
using TxTextControl.McpServer;

// Must precede web-host initialization and any console output.
// The default worker pool relaunches this executable in worker mode.
if (await TextControlMcpWorker.RunIfRequestedAsync(args) is int exitCode)
{
    Environment.ExitCode = exitCode;
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTextControlMcpServer(builder.Configuration);

var app = builder.Build();
app.MapTextControlMcp();
app.Run();
```

MapTextControlMcp registers /mcp, /mcp/knowledge/extract and /exports/{sessionId}/{exportId}. It returns a route group, so a host can apply its authorization policy to the tool transport, extraction and downloads:

Knowledge extraction is a host-controlled binary HTTP endpoint, not a model-facing tool.
POST `<MCP path>/knowledge/extract?format=pdf` (or docx/rtf/tx/html/htm) with
`Content-Type: application/octet-stream`, `X-TextControl-Knowledge: 1` and the same
host-required authorization. The response is `{ version: "mcp-tx34-structure-v1", blocks: [...] }`,
with text, locators and optional heading/table-header context. It accepts up to 32 MiB
(subject to lower ingress limits), two million extracted characters and 100,000 blocks.
One short-lived control is created and disposed on the MCP machine. No working session,
model call or server-side source file is created. Native imports are serialized;
cancellation cannot interrupt a native load already in progress. PDF uses text-line
extraction; scanned/image-only files need OCR before upload. Protect this endpoint
with the same host authorization/ingress rules as MCP, including TLS and quotas.

The AI host stores original sources and indexes, and no longer needs native Text Control
for Knowledge. Upgrade both MCP and AI integration binaries together when adopting this endpoint.

```csharp
app.MapTextControlMcp().RequireAuthorization("DocumentAccess");
```

Register that policy and authentication middleware in your application. The package does **not** automatically authenticate callers or provide per-user/session ownership isolation. Keep unauthenticated examples on loopback. Before exposing an endpoint, implement authentication, document/session ownership checks, upload limits, TLS, and deployment-appropriate access controls. Browser downloads must carry the credentials required by your host.

Call MapTextControlMcp on the root application, not inside an additional route-group prefix. When deployed under a URL base path, configure ASP.NET Core PathBase and trusted forwarding consistently so generated export URLs match the public address.

## Configuration

Pass the application configuration **root** to AddTextControlMcpServer. The existing section names are retained:

```json
{
  "McpServer": {
    "BasePath": "documents",
    "SessionMaxAgeHours": 12
  },
  "DocumentWorkerPool": {
    "Enabled": true,
    "WorkerCount": 2,
    "QueueCapacity": 128
  }
}
```

- McpServer: document storage location and session retention. Use a writable, private directory.
- DocumentWorkerPool: concurrency, queue limits, timeouts, hot sessions, and optional WorkerExecutablePath.
- DocumentAutomation: capability packs, enabled operations, paragraph and table presets, style roles, and default page layout.

Presentation presets are host configuration, not hard-coded package policy. Supply the
DocumentAutomation section from the reference host or minimal sample to reproduce its
styled output; the short JSON example above only configures storage and workers.

The reference host exposes administration controls for these options. The package does not map administration endpoints or modify the host's authentication configuration. The current settings services persist explicit administration edits to appsettings.json under the host content root; deploy that file writable only if your application uses those settings-editing services.

## Worker deployment

With the default pool enabled, include the worker dispatch shown above in every consuming host. Workers use redirected standard input/output for their protocol; do not print startup banners before dispatch.

For IIS, service wrappers, or other launchers where the current process is not your application executable, explicitly configure DocumentWorkerPool:WorkerExecutablePath to an executable or published DLL implementing this dispatch. Deploy its runtime configuration and dependencies alongside it. The package does not bundle a separate worker executable.

Disabling DocumentWorkerPool:Enabled selects the in-process document engine. Sessions are currently held in memory and document data is stored on disk. A single host is not a distributed session store.

## Package contents

The package contains the signed TXTextControl.McpServer.Core assembly, XML API documentation, the transitive font-deployment target, this README, LICENSE.txt, and the TX 34 icon. Dependencies are declared through NuGet, not bundled as proprietary payloads. The standalone host retains its existing executable assembly name.

No private signing key, admin UI, credentials, appsettings.json, or user documents are included. The accompanying .snupkg contains debug symbols. Assembly strong-name signing is distinct from certificate-signing the NuGet archive.
