# TX Text Control MCP Document Server (Simplified)

A lightweight MCP server for document workflows using **TX Text Control .NET Server**.

This project exposes document/session tools over MCP HTTP transport and stores working documents in **InternalUnicodeFormat**.

## Features

- Create and manage document sessions
- Load documents from base64 (auto format detection, including markdown edge case)
- Export documents as base64 (`tx`, `docx`, `pdf`, `html`, `md`)
- Format text by:
  - character range (`start`, `length`)
  - paragraph index (`paragraphIndex`)
- Content utilities:
  - read paragraphs
  - search by paragraph index
  - search by exact ranges (`start`, `length`)
  - extract full plain text
- Structured MCP tool error payloads

## Tech Stack

- .NET 8
- C#
- ASP.NET Core Minimal API
- Model Context Protocol (MCP) .NET SDK
- TX Text Control .NET Server

## Project Structure

- `src/TxTextControl.McpServer/Program.cs` - app bootstrap and MCP registration
- `src/TxTextControl.McpServer/Tools/DocumentTools.cs` - session/document lifecycle tools
- `src/TxTextControl.McpServer/Tools/ContentTools.cs` - content/query tools
- `src/TxTextControl.McpServer/Services/Engines/ServerTextControlDocumentEngine.cs` - document operations
- `src/TxTextControl.McpServer/Services/Engines/ServerTextControlDocumentEngine.Content.cs` - content operations

## MCP Endpoint

- Health: `GET /`
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

## Typical Workflow

1. Create session
   - `create_document`
2. Optional: load content
   - `load_from_base64`
3. Edit/query content
   - `format_text`, `search_text`, `get_text`, etc.
4. Export
   - `get_as_base64`
5. Cleanup
   - `delete_session`

## Build and Run

```bash
dotnet build
dotnet run --project src/TxTextControl.McpServer
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

## Notes

- TX Text Control licensing is required for full runtime functionality.
- Errors from tools are returned as structured MCP payloads (`code`, `message`, `isError`).

## License

See repository license terms and TX Text Control licensing terms.
