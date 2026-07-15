using System.Collections.Generic;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class TableCapabilityPack : ICapabilityPack
{
    public const string PackName = "Tables";
    public const string AppendTable = "append_table";
    public const string SetTableCellText = "set_table_cell_text";
    public const string FormatTableCell = "format_table_cell";
    public const string FormatTableHeaderRow = "format_table_header_row";
    public const string FormatTableColumn = "format_table_column";
    public const string ApplyTableStylePreset = "apply_table_style_preset";
    public const string AddTableRow = "add_table_row";

    public string Name => PackName;

    public string Description =>
        "Creates simple document tables from AI-friendly row and cell data.";

    public IReadOnlyCollection<string> OperationTypes { get; } =
    [
        AppendTable,
        SetTableCellText,
        FormatTableCell,
        FormatTableHeaderRow,
        FormatTableColumn,
        ApplyTableStylePreset,
        AddTableRow
    ];
}
