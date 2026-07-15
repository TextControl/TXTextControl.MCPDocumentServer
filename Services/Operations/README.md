# Document Operations

This folder contains the AI-facing document operation framework. Namespaces intentionally remain `TxTextControl.McpServer.Services.Operations` across subfolders so handlers can move by capability without changing public code references.

- `Abstractions/` - operation handler, capability pack, context, and registry contracts
- `Common/` - shared formatting and descriptor helpers
- `Text/` - styles, paragraphs, text replacement, and occurrence formatting
- `Tables/` - table creation, row/cell editing, cell formatting, backgrounds, and borders
- `Fields/` - TX Text Control application/merge field operations
- `HeaderFooters/` - header and footer operations
- `Media/` - image insertion operations
