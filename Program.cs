using System;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services.Admin;
using TxTextControl.McpServer.Services.Operations;
using TxTextControl.McpServer.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "TxMcpAdmin";
        options.LoginPath = "/admin/login";
        options.LogoutPath = "/admin/logout";
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin");
    options.Conventions.AllowAnonymousToPage("/Admin/Login");
});

// Use fully qualified names to avoid ambiguity with ModelContextProtocol.Server.McpServerOptions
builder.Services.Configure<TxTextControl.McpServer.Options.McpServerOptions>(
    builder.Configuration.GetSection(TxTextControl.McpServer.Options.McpServerOptions.SectionName));
builder.Services.Configure<AdminOptions>(
    builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.Configure<DocumentAutomationOptions>(
    builder.Configuration.GetSection(DocumentAutomationOptions.SectionName));

// Core services for simplified implementation
builder.Services.AddSingleton<TxTextControl.McpServer.Services.PathResolver>();
builder.Services.AddSingleton<DocumentSessionService>();
builder.Services.AddSingleton<ICapabilityPack, BasicTextCapabilityPack>();
builder.Services.AddSingleton<ICapabilityPack, MediaCapabilityPack>();
builder.Services.AddSingleton<ICapabilityPack, TableCapabilityPack>();
builder.Services.AddSingleton<ICapabilityPack, FieldsCapabilityPack>();
builder.Services.AddSingleton<ICapabilityPack, SectionCapabilityPack>();
builder.Services.AddSingleton<ICapabilityPack, HeaderFooterCapabilityPack>();
builder.Services.AddSingleton<IDocumentOperationHandler, DefineStyleOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, AppendParagraphOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, ApplyStyleToParagraphOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, FormatParagraphsOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, FormatTextOccurrencesOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, ReplaceTextOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, AppendImageOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, AppendTableOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, SetTableCellTextOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, FormatTableCellOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, FormatTableHeaderRowOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, FormatTableColumnOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, ApplyTableStylePresetOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, AddTableRowOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, AppendMergeFieldOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, UpdateMergeFieldOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, ClearApplicationFieldsOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, AppendMergeBlockOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, AppendFormFieldOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, UpdateFormFieldOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, ClearFormFieldsOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, InsertSectionBreakOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, SetSectionLayoutOperationHandler>();
builder.Services.AddSingleton<IDocumentOperationHandler, SetHeaderFooterOperationHandler>();
builder.Services.AddSingleton<DocumentOperationRegistry>();
builder.Services.AddSingleton<AutomationSettingsService>();
builder.Services.AddSingleton<SupportedFontService>();
builder.Services.AddSingleton<AuthoringGuideService>();
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
    .WithTools<ContentTools>()
    .WithTools<OperationTools>();


var app = builder.Build();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/admin/automation", (
    IOptions<DocumentAutomationOptions> options,
    AutomationSettingsService settings,
    DocumentOperationRegistry registry) => Results.Ok(new
{
    capabilityPacks = registry.GetCapabilityPacks(),
    enabledCapabilityPacks = settings.GetEnabledCapabilityPacks(),
    enabledOperations = settings.GetEnabledOperations(),
    operations = registry.GetCapabilities(),
    defaultParagraphStyleName = options.Value.DefaultParagraphStyleName,
    styleRoles = options.Value.StyleRoles,
    stylePresets = options.Value.StylePresets,
    tableStylePresets = options.Value.TableStylePresets
})).RequireAuthorization();

app.MapRazorPages();
app.MapMcp("/mcp");
app.Run();

public partial class Program;
