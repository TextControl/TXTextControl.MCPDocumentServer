using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Tools;

var builder = WebApplication.CreateBuilder(args);

// Use fully qualified names to avoid ambiguity with ModelContextProtocol.Server.McpServerOptions
builder.Services.Configure<TxTextControl.McpServer.Options.McpServerOptions>(
    builder.Configuration.GetSection(TxTextControl.McpServer.Options.McpServerOptions.SectionName));

// Core services for simplified implementation
builder.Services.AddSingleton<TxTextControl.McpServer.Services.PathResolver>();
builder.Services.AddSingleton<DocumentSessionService>();
builder.Services.AddSingleton<ITxDocumentEngine, ServerTextControlDocumentEngine>();
builder.Services.AddSingleton<DocumentWorkflowService>();
builder.Services.AddHostedService<FileCleanupService>();

// MCP Server tools split by responsibility
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Recommended for servers that don't need server-to-client requests.
        options.Stateless = true;
    })
    .WithTools<DocumentTools>()
    .WithTools<ContentTools>();


var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    name = "TX Text Control Document MCP Server (Simplified)",
    version = "1.0",
    status = "ok",
    description = "Session-based document management and manipulation with InternalUnicodeFormat storage",
    mcp = "/mcp",
    documentFormat = "InternalUnicodeFormat"
}));

app.MapMcp("/mcp");
app.Run();

public partial class Program;
