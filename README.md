# TX Text Control MCP Document Server

A document automation MCP server for AI-driven workflows using **TX Text Control .NET Server**.

The server exposes TX Text Control document generation, inspection, editing, template, form-field, table, image, section, header/footer, and export capabilities through stateless MCP HTTP transport. Working documents are stored in **InternalUnicodeFormat** and can be exported as DOCX, PDF, HTML, Markdown, or TX internal format.

## Features

- Create and manage document sessions
- Load documents from base64 (auto format detection, including markdown edge case)
- Export documents as base64 (`tx`, `docx`, `pdf`, `html`, `md`)
- Discover the complete AI authoring contract through `get_authoring_guide`
- Format text by:
  - character range (`start`, `length`)
  - paragraph index (`paragraphIndex`)
- AI-friendly document operation framework:
  - neutral core document model (`Document`, `Section`, `Paragraph`, `Run`, `Table`, `Image`, `Style`, `HeaderFooter`, `Field`)
  - `render_document_model` for model-first document creation
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
- Content utilities:
  - read paragraphs
  - search by paragraph index
  - search by exact ranges (`start`, `length`)
  - extract full plain text
- Structured MCP tool error payloads

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

## MCP Endpoint

- Health: `GET /`
- Admin: `GET /admin`
- Automation config JSON: `GET /admin/automation`
- MCP: `POST /mcp`

Transport is configured as stateless HTTP.

## Available MCP Tools

### DocumentTools

- `create_document()`
- `load_from_base64(request, sessionId?)`
- `get_as_base64(request)`
- `get_session(sessionId)`
- `delete_session(sessionId)`

### ContentTools

- `format_text(sessionId, request)`
- `get_paragraphs(sessionId, start?, end?)`
- `search_text(sessionId, text?, matchCase?, wholeWord?)`
- `search_text_ranges(sessionId, text?, matchCase?, wholeWord?)`
- `get_text(sessionId)`

### OperationTools

- `get_document_automation_capabilities()`
- `get_authoring_guide()`
- `render_document_model(request)`
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

External AI clients should call `get_authoring_guide` first. It returns recommended workflows, operation-specific schemas and examples, style/table preset definitions, valid value sets, document model guidance, recipes, best practices, and troubleshooting notes.

Use `render_document_model` for new model-first draft documents. The model-first path applies configured defaults automatically: `document.title` is rendered with the configured title role, unstyled paragraphs and headers/footers use the configured body role, and unstyled tables receive the first configured table style preset. It supports simple table cell styles and uniform whole-cell run styles, but clients must inspect `warnings` for rich content that was flattened or not rendered with full fidelity. Use `apply_operations` for precise incremental edits, table header/cell formatting, images, fields, merge blocks, form fields, sections, headers/footers, and targeted formatting.

Operation-first document creation also applies semantic defaults: the first unstyled body paragraph receives the configured title role, later unstyled paragraphs receive the configured body role, and `append_table` applies the first configured table style preset unless a table `styleName` is explicitly supplied.

Session continuity rule for external AI clients:

- If the user asks to change, modify, update, edit, adjust, make, increase, decrease, replace, or refers to the current/same/that/previous document, reuse the existing `sessionId`.
- Inspect the existing document first with tools such as `get_document_structure`, `get_document_tables`, or `get_document_model`.
- Do not create a new document/session unless the user explicitly asks for a new document.

Style omission policy for external AI clients:

- If the user prompt does not explicitly mention styling, fonts, colors, sizes, spacing, borders, alignment, or named styles/presets, omit all style-related properties.
- Do not invent `styleName`, `style`, `paragraphStyle`, `paragraph`, `cellStyle`, `tableStyleName`, font, color, size, border, spacing, or alignment values.
- Let the server apply configured defaults automatically.
- Send style information only when the user explicitly asks for it or names a configured style/preset.
- Always inspect `render_document_model` warnings. If warnings mention table cell content, spans, mixed inline styles, fields, form fields, or images in cells, use `apply_operations` for exact output.

## Capability Packs

Capability packs are first-class modules represented by `ICapabilityPack`. A pack declares its name, AI-facing description, and supported operation types. Operation handlers still perform the work, but the registry uses pack metadata to expose enabled modules and reject disabled operations.

The `BasicText` capability pack includes:

- `define_style`
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
   - Use `append_merge_field` for body fields.
   - Use `append_merge_field` with `tableId`, `rowIndex`, and `columnIndex` for fields inside table cells.
   - Use `append_merge_block` to wrap a table row as a repeating merge block.
2. Confirm template fields and blocks with `get_template_merge_fields(sessionId)` and `get_template_merge_blocks(sessionId)`.
   - This reads actual TX `MERGEFIELD` `ApplicationFields` from the document, not just the neutral model.
3. Merge data with `merge_template`.
   - Provide either `request.jsonData` as a JSON string or `request.data` as a JSON object/array.
   - The implementation uses `TXTextControl.DocumentServer.MailMerge.MergeJsonData`.
4. Export the merged result with `get_as_base64`.

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

## Typical Workflow

1. Create session
   - `create_document`, `render_document_model`, or `apply_operations` with `createIfMissing`
2. Optional: load content
   - `load_from_base64`
3. Discover capabilities
   - `get_authoring_guide`
4. Edit/query content
   - `render_document_model`, `apply_operations`, `format_text`, `search_text`, `get_text`, inspection tools, etc.
5. Export
   - `get_as_base64`
6. Cleanup
   - `delete_session`

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

Create a new document session:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "create_document",
    "arguments": {}
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

`get_authoring_guide()` is the safest starting point for external AI clients. The response includes:

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

The response includes the `sessionId` and per-operation results. Use `get_as_base64` afterward to export the session as `docx`, `pdf`, `html`, `md`, or `tx`.

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

## External Configuration

`appsettings.json` contains a `DocumentAutomation` section:

- `EnabledCapabilityPacks` controls feature groups such as `BasicText`, `Media`, `Tables`, `Fields`, `Sections`, and `HeaderFooter`.
- `EnabledOperations` controls individual operation types.
- `DefaultParagraphStyleName` defines the fallback paragraph style.
- `StyleRoles` maps semantic roles such as title, heading1, heading2, and body to configured style names.
- `StylePresets` defines reusable paragraph/text style names that AI clients can use without redefining them in every request.
- `TableStylePresets` defines default table header/body/alternating-row styles, cell backgrounds, and borders. For `render_document_model`, the first configured table preset is applied when a table has no explicit `styleName`.

The `/admin` page exposes editable capability packs, operations, style presets, and table presets. The `/admin/automation` JSON endpoint exposes the currently configured automation surface.

## Notes

- TX Text Control licensing is required for full runtime functionality.
- Errors from tools are returned as structured MCP payloads (`code`, `message`, `isError`).

## License

See repository license terms and TX Text Control licensing terms.
