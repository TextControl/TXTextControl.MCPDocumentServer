using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Tools;

/// <summary>Model-friendly table operations.</summary>
[McpServerToolType]
public sealed class TableTools
{
    private readonly DocumentWorkflowService _workflow;

    /// <summary>Initializes table tools for the document workflow.</summary>
    public TableTools(DocumentWorkflowService workflow)
    {
        _workflow = workflow;
    }

    /// <summary>Inserts one table into an existing document.</summary>
    [McpServerTool(
        Name = "insert_table",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Primary tool for adding a table to an existing document. Required: sessionId and rows, where rows is an array of row arrays and each inner value is one cell. The first row is treated as the header by the default table preset. Optional placement is end (default), before, or after; paragraphIndex is required for before/after. Optional styleName and column widths should be omitted unless explicitly requested. This tool supplies the internal operation type automatically; do not use apply_operations for a simple table insertion.")]
    public object InsertTable(InsertTableRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
            return _workflow.ApplyOperations(new ApplyOperationsRequest
            {
                SessionId = request.SessionId,
                CreateIfMissing = false,
                Operations =
                [
                    new DocumentOperation
                    {
                        Type = TableCapabilityPack.AppendTable,
                        Rows = request.Rows,
                        TableId = request.TableId,
                        StyleName = request.StyleName,
                        ColumnWidths = request.ColumnWidths,
                        ColumnWidthUnit = request.ColumnWidthUnit,
                        ParagraphIndex = request.ParagraphIndex,
                        Placement = request.Placement
                    }
                ]
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    /// <summary>Formats cells in an existing table using semantic or selection-based targeting.</summary>
    [McpServerTool(
        Name = "format_table",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Primary tool for table formatting requests such as 'give the selected cells a red background' or 'make the selected table header green'. Required: sessionId, scope, and at least one formatting property. scope is selectedCells, header, cell, row, column, or table. With an editor selection, pass selectedText as matchText, browser start as nearTextPosition, and browser selection length as selectionLength; the server resolves the authoritative TX table/cells using TableCell.Start and Length. Do not call search_text_ranges and do not translate browser offsets into table, row, or column indexes. For header scope, rowIndex defaults to 0. Explicit targeting may use one-based tableNumber (preferred for imported documents) or tableId plus zero-based rowIndex/columnIndex. Cell appearance properties include backgroundColorHex, borderWidth/borderColorHex, padding, horizontalAlignment, and verticalAlignment. Text properties include bold, italic, underline, textColorHex, fontName, and fontSize. The result returns the actual tableId, tableNumber, cellCount, and cell coordinates changed.")]
    public object FormatTable(FormatTableRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Scope);

            bool hasTextStyle = request.Bold.HasValue
                                || request.Italic.HasValue
                                || request.Underline.HasValue
                                || !string.IsNullOrWhiteSpace(request.TextColorHex)
                                || !string.IsNullOrWhiteSpace(request.FontName)
                                || request.FontSize.HasValue;
            bool hasCellStyle = !string.IsNullOrWhiteSpace(request.BackgroundColorHex)
                                || request.BorderWidth.HasValue
                                || !string.IsNullOrWhiteSpace(request.BorderColorHex)
                                || request.PaddingLeft.HasValue
                                || request.PaddingRight.HasValue
                                || request.PaddingTop.HasValue
                                || request.PaddingBottom.HasValue
                                || !string.IsNullOrWhiteSpace(request.HorizontalAlignment)
                                || !string.IsNullOrWhiteSpace(request.VerticalAlignment);
            if (!hasTextStyle && !hasCellStyle)
            {
                throw new ArgumentException(
                    "At least one table text or cell formatting property is required.",
                    nameof(request));
            }

            TextStyleDefinition? textStyle = hasTextStyle
                ? new TextStyleDefinition
                {
                    Bold = request.Bold,
                    Italic = request.Italic,
                    Underline = request.Underline,
                    ColorHex = request.TextColorHex,
                    FontName = request.FontName,
                    FontSize = request.FontSize,
                    FontSizeUnit = request.FontSizeUnit
                }
                : null;
            CellStyleDefinition? cellStyle = hasCellStyle
                ? new CellStyleDefinition
                {
                    BackgroundColorHex = request.BackgroundColorHex,
                    Border = request.BorderWidth.HasValue || !string.IsNullOrWhiteSpace(request.BorderColorHex)
                        ? new CellBorderDefinition
                        {
                            Width = request.BorderWidth,
                            ColorHex = request.BorderColorHex
                        }
                        : null,
                    PaddingLeft = request.PaddingLeft,
                    PaddingRight = request.PaddingRight,
                    PaddingTop = request.PaddingTop,
                    PaddingBottom = request.PaddingBottom,
                    PaddingUnit = request.PaddingUnit,
                    HorizontalAlignment = request.HorizontalAlignment,
                    VerticalAlignment = request.VerticalAlignment
                }
                : null;

            return _workflow.ApplyOperations(new ApplyOperationsRequest
            {
                SessionId = request.SessionId,
                CreateIfMissing = false,
                Operations =
                [
                    new DocumentOperation
                    {
                        Type = TableCapabilityPack.FormatTableCell,
                        TableScope = request.Scope,
                        TableId = request.TableId,
                        TableNumber = request.TableNumber,
                        RowIndex = request.RowIndex,
                        ColumnIndex = request.ColumnIndex,
                        MatchText = request.MatchText,
                        OccurrenceIndex = request.OccurrenceIndex,
                        NearTextPosition = request.NearTextPosition,
                        SelectionLength = request.SelectionLength,
                        MatchCase = request.MatchCase,
                        Style = textStyle,
                        CellStyle = cellStyle
                    }
                ]
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    /// <summary>Adds rows to a table selected by its id or its one-based document order.</summary>
    [McpServerTool(
        Name = "add_table_rows",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Primary tool for requests such as 'add 5 more rows to the second table'. Required: sessionId and either tableId or one-based tableNumber. Supply count to add empty rows, or rows as an array of row arrays to add specific values. Call get_document_tables first when the user identifies a table by order or when multiple tables exist; it returns authoritative tableCount, tableNumber, tableId, rowCount, columnCount, and cell previews from the live TX document. Do not guess a table id.")]
    public object AddTableRows(AddTableRowsRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
            DocumentTablesResponse tables = _workflow.GetDocumentTables(request.SessionId);
            string? tableId = null;
            int? tableNumber = null;
            if (!string.IsNullOrWhiteSpace(request.TableId))
            {
                tableId = request.TableId.Trim();
                if (!tables.Tables.Any(table => table.Id.Equals(tableId, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException($"Table '{tableId}' was not found in the live document.");
                }
            }
            else if (request.TableNumber.HasValue)
            {
                if (request.TableNumber.Value < 1 || request.TableNumber.Value > tables.TableCount)
                {
                    throw new ArgumentException(
                        $"tableNumber must be between 1 and {tables.TableCount} for this document.");
                }

                tableNumber = request.TableNumber.Value;
            }
            else if (tables.TableCount == 1)
            {
                tableNumber = 1;
            }
            else
            {
                throw new ArgumentException(
                    $"Specify tableId or tableNumber. The live document contains {tables.TableCount} tables.");
            }

            List<List<string>> rows;
            if (request.Rows.Count > 0)
            {
                if (request.Count.HasValue && request.Count.Value != request.Rows.Count)
                {
                    throw new ArgumentException("count must equal rows.Count when both are supplied.");
                }

                rows = request.Rows;
            }
            else
            {
                int count = request.Count ?? 1;
                if (count < 1 || count > 1000)
                {
                    throw new ArgumentException("count must be between 1 and 1000.");
                }

                rows = Enumerable.Range(0, count)
                    .Select(_ => new List<string> { string.Empty })
                    .ToList();
            }

            return _workflow.ApplyOperations(new ApplyOperationsRequest
            {
                SessionId = request.SessionId,
                CreateIfMissing = false,
                Operations = rows.Select(row => new DocumentOperation
                {
                    Type = TableCapabilityPack.AddTableRow,
                    TableId = tableId,
                    TableNumber = tableNumber,
                    Rows = [row]
                }).ToList()
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }
}
