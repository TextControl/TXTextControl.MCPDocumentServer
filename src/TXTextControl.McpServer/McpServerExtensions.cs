using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Services.Admin;
using TxTextControl.McpServer.Services.Operations;
using TxTextControl.McpServer.Services.Workers;
using TxTextControl.McpServer.Tools;

namespace TxTextControl.McpServer;

/// <summary>Registers the document MCP implementation without admin pages or authentication policy.</summary>
public static class McpServerExtensions
{
    /// <summary>Registers tools, presets, sessions, exports, and the configured document engine.</summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="configuration">The application configuration root, with McpServer, DocumentAutomation and DocumentWorkerPool sections.</param>
    public static IServiceCollection AddTextControlMcpServer(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddHttpContextAccessor();
        // Use fully qualified names to avoid ambiguity with ModelContextProtocol.Server.McpServerOptions
        services.Configure<TxTextControl.McpServer.Options.McpServerOptions>(
            configuration.GetSection(TxTextControl.McpServer.Options.McpServerOptions.SectionName));
        services.Configure<AdminOptions>(
            configuration.GetSection(AdminOptions.SectionName));
        services.Configure<DocumentAutomationOptions>(
            configuration.GetSection(DocumentAutomationOptions.SectionName));
        services.Configure<DocumentWorkerPoolOptions>(
            configuration.GetSection(DocumentWorkerPoolOptions.SectionName));
        
        // Core services for simplified implementation
        services.AddSingleton<TxTextControl.McpServer.Services.PathResolver>();
        services.AddSingleton<DocumentSessionService>();
        services.AddSingleton<ICapabilityPack, BasicTextCapabilityPack>();
        services.AddSingleton<ICapabilityPack, MediaCapabilityPack>();
        services.AddSingleton<ICapabilityPack, TableCapabilityPack>();
        services.AddSingleton<ICapabilityPack, FieldsCapabilityPack>();
        services.AddSingleton<ICapabilityPack, SectionCapabilityPack>();
        services.AddSingleton<ICapabilityPack, HeaderFooterCapabilityPack>();
        services.AddSingleton<IDocumentOperationHandler, DefineStyleOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, RenameStyleOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, DeleteStyleOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, CreateStylesFromParagraphsOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, AppendParagraphOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, ApplyStyleToParagraphOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, FormatParagraphsOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, FormatTextOccurrencesOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, ReplaceTextOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, AppendImageOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, AppendTableOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, SetTableCellTextOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, FormatTableCellOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, FormatTableHeaderRowOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, FormatTableColumnOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, ApplyTableStylePresetOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, AddTableRowOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, AppendMergeFieldOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, UpdateMergeFieldOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, ClearApplicationFieldsOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, AppendMergeBlockOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, AppendFormFieldOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, UpdateFormFieldOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, ClearFormFieldsOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, InsertSectionBreakOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, SetSectionLayoutOperationHandler>();
        services.AddSingleton<IDocumentOperationHandler, SetHeaderFooterOperationHandler>();
        services.AddSingleton<DocumentOperationRegistry>();
        services.AddSingleton<AutomationSettingsService>();
        services.AddSingleton<ServerSettingsService>();
        services.AddSingleton<SupportedFontService>();
        services.AddSingleton<AuthoringGuideService>();
        bool workerPoolEnabled = configuration.GetValue<bool?>(
            $"{DocumentWorkerPoolOptions.SectionName}:Enabled") ?? true;
        if (workerPoolEnabled)
        {
            services.AddSingleton<DocumentWorkerPool>();
            services.AddHostedService(serviceProvider =>
                serviceProvider.GetRequiredService<DocumentWorkerPool>());
            services.AddSingleton<ITxDocumentEngine, WorkerPooledDocumentEngine>();
        }
        else
        {
            services.AddSingleton<ITxDocumentEngine, ServerTextControlDocumentEngine>();
        }
        services.AddSingleton<DocumentWorkflowService>();
        services.AddHostedService<FileCleanupService>();
        
        // MCP Server tools split by responsibility
        services
            .AddMcpServer(options =>
            {
                options.ServerInstructions = """
                    Route every request by intent and do not mix workflows:
        
                    First classify the request as QUESTION, EDIT, CONVERT, or CREATE. Run only the matching workflow
                    unless the user explicitly combines intents. Never infer that conversion means rewriting.
        
                    1. QUESTION ABOUT A DOCUMENT: the document must already be loaded. Call inspect_document with
                       its sessionId and an optional focused query. It returns a bounded chunk. Keep maxCharacters at
                       or below 12000 in a model tool loop. If truncated is true and the answer requires more content,
                       continue from nextParagraphIndex; never replace bounded inspection with get_text. Answer only
                       from returned content. Do not edit, create, convert, or export unless separately requested.
                    2. EDIT AN EXISTING DOCUMENT: reuse its sessionId. When the user identifies a clause or part by
                       heading, call inspect_document_section and then replace_document_section with the returned
                       contentHash; inspect definitions or related clauses separately when the requested rewrite
                       depends on them. If a requested role such as Party A is not defined unambiguously in the
                       document, ask the user which signatory or defined party they mean before editing. For other
                       edits call inspect_document first. Use edit_document for exact text,
                       paragraph-index, paragraph-range, or character-range replacement. Use
                       expectedText with every character-range replacement so stale or invented offsets are rejected.
                       Treat an edit as complete only when its mutation result succeeds and reports a change; never
                       claim success merely because a tool call was planned or attempted. Use
                       format_text for one inspected range or paragraph. For requests to format every/all occurrence
                       of specified text, call format_text_occurrences exactly once with matchText and the requested
                       formatting properties (for example bold and colorHex); set
                       wholeWord=true when a word is named. Do not search for offsets or issue repeated format_text
                       calls for this intent. Use apply_operations only for structural edits unsupported by the focused
                       tools. For paragraph-level alignment, spacing, line spacing, or a named paragraph style, call
                       format_paragraph. For an editor selection pass selectedText as matchText and browser start as
                       nearTextPosition; the server resolves the containing TX paragraph. Never treat a browser offset
                       as paragraphIndex. NAMED STYLES: distinguish changing a style definition from formatting one
                       paragraph. For prompts such as "change the style Heading 1 to have red text", call
                       set_document_style with styleName="Heading 1" and text.colorHex="#FF0000". Do not format each
                       Heading 1 paragraph separately: the server commits the native style and TX propagates it to all
                       linked paragraphs. Use list_document_styles when the exact native style name or usage is unknown,
                       apply_document_style to link target paragraphs, rename_document_style or delete_document_style
                       for lifecycle changes, and create_styles_from_paragraphs when asked to turn repeated direct
                       paragraph formatting into reusable styles. For table appearance changes call format_table. With an editor selection pass
                       selectedText as matchText, browser start as nearTextPosition, and browser selection length as
                       selectionLength. Use scope selectedCells for selected cells and scope header for the selected
                       table's header; never derive table, row, or column indexes from browser offsets. When the user
                       asks to apply preset/default styles to a loaded document, call
                       apply_document_preset_styles once; do not recreate the document or generate individual style
                       operations. For a simple table insertion, call insert_table with the complete rows; it
                       supplies the internal operation discriminator automatically. For questions about table count,
                       structure, or order, call get_document_tables; it inspects the live TX document. For requests
                       such as "add 5 rows to the second table", call get_document_tables and then add_table_rows with
                       tableNumber=2 and count=5 (or use the returned tableId). Never guess table ids. Use apply_operations only for
                       other structural changes and include type in every operation. Never recreate the document to
                       make an edit.
                       FIELD EDITS: when the user asks to replace names, labels, or placeholders with merge fields,
                       call insert_merge_field with matchText and replaceAll/occurrenceIndex, or with an MCP-inspected
                       start/length/expectedText range. For a selection supplied by a browser editor, use its selected
                       text as matchText and fieldText and pass the browser start only as nearTextPosition; do not reuse
                       the browser character offset as an exact range because table structural positions can change
                       during serialization. This must create real MERGEFIELD
                       ApplicationFields and preserve the visible selected text unless the user asks otherwise. Never
                       substitute ordinary {{name}} text. Use insert_form_field for fillable controls and
                       create_merge_block for repeating content. Use update_merge_field/update_form_field to change
                       existing fields and the clear tools only when the user explicitly asks to remove field markup.
                       Use get_template_merge_fields or
                       get_template_form_fields to verify field markup after mutation.
                    3. CONVERT A DOCUMENT: call convert_document only. Pass the existing sessionId, or uploaded data
                       with sourceFormat when known, and outputFormat. Conversion must not inspect, summarize,
                       rewrite, restyle, render a new model, or use a recipe.
                    4. CREATE A NEW DOCUMENT: for ordinary fixed-content documents that fit headings, paragraphs,
                       lists, emphasis, and basic tables—including reports, proposals, agendas, letters, and invoices
                       with concrete line items—call create_document_from_markdown once with the complete raw Markdown.
                       Use one H1 title, H2/H3 hierarchy, and valid Markdown table rows; never send Base64 or wrap the
                       entire value in a code fence. The server imports and styles it. If the user requests a reusable
                       template, merge fields, repeating blocks, or form fields, call list_document_recipes and use a
                       matching recipe. Use create_document only for explicit fonts/colors/sizes, headers/footers,
                       images, fields, advanced layout, or structures Markdown cannot represent. Never call
                       create_empty_document first or assemble a complete draft with apply_operations.
        
                    Styling and page policy for creation: omit every style, font, color, spacing, table-style, page
                    size, margin, and alignment property the user did not explicitly request. The server fills all
                    omitted presentation properties from configured presets. Explicit user styling wins only for
                    the targeted elements; other elements still use presets. Omitted page size and margins always
                    use the configured default page layout. In semantic models and operations, always use real table
                    rows and cells rather than delimiter text. Preserve and reuse sessionId for every follow-up. A successful creation or edit returns the
                    authoritative sessionId. Never export after a failed call. Return tool results as a concise user
                    answer. Treat export metadata, especially downloadUri, as structured tool output for the client UI.
                    Do not repeat the raw downloadUri in prose unless the user explicitly asks for the raw URL, and
                    never construct or alter it. Never claim that a downloadable file exists unless an export or
                    conversion tool returned a successful structured result. Never print raw <tool_call> markup.
                    """;
            })
            .WithHttpTransport(options =>
            {
                // Recommended for servers that don't need server-to-client requests.
                options.Stateless = true;
            })
            .WithTools<DocumentTools>()
            .WithTools<ContentTools>()
            .WithTools<OperationTools>()
            .WithTools<TableTools>()
            .WithTools<ParagraphTools>()
            .WithTools<StyleTools>()
            .WithTools<SectionTools>()
            .WithTools<FieldTools>();
        return services;
    }

    /// <summary>Maps the MCP transport and structured download endpoints. Apply authorization to the returned group.</summary>
    /// <remarks>Call on the root application. Downloads are mapped at /exports; the host owns authentication and access policy.</remarks>
    public static RouteGroupBuilder MapTextControlMcp(this IEndpointRouteBuilder endpoints, string pattern = "/mcp")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("");
        group.MapPost(pattern.TrimEnd('/') + "/knowledge/extract", KnowledgeExtractionEndpoint.ExtractAsync);
        group.MapGet("/exports/{sessionId}/{exportId}", (
            string sessionId,
            string exportId,
            HttpContext context,
            DocumentWorkflowService workflow) =>
        {
            try
            {
                var export = workflow.GetDocumentExport(sessionId, exportId);
                context.Response.Headers.CacheControl = "private, no-store";
                return Results.File(
                    export.Path,
                    export.MimeType,
                    export.FileName,
                    enableRangeProcessing: true);
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException)
            {
                return Results.BadRequest();
            }
            catch (InvalidOperationException)
            {
                return Results.NotFound();
            }
        });
        group.MapMcp(pattern);
        return group;
    }
}
