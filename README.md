# TX Text Control MCP Document Server

## Reusable server package

The standalone application now consumes the **TXTextControl.McpServer** hosting library.
Its admin pages and static assets remain in this application. The reusable package includes
the document tools, engine, worker pool, configuration services, and export endpoints.

See [package usage](src/TXTextControl.McpServer/README.md) for the hosting API and worker bootstrap,
and [packaging](docs/packaging.md) for signing, license, icon, and local package verification.
The [minimal host](samples/MinimalMcpHost/Program.cs) demonstrates using the library without the admin UI.


A document automation MCP server for AI-driven workflows using **TX Text Control .NET Server**.

The server exposes four primary workflows—inspect, edit, convert, and create—plus advanced TX Text Control document automation through stateless MCP HTTP transport. Working documents are stored in **InternalUnicodeFormat** and can be exported as TX, RTF, DOCX, PDF, HTML, Markdown, or plain text.

## Features

- Create and manage document sessions
- Create ordinary fixed-content documents from semantic Markdown in one server-owned import and styling pass
- Load documents from base64 (auto format detection, including markdown edge case)
- Stream document exports over HTTP without Base64 overhead (`tx`, `rtf`, `docx`, `pdf`, `html`, `md`, `txt`)
- Retain Base64 import/export for compatibility clients
- Discover lightweight server-owned recipes through `list_document_recipes`
- Execute standard document recipes through `create_document_from_recipe`
- Discover the complete AI authoring contract through `get_authoring_guide`
- Format text by:
  - character range (`start`, `length`)
  - paragraph index (`paragraphIndex`)
- AI-friendly document operation framework:
  - neutral core document model (`Document`, `Section`, `Paragraph`, `Run`, `Table`, `Image`, `Style`, `HeaderFooter`, `Field`)
  - `create_document` for model-first document creation
  - `apply_operations` for semantic incremental editing
  - external style presets, table style presets, and enabled operations
  - first-class capability packs with operation metadata
  - text, table, media, field, section, and header/footer operations
  - TX-supported image insertion in body, headers, and footers
  - template workflows with TX Text Control `MailMerge.MergeJsonData`
  - capability and authoring-guide inspection before authoring
- Admin UI:
  - login-protected admin area
  - enable/disable capability packs and operations
  - edit style presets and table style presets
  - inspect MCP endpoint information
- Primary content tools:
- `inspect_document` returns relevant text with stable zero-based paragraph indexes
- `classify_document` returns a fast live-document category and category-specific suggested actions without an LLM call
  - `edit_document` replaces exact text, one paragraph, a paragraph range, or a character range
  - `convert_document` performs direct TX Text Control conversion without model rewriting or styling
- Structured MCP tool error payloads
- Bounded, isolated persistent document workers with strict per-session ordering and crash recovery

## Tech Stack

- .NET 10
- C#
- ASP.NET Core Minimal API
- Model Context Protocol (MCP) .NET SDK
- TX Text Control .NET Server

## Project Structure

- `Program.cs` - app bootstrap and MCP registration
- `Tools/DocumentTools.cs` - session/document lifecycle tools
- `Tools/ContentTools.cs` - content/query tools
- `Tools/OperationTools.cs` - AI-facing document operation tools
- `Services/Engines/ServerTextControlDocumentEngine.cs` - TX Text Control engine entry point
- `Services/Engines/ServerTextControlDocumentEngine.Content.cs` - content operations
- `Services/Engines/ServerTextControlDocumentEngine.Operations.cs` - operation framework execution
- `Services/Operations/` - operation framework, grouped by capability pack
- `Services/AuthoringGuideService.cs` - self-contained external AI authoring guide
- `Models/DocumentModel/` - neutral AI-facing document model
- `Tests/` - xUnit test project for framework and model behavior
- `Services/Workers/` - versioned process protocol, bounded scheduler, affinity, recovery, and metrics
- `Benchmarks/` - repeatable MCP latency and throughput benchmark harness

## MCP Endpoint

- Health: `GET /`
- Admin: `GET /admin`
- Automation config JSON: `GET /admin/automation`
- MCP: `POST /mcp`
- Export download: `GET /exports/{sessionId}/{exportId}` (supports HTTP range requests)
- Worker health: `GET /health/document-workers`
- Worker diagnostics: `GET /admin/document-workers` (authenticated)

Transport is configured as stateless HTTP.

`create_document_export` returns structured metadata and a download URI. The generated artifact is stored inside the document session and expires with that session. The download response is streamed by ASP.NET Core, supports range requests, and uses `Cache-Control: private, no-store`.

## Performance behavior

- Session metadata is cached in memory after its first read and persisted as compact JSON.
- New `create_document` and `apply_operations` sessions are created and populated in one TX engine pass instead of creating and reloading an intermediate blank document.
- Content inspection reuses a per-session text/paragraph snapshot, so consecutive `inspect_document`, `get_text`, `get_paragraphs`, and `search_text` calls do not reload the TX document.
- Re-uploading identical source bytes and source format to the same session is detected with SHA-256 and does not import or rewrite the document again.
- Uploaded-content conversion loads the source, persists the working TX document, and writes the requested output in one TX Text Control instance.
- Merge-field, merge-block, and form-field inspection share one template snapshot. `merge_template` captures its before/after state in a single TX document load.
- Mutations are serialized per session, while unrelated sessions can execute concurrently.
- Use `create_document_export` plus the `/exports/...` download URI for production downloads. `get_as_base64` remains available for compatibility but adds Base64 allocation and transfer overhead.
- MCP requests taking at least one second are logged as `McpPerformance` warnings. Enable `Debug` logging for that category to record all MCP request timings.

Document engine calls run in a bounded pool of persistent child processes by default. A hot worker keeps one session document loaded, while the canonical TX snapshot and revision remain durable. Restarting the MCP host drops only in-memory caches; persisted sessions and completed mutation records are restored. See [the worker-pool architecture and benchmark report](docs/document-worker-pool.md).

## Available MCP Tools

### DocumentTools

- `create_empty_document()` — advanced lifecycle tool; not normal document creation
- `load_document(request, sessionId?)` — imports TX, RTF, DOCX, HTML, PDF, Markdown, or plain text; specify `sourceFormat` when known
- `convert_document(request)` — the only tool needed for pure format conversion
- `get_as_base64(request)` — compatibility export
- `create_document_export(request)` (preferred for AI and high-performance clients)
- `get_session(sessionId)`
- `delete_session(sessionId)`

### ContentTools

- `inspect_document(request)` — primary question/inspection tool; returns bounded indexed chunks with `truncated`, `nextParagraphIndex`, and `returnedCharacters` for token-safe paging
- `edit_document(request)` — primary deterministic text-edit tool
- `format_text(sessionId, request)`
- `get_paragraphs(sessionId, start?, end?)`
- `search_text(sessionId, text?, matchCase?, wholeWord?)`
- `search_text_ranges(sessionId, text?, matchCase?, wholeWord?)`
- `get_text(sessionId)`

### OperationTools

- `get_document_automation_capabilities()`
- `create_document_from_markdown(request)` — preferred creation path for ordinary fixed-content documents
- `apply_document_preset_styles(request)` — normalizes an imported document with configured page, paragraph, and table presets
- `list_document_recipes()`
- `create_document_from_recipe(request)`
- `get_authoring_guide()`
- `create_document(request)`
- `apply_operations(request)`
- `get_document_model(sessionId)`
- `get_document_structure(sessionId)`
- `get_document_styles(sessionId)`
- `get_document_tables(sessionId)`
- `get_document_fields(sessionId)`
- `get_document_headers_footers(sessionId)`
- `get_template_merge_fields(sessionId)`
- `get_template_merge_blocks(sessionId)`
- `get_template_form_fields(sessionId)`
- `merge_template(request)`

For an ordinary fixed-content document—such as a report, letter, proposal, agenda, article, or invoice with concrete line items—external AI clients should call `create_document_from_markdown` once. Send complete raw Markdown with one H1 title, H2/H3 hierarchy, lists, emphasis, and valid Markdown tables as appropriate. The server imports the content, maps it to configured paragraph roles, creates native TX tables, applies the default page layout and table preset, and returns the new `sessionId`. When an output format was requested, call `create_document_export` separately with that session.

Use `list_document_recipes` and `create_document_from_recipe` for reusable templates and advanced server-owned semantics such as merge fields, repeating blocks, and form fields. This keeps operation payloads, field names, layouts, styles, and repeating-block definitions on the MCP server.

Call `get_authoring_guide` only when the model needs to construct a custom document or operation sequence. It returns recommended workflows, operation-specific schemas and examples, style/table preset definitions, valid value sets, document model guidance, recipes, best practices, and troubleshooting notes.

Use `create_document` only when a new document requires explicit fonts, colors, sizes, advanced layout, headers/footers, images, fields, or structures Markdown cannot represent. The model-first path applies configured defaults automatically: `document.title` is rendered with the configured title role, unstyled paragraphs and headers/footers use the configured body role, unstyled tables receive the first configured table style preset, and sections without a page layout receive the configured default. It supports simple table cell styles and uniform whole-cell run styles, but clients must inspect `warnings` for rich content that was flattened or not rendered with full fidelity. Use `apply_operations` for precise incremental edits, table header/cell formatting, images, fields, merge blocks, form fields, sections, headers/footers, and targeted formatting.

Operation-first document creation also applies semantic defaults: the first unstyled body paragraph receives the configured title role, later unstyled paragraphs receive the configured body role, and `append_table` applies the first configured table style preset unless a table `styleName` is explicitly supplied.

Session continuity rule for external AI clients:

- If the user asks to change, modify, update, edit, adjust, make, increase, decrease, replace, or refers to the current/same/that/previous document, reuse the existing `sessionId`.
- Inspect the existing document first with `inspect_document`; use specialized structure/table/model tools only when needed.
- Do not create a new document/session unless the user explicitly asks for a new document.

For paragraph-level changes, use the focused `format_paragraph` tool. It supports alignment (`left`, `right`, `center`, or `justify`), paragraph spacing, line spacing, and named styles. Target a paragraph by an MCP zero-based `paragraphIndex`, by `matchText`, or explicitly with `allParagraphs`. Browser-editor selections must use the selected text as `matchText` and the browser start only as `nearTextPosition`; the server resolves the closest TX match and its containing paragraph. Do not translate browser offsets into paragraph indexes.

For table appearance changes, use the focused `format_table` tool. Its `scope` can be `selectedCells`, `header`, `cell`, `row`, `column`, or `table`. In an editor, send the exact selection as `matchText`, the browser start as `nearTextPosition`, and the browser selection length as `selectionLength`; the server maps that hint to authoritative TX table cells. For example, a red selected-cell background uses `scope: "selectedCells"` and `backgroundColorHex: "#FF0000"`; a green header in the selected table uses `scope: "header"` and `backgroundColorHex: "#008000"`.

`get_document_tables` reads tables from the live TX document and returns an explicit `tableCount` plus ordered `tableIndex`, `tableNumber`, actual table id, dimensions, and cells. Use `add_table_rows` for ordinal requests such as “add 5 more rows to the second table” (`tableNumber: 2`, `count: 5`) or supply concrete `rows`. This works for uploaded documents even when no neutral AI table model exists.

Style omission policy for external AI clients:

- If the user prompt does not explicitly mention styling, fonts, colors, sizes, spacing, borders, alignment, or named styles/presets, omit all style-related properties.
- Do not invent `styleName`, `style`, `paragraphStyle`, `paragraph`, `cellStyle`, `tableStyleName`, font, color, size, border, spacing, or alignment values.
- Let the server apply configured defaults automatically.
- Send style information only when the user explicitly asks for it or names a configured style/preset.
- Always inspect `create_document` warnings. If warnings mention table cell content, spans, mixed inline styles, fields, form fields, or images in cells, use `apply_operations` for exact output.

Named paragraph styles have focused MCP tools:

- `list_document_styles` returns the native TX styles, exact names, inheritance, following style, formatting, built-in status, and live usage count.
- `set_document_style` creates a style or changes an existing style. Existing styles are committed with `ParagraphStyle.Apply()`, so all linked paragraphs update automatically.
- `apply_document_style` links one or more target paragraphs to an existing style.
- `rename_document_style` and `delete_document_style` preserve or explicitly replace paragraph links.
- `create_styles_from_paragraphs` compares common character and paragraph formatting, reuses equivalent styles, and creates styles for previously unseen formatting groups. Mixed-format paragraphs are deliberately skipped so inline emphasis is not lost.

For example, “change the style Heading 1 to have red text” maps to `set_document_style` with `styleName: "Heading 1"` and `text.colorHex: "#FF0000"`. It must not be translated into repeated direct-formatting calls.

## Capability Packs

Capability packs are first-class modules represented by `ICapabilityPack`. A pack declares its name, AI-facing description, and supported operation types. Operation handlers still perform the work, but the registry uses pack metadata to expose enabled modules and reject disabled operations.

The `BasicText` capability pack includes:

- `define_style`
- `rename_style`
- `delete_style`
- `create_styles_from_paragraphs`
- `append_paragraph`
- `apply_style_to_paragraph`
- `format_paragraphs`
- `format_text_occurrences`
- `replace_text`

The `Media` capability pack includes:

- `append_image`

`append_image` accepts TX Text Control supported image formats: BMP, TIF/TIFF, WMF, PNG, JPG/JPEG, GIF, EMF, and SVG. Unsupported formats such as WebP are rejected with a structured tool error. Images can be supplied through `imagePath`, `imageBase64`, or a `data:image/...;base64,...` URI. TX runtime sizing uses `horizontalScaling` and `verticalScaling`; `width` and `height` are retained as neutral-model metadata only.

Use `target` to choose where the image is inserted. Supported values are `body`, `header`, `footer`, `firstPageHeader`, `firstPageFooter`, `evenHeader`, and `evenFooter`. The default is `body`.

The `Tables` capability pack includes:

- `append_table`
- `set_table_cell_text`
- `format_table_cell`
- `format_table_header_row`
- `format_table_column`
- `apply_table_style_preset`
- `add_table_row`

Table presets are configured in `DocumentAutomation.TableStylePresets`. User instructions override defaults, so an explicit request such as “make the table header pink” should be applied after the default table preset.

The `Fields` capability pack includes:

- `append_merge_field`
- `update_merge_field`
- `clear_application_fields`
- `append_merge_block`
- `append_form_field`
- `update_form_field`
- `clear_form_fields`

For LLM clients, the MCP also exposes focused tools that avoid constructing the larger `apply_operations` payload:

- `insert_merge_field` inserts genuine `MERGEFIELD` markup by matching text, replacing an inspected character range, using an absolute text position, targeting a paragraph or table cell, or targeting a header/footer.
- `insert_form_field` provides the same positional targets for real TX Text Control form fields.
- `create_merge_block` wraps an existing table row, character range, or paragraph range in a `txmb_` `SubTextPart`.
- `update_merge_field` and `update_form_field` update every matching real field in the body, headers, and footers and fail instead of reporting success when the name does not exist.
- `clear_application_fields` and `clear_form_fields` remove only their respective field kind; `keepText` defaults to `true`.

When replacing party names or placeholders, use `insert_merge_field` with `matchText` and `replaceAll` (or `occurrenceIndex`). Do not insert `{{name}}` as plain text; that is not a merge field.

Form fields support text, selection/dropdown/combobox, checkbox, and date fields. Merge blocks are TX Text Control `SubTextPart` objects named `txmb_<blockName>`.

The `Sections` capability pack includes:

- `insert_section_break`
- `set_section_layout`

Set page size and margins before inserting wide tables because TX Text Control tables do not automatically adapt after page size changes.

The `HeaderFooter` capability pack includes:

- `set_header_footer`

## Template / Mail Merge Workflow

Build templates with the existing field operations, inspect the real TX Text Control `ApplicationFields`, then merge data through the TX Text Control `MailMerge` class:

1. Create a template with `apply_operations`.
   - Prefer `insert_merge_field` for fields in body text, paragraphs, table cells, headers, or footers. It can replace all matching text or a stale-checked character range.
   - Prefer `insert_form_field` for positional fillable controls.
   - Prefer `create_merge_block` to wrap a table row, character range, or paragraph range.
   - The corresponding `append_*` operations remain available inside `apply_operations` for batching.
2. Confirm template fields and blocks with `get_template_merge_fields(sessionId)` and `get_template_merge_blocks(sessionId)`.
   - This reads actual TX `MERGEFIELD` `ApplicationFields` from the document, not just the neutral model.
3. Merge data with `merge_template`.
   - Provide either `request.jsonData` as a JSON string or `request.data` as a JSON object/array.
   - The implementation uses `TXTextControl.DocumentServer.MailMerge.MergeJsonData`.
4. Export the merged result with `create_document_export`. Use `get_as_base64` only for compatibility clients.

Example merge request:

```json
{
  "sessionId": "...",
  "data": {
    "CustomerName": "ACME Corp",
    "InvoiceNumber": "INV-1001",
    "lineItems": [
      {
        "ItemName": "TX Fuel",
        "Description": "Document automation package",
        "Quantity": "2",
        "UnitPrice": "199.00",
        "LineTotal": "398.00"
      }
    ]
  }
}
```

## Intent-routed workflows

Classify each request first. Do not mix these workflows unless the user explicitly combines intents.

1. Ask a question about an upload
   - Call `load_document` once and retain its `sessionId`.
   - Call `inspect_document` with that `sessionId` and the question in `query`.
   - Answer only from the returned indexed paragraphs. Do not edit or export.
2. Modify an upload or current document
   - Reuse the existing `sessionId` and call `inspect_document` to resolve exact text or paragraph indexes.
   - Call `edit_document` for replacement by exact text, paragraph index/range, or character range.
   - Use `format_text` or `apply_operations` only for formatting and structural mutations not supported by `edit_document`. Never recreate the document.
3. Convert a document
   - Call `convert_document` only. Supply either `data` plus the known `sourceFormat`, or an existing `sessionId`, and the `outputFormat`.
   - Return its `downloadUri`. Do not inspect, summarize, rewrite, restyle, or use a recipe.
4. Create a new document
   - For a standard type, use `list_document_recipes` and `create_document_from_recipe` when a matching recipe exists.
   - Otherwise call `create_document` once with the complete semantic model.
   - Omit presentation properties not requested by the user. The server fills missing title, heading, body, table, page-size, and margin settings from presets.
5. Export a created or edited document
   - Call `create_document_export` (preferred) or `get_as_base64` (compatibility) with the same `sessionId`.

## Build and Run

```bash
dotnet build
dotnet run --urls http://localhost:5000
```

Run tests:

```bash
dotnet test TxTextControl.McpServer.sln
```

## Example MCP Call (JSON-RPC)

Create a complete new document:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "create_document",
    "arguments": {
      "request": {
        "createIfMissing": true,
        "document": {
          "title": "Example",
          "sections": [
            {
              "blocks": [
                {
                  "type": "paragraph",
                  "paragraph": { "role": "body", "text": "Body text" }
                }
              ]
            }
          ]
        }
      }
    }
  }
}
```

Use the returned `sessionId` in subsequent tool calls.

## FormatText Request Shape

`format_text(sessionId, request)` accepts:

- `start` (int, optional)
- `length` (int, optional)
- `paragraphIndex` (int, optional)
- `bold` (bool)
- `italic` (bool)
- `underline` (bool)
- `color_hex` (string, optional, e.g. `#FF0000`)
- `font_name` (string, optional)
- `font_size` (float, optional, points)

> Use either `start`+`length` **or** `paragraphIndex`.

## Authoring Guide

Use `list_document_recipes()` as the lightweight starting point for standard document types. It returns only recipe names and goals. Pass a selected name to `create_document_from_recipe({ "recipeName": "..." })`; the server executes its authoritative recipe and returns compact session and template metadata.

Use `get_authoring_guide()` when a client needs the complete authoring contract for custom document construction. The response includes:

- `recommendedWorkflow`
- `toolMap`
- `documentModelContract`
- `operationSchemas`
- `styleRoles`
- `stylePresets`
- `tableStylePresets`
- `stylePolicy`
- `sessionPolicy`
- `valueSets`
- `recipes`
- `bestPractices`
- `troubleshooting`

## RenderDocumentModel Request Shape

Create a document from a neutral model:

```json
{
  "createIfMissing": true,
  "document": {
    "styles": [
      {
        "name": "Title",
        "type": "paragraph",
        "text": {
          "fontName": "Arial",
          "fontSize": 30,
          "fontSizeUnit": "pt",
          "bold": true
        }
      }
    ],
    "sections": [
      {
        "pageLayout": {
          "pageSize": "Letter",
          "orientation": "portrait",
          "unit": "in",
          "marginLeft": 1,
          "marginRight": 1,
          "marginTop": 1,
          "marginBottom": 1
        },
        "blocks": [
          {
            "type": "paragraph",
            "paragraph": {
              "styleName": "Title",
              "runs": [
                { "text": "Quarterly Report" }
              ]
            }
          }
        ]
      }
    ]
  }
}
```

## ApplyOperations Request Shape

Create a styled document in one AI-friendly call:

```json
{
  "createIfMissing": true,
  "operations": [
    {
      "type": "define_style",
      "style": {
        "name": "Heading",
        "fontName": "Arial",
        "fontSize": 20,
        "fontSizeUnit": "px",
        "bold": true
      }
    },
    {
      "type": "define_style",
      "style": {
        "name": "Paragraph-Text",
        "fontName": "Arial",
        "fontSize": 12,
        "fontSizeUnit": "px",
        "bold": false
      }
    },
    {
      "type": "append_paragraph",
      "styleName": "Heading",
      "text": "This is my title"
    },
    {
      "type": "append_paragraph",
      "styleName": "Paragraph-Text",
      "text": "This is the paragraph text."
    }
  ]
}
```

The response includes the `sessionId` and per-operation results. Use `create_document_export` afterward to export the session as `tx`, `rtf`, `docx`, `pdf`, `html`, `md`, or `txt`.

Create and style a table:

```json
{
  "createIfMissing": true,
  "operations": [
    {
      "type": "set_section_layout",
      "pageSize": "Letter",
      "unit": "in",
      "marginLeft": 1,
      "marginRight": 1,
      "marginTop": 1,
      "marginBottom": 1
    },
    {
      "type": "append_table",
      "tableId": "10",
      "rows": [
        ["Country", "Sales", "Qty"],
        ["Germany", "$842,000", "1280"],
        ["USA", "$1,240,000", "1985"]
      ]
    },
    {
      "type": "apply_table_style_preset",
      "tableId": "10",
      "styleName": "Professional Blue"
    },
    {
      "type": "format_table_header_row",
      "tableId": "10",
      "style": {
        "bold": true,
        "colorHex": "#FFFFFF"
      },
      "cellStyle": {
        "backgroundColorHex": "#D0006F",
        "border": {
          "width": 10,
          "colorHex": "#000000"
        }
      }
    }
  ]
}
```

Add a TX-supported image:

```json
{
  "sessionId": "...",
  "operations": [
    {
      "type": "append_image",
      "imagePath": "C:\\Images\\photo.png",
      "altText": "Product photo",
      "horizontalScaling": 75,
      "verticalScaling": 75,
      "alignment": "centered",
      "insertionMode": "displaceText"
    }
  ]
}
```

Add an embedded base64/data URI image:

```json
{
  "sessionId": "...",
  "operations": [
    {
      "type": "append_image",
      "imageBase64": "data:image/png;base64,...",
      "altText": "Embedded product photo",
      "horizontalScaling": 50,
      "verticalScaling": 50
    }
  ]
}
```

Insert a floating image at a page location:

```json
{
  "sessionId": "...",
  "operations": [
    {
      "type": "append_image",
      "imagePath": "C:\\Images\\watermark.png",
      "pageNumber": 1,
      "locationX": 25,
      "locationY": 40,
      "locationUnit": "mm",
      "insertionMode": "belowText"
    }
  ]
}
```

Add a logo to a header:

```json
{
  "sessionId": "...",
  "operations": [
    {
      "type": "set_header_footer",
      "headerFooterType": "header",
      "text": "Company Report "
    },
    {
      "type": "append_image",
      "target": "header",
      "imagePath": "C:\\Images\\logo.png",
      "altText": "Company logo",
      "horizontalScaling": 40,
      "verticalScaling": 40
    }
  ]
}
```

Use `get_document_model(sessionId)` to inspect the neutral document tree captured for the session. The model is intentionally independent from TX Text Control runtime classes so additional operation handlers can target a stable structure first and render through TX Text Control afterward.

## Markdown-first document creation

For ordinary new documents, call `create_document_from_markdown` with the complete raw Markdown. Do not
Base64-encode it and do not wrap the whole value in a Markdown code fence. A typical request contains one
H1 title, H2/H3 headings, paragraphs, lists, emphasis, and pipe tables. In one TX engine pass, the server:

1. imports the Markdown content;
2. materializes pipe tables as native TX Text Control tables;
3. maps H1/H2/H3 and body text to the configured Title/Heading1/Heading2/Body roles;
4. applies the configured default page layout and first table style preset; and
5. persists the document and returns its `sessionId`.

Call `create_document_export` with the returned session only when the user requests an output file. Keeping
creation and export as two explicit tools makes the saved artifact verifiable without asking the model to
reconstruct the document.

## Styling an imported document with configured presets

After `load_document`, call `apply_document_preset_styles` with the returned `sessionId` when the user asks
to apply, normalize, or polish the document using server presets. The tool preserves content and session
identity, applies the configured default page layout, maps imported Markdown H1/H2/H3 hierarchy to the
configured Title/Heading1/Heading2 roles, applies the configured Body style to remaining paragraphs, and
styles native tables with the first configured table preset. The model does not need to inspect the document
or generate one formatting operation per paragraph or table cell.

## External Configuration

`appsettings.json` contains a `DocumentAutomation` section:

- `EnabledCapabilityPacks` controls feature groups such as `BasicText`, `Media`, `Tables`, `Fields`, `Sections`, and `HeaderFooter`.
- `EnabledOperations` controls individual operation types.
- `DefaultParagraphStyleName` defines the fallback paragraph style.
- `StyleRoles` maps semantic roles such as title, heading1, heading2, and body to configured style names.
- `StylePresets` defines reusable paragraph/text style names that AI clients can use without redefining them in every request.
- `DefaultPageLayout` defines the page size, orientation, and margins used whenever creation omits them.
- `TableStylePresets` defines default table header/body/alternating-row styles, cell backgrounds, and borders. For `create_document`, the first configured table preset is applied when a table has no explicit `styleName`.

The `/admin` page exposes editable capability packs, operations, style presets, and table presets. The `/admin/automation` JSON endpoint exposes the currently configured automation surface.

## Notes

- TX Text Control licensing is required for full runtime functionality.
- Errors from tools are returned as structured MCP payloads (`code`, `message`, `isError`).

## License

See repository license terms and TX Text Control licensing terms.
