using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Options;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Services;

public static class DocumentModelOperationCompiler
{
    public sealed class CompileResult
    {
        public ApplyOperationsRequest Request { get; init; } = new();
        public List<string> Warnings { get; init; } = [];
    }

    public static ApplyOperationsRequest Compile(
        RenderDocumentModelRequest request,
        DocumentAutomationOptions? options = null)
        => CompileDetailed(request, options).Request;

    public static CompileResult CompileDetailed(
        RenderDocumentModelRequest request,
        DocumentAutomationOptions? options = null)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Document is null)
        {
            throw new ArgumentException("'document' is required.", nameof(request));
        }

        var operations = new List<DocumentOperation>();
        var tableIds = CollectTableIds(request.Document);
        var defaultBodyStyleName = ResolveBodyStyleName(options);
        var titleStyleName = ResolveTitleStyleName(options);
        var defaultTableStyleName = ResolveDefaultTableStyleName(options);
        var styles = BuildStyleDictionary(request.Document, options);
        var warnings = new List<string>();

        foreach (var style in request.Document.Styles)
        {
            if (style.Text is not null)
            {
                style.Text.Name = string.IsNullOrWhiteSpace(style.Text.Name) ? style.Name : style.Text.Name;
                operations.Add(new DocumentOperation
                {
                    Type = "define_style",
                    Style = style.Text
                });
            }
        }

        for (var sectionIndex = 0; sectionIndex < request.Document.Sections.Count; sectionIndex++)
        {
            var section = request.Document.Sections[sectionIndex];
            if (sectionIndex > 0)
            {
                operations.Add(new DocumentOperation
                {
                    Type = SectionCapabilityPack.InsertSectionBreak,
                    BreakKind = "beginAtNewPage"
                });
            }

            if (section.PageLayout is not null)
            {
                operations.Add(new DocumentOperation
                {
                    Type = SectionCapabilityPack.SetSectionLayout,
                    SectionIndex = sectionIndex,
                    PageLayout = section.PageLayout
                });
            }

            if (section.Header is not null)
            {
                operations.AddRange(CompileHeaderFooter(section.Header, isHeader: true, defaultBodyStyleName));
            }

            if (section.Footer is not null)
            {
                operations.AddRange(CompileHeaderFooter(section.Footer, isHeader: false, defaultBodyStyleName));
            }

            if (sectionIndex == 0 && !string.IsNullOrWhiteSpace(request.Document.Title))
            {
                operations.Add(new DocumentOperation
                {
                    Type = BasicTextCapabilityPack.AppendParagraph,
                    Text = request.Document.Title.Trim(),
                    Runs =
                    [
                        new DocumentModel.Run
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Text = request.Document.Title.Trim()
                        }
                    ],
                    StyleName = titleStyleName
                });
            }

            foreach (var block in section.Blocks)
            {
                operations.AddRange(CompileBlock(block, defaultBodyStyleName, defaultTableStyleName, tableIds, styles, warnings));
            }
        }

        if (operations.Count == 0)
        {
            throw new ArgumentException("'document' must contain at least one style or supported content block.", nameof(request));
        }

        return new CompileResult
        {
            Request = new ApplyOperationsRequest
            {
                SessionId = request.SessionId,
                CreateIfMissing = request.CreateIfMissing,
                Operations = operations
            },
            Warnings = warnings
        };
    }

    private static IReadOnlyList<DocumentOperation> CompileBlock(
        DocumentModel.DocumentBlock block,
        string? defaultBodyStyleName,
        string? defaultTableStyleName,
        HashSet<int> tableIds,
        IReadOnlyDictionary<string, DocumentModel.TextStyleDefinition> styles,
        List<string> warnings)
        => block.Type.Trim().ToLowerInvariant() switch
        {
            "paragraph" => [CompileParagraph(block.Paragraph, defaultBodyStyleName)],
            "table" => CompileTable(block.Table, defaultTableStyleName, tableIds, styles, warnings),
            "image" => [CompileImage(block.Image)],
            "field" => [CompileField(block.Field)],
            _ => throw new NotSupportedException($"Unsupported document block type '{block.Type}'.")
        };

    private static DocumentOperation CompileParagraph(
        DocumentModel.Paragraph? paragraph,
        string? defaultBodyStyleName)
    {
        if (paragraph is null)
        {
            throw new ArgumentException("paragraph block requires paragraph content.");
        }

        return new DocumentOperation
        {
            Type = "append_paragraph",
            Text = string.Concat(paragraph.Runs.Select(run => run.Text ?? string.Empty)),
            Runs = paragraph.Runs
                .Select(run => new DocumentModel.Run
                {
                    Id = string.IsNullOrWhiteSpace(run.Id) ? Guid.NewGuid().ToString("N") : run.Id,
                    Text = run.Text ?? string.Empty,
                    StyleName = string.IsNullOrWhiteSpace(run.StyleName) ? null : run.StyleName.Trim(),
                    Style = run.Style
                })
                .ToList(),
            StyleName = string.IsNullOrWhiteSpace(paragraph.StyleName)
                ? defaultBodyStyleName
                : paragraph.StyleName.Trim()
        };
    }

    private static IReadOnlyList<DocumentOperation> CompileTable(
        DocumentModel.Table? table,
        string? defaultTableStyleName,
        HashSet<int> tableIds,
        IReadOnlyDictionary<string, DocumentModel.TextStyleDefinition> styles,
        List<string> warnings)
    {
        if (table is null)
        {
            throw new ArgumentException("table block requires table content.");
        }

        var tableId = ResolveTableId(table, tableIds);
        var operations = new List<DocumentOperation>
        {
            new()
            {
                Type = TableCapabilityPack.AppendTable,
                TableId = tableId,
                Rows = table.Rows
                    .Select(row => row.Cells
                        .Select(GetCellText)
                        .ToList())
                    .ToList()
            }
        };

        var tableStyleName = string.IsNullOrWhiteSpace(table.StyleName)
            ? defaultTableStyleName
            : table.StyleName.Trim();
        if (!string.IsNullOrWhiteSpace(tableStyleName))
        {
            operations.Add(new DocumentOperation
            {
                Type = TableCapabilityPack.ApplyTableStylePreset,
                TableId = tableId,
                StyleName = tableStyleName
            });
        }

        operations.AddRange(CompileTableCellFormatting(tableId, table, styles, warnings));

        return operations;
    }

    private static DocumentOperation CompileImage(DocumentModel.Image? image)
    {
        if (image is null)
        {
            throw new ArgumentException("image block requires image content.");
        }

        var operation = new DocumentOperation
        {
            Type = "append_image",
            AltText = image.AltText,
            Width = image.Width,
            Height = image.Height,
            Unit = image.Unit,
            HorizontalScaling = image.HorizontalScaling,
            VerticalScaling = image.VerticalScaling,
            InsertionMode = image.InsertionMode,
            Alignment = image.Alignment,
            LocationX = image.LocationX,
            LocationY = image.LocationY,
            LocationUnit = image.LocationUnit
        };

        if (image.Source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            operation.ImageBase64 = image.Source;
        }
        else
        {
            operation.ImagePath = image.Source;
        }

        return operation;
    }

    private static IReadOnlyList<DocumentOperation> CompileHeaderFooter(
        DocumentModel.HeaderFooter headerFooter,
        bool isHeader,
        string? defaultBodyStyleName)
    {
        var operations = new List<DocumentOperation>();
        var target = string.IsNullOrWhiteSpace(headerFooter.Type)
            ? (isHeader ? "header" : "footer")
            : headerFooter.Type;
        var paragraph = headerFooter.Blocks
            .Where(block => string.Equals(block.Type, "paragraph", StringComparison.OrdinalIgnoreCase))
            .Select(block => block.Paragraph)
            .FirstOrDefault(paragraph => paragraph is not null);

        if (paragraph is not null)
        {
            var text = string.Concat(paragraph.Runs.Select(run => run.Text ?? string.Empty));
            var includePageNumber = text.Contains("{PAGE}", StringComparison.OrdinalIgnoreCase);
            if (includePageNumber)
            {
                text = text.Replace("{PAGE}", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            operations.Add(new DocumentOperation
            {
                Type = "set_header_footer",
                HeaderFooterType = target,
                Text = text,
                Runs = paragraph.Runs
                    .Select(run => new DocumentModel.Run
                    {
                        Id = string.IsNullOrWhiteSpace(run.Id) ? Guid.NewGuid().ToString("N") : run.Id,
                        Text = (run.Text ?? string.Empty).Replace("{PAGE}", string.Empty, StringComparison.OrdinalIgnoreCase),
                        StyleName = string.IsNullOrWhiteSpace(run.StyleName) ? null : run.StyleName.Trim(),
                        Style = run.Style
                    })
                    .Where(run => !string.IsNullOrEmpty(run.Text))
                    .ToList(),
                StyleName = string.IsNullOrWhiteSpace(paragraph.StyleName)
                    ? defaultBodyStyleName
                    : paragraph.StyleName.Trim(),
                IncludePageNumber = includePageNumber
            });
        }

        foreach (var imageBlock in headerFooter.Blocks.Where(block => string.Equals(block.Type, "image", StringComparison.OrdinalIgnoreCase)))
        {
            var operation = CompileImage(imageBlock.Image);
            operation.Target = target;
            operations.Add(operation);
        }

        if (operations.Count == 0)
        {
            throw new NotSupportedException("HeaderFooter rendering currently supports paragraph and image blocks.");
        }

        return operations;
    }

    private static DocumentOperation CompileField(DocumentModel.Field? field)
    {
        if (field is null)
        {
            throw new ArgumentException("field block requires field content.");
        }

        if (!string.Equals(field.Type, "merge", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Unsupported field type '{field.Type}'.");
        }

        var parameters = new List<string>();
        if (field.Properties.TryGetValue("parameters", out var serializedParameters)
            && !string.IsNullOrWhiteSpace(serializedParameters))
        {
            parameters.AddRange(serializedParameters
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return new DocumentOperation
        {
            Type = "append_merge_field",
            FieldId = string.IsNullOrWhiteSpace(field.Id) ? null : field.Id.Trim(),
            FieldName = field.Name,
            FieldText = field.Value,
            Parameters = parameters
        };
    }

    private static string GetCellText(DocumentModel.TableCell cell)
        => string.Join(
            "\n",
            cell.Blocks
                .Where(block => string.Equals(block.Type, "paragraph", StringComparison.OrdinalIgnoreCase))
                .Select(block => block.Paragraph)
                .Where(paragraph => paragraph is not null)
                .Select(paragraph => string.Concat(paragraph!.Runs.Select(run => run.Text ?? string.Empty))));

    private static IReadOnlyList<DocumentOperation> CompileTableCellFormatting(
        string? tableId,
        DocumentModel.Table table,
        IReadOnlyDictionary<string, DocumentModel.TextStyleDefinition> styles,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(tableId))
        {
            return [];
        }

        var operations = new List<DocumentOperation>();
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
            {
                var cell = row.Cells[columnIndex];
                AddCellFidelityWarnings(tableId, cell, rowIndex, columnIndex, warnings);
                var style = ResolveWholeCellTextStyle(tableId, cell, rowIndex, columnIndex, styles, warnings);
                if (style is null && cell.CellStyle is null)
                {
                    continue;
                }

                operations.Add(new DocumentOperation
                {
                    Type = TableCapabilityPack.FormatTableCell,
                    TableId = tableId,
                    RowIndex = rowIndex,
                    ColumnIndex = columnIndex,
                    Style = style,
                    CellStyle = cell.CellStyle
                });
            }
        }

        return operations;
    }

    private static void AddCellFidelityWarnings(
        string? tableId,
        DocumentModel.TableCell cell,
        int rowIndex,
        int columnIndex,
        List<string> warnings)
    {
        var location = BuildCellLocation(tableId, rowIndex, columnIndex);
        if (cell.ColumnSpan > 1 || cell.RowSpan > 1)
        {
            warnings.Add($"{location}: columnSpan/rowSpan is not rendered by render_document_model yet.");
        }

        var paragraphCount = cell.Blocks.Count(block =>
            string.Equals(block.Type, "paragraph", StringComparison.OrdinalIgnoreCase));
        if (paragraphCount > 1)
        {
            warnings.Add($"{location}: multiple paragraph blocks are flattened into newline-separated cell text.");
        }

        foreach (var block in cell.Blocks)
        {
            var type = string.IsNullOrWhiteSpace(block.Type) ? "unknown" : block.Type.Trim();
            if (!string.Equals(type, "paragraph", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"{location}: table cell block type '{type}' is not rendered by render_document_model yet; use apply_operations for fields, form fields, images, or rich cell content.");
            }

            if (block.Paragraph?.ParagraphStyle is not null || !string.IsNullOrWhiteSpace(block.Paragraph?.Alignment))
            {
                warnings.Add($"{location}: paragraph-level formatting inside table cells is not rendered by render_document_model yet; use format_table_cell or follow-up operations.");
            }
        }
    }

    private static DocumentModel.TextStyleDefinition? ResolveWholeCellTextStyle(
        string? tableId,
        DocumentModel.TableCell cell,
        int rowIndex,
        int columnIndex,
        IReadOnlyDictionary<string, DocumentModel.TextStyleDefinition> styles,
        List<string> warnings)
    {
        var styledRuns = cell.Blocks
            .Where(block => string.Equals(block.Type, "paragraph", StringComparison.OrdinalIgnoreCase))
            .Select(block => block.Paragraph)
            .Where(paragraph => paragraph is not null)
            .SelectMany(paragraph => paragraph!.Runs)
            .Where(run => run.Style is not null || !string.IsNullOrWhiteSpace(run.StyleName))
            .ToList();

        if (styledRuns.Count == 0)
        {
            return null;
        }

        var allRuns = cell.Blocks
            .Where(block => string.Equals(block.Type, "paragraph", StringComparison.OrdinalIgnoreCase))
            .Select(block => block.Paragraph)
            .Where(paragraph => paragraph is not null)
            .SelectMany(paragraph => paragraph!.Runs)
            .Where(run => !string.IsNullOrEmpty(run.Text))
            .ToList();

        var location = BuildCellLocation(tableId, rowIndex, columnIndex);
        if (styledRuns.Count != allRuns.Count)
        {
            warnings.Add($"{location}: mixed styled and unstyled runs cannot be rendered with exact inline fidelity in table cells; use apply_operations for precise cell text styling.");
            return null;
        }

        var resolvedStyles = styledRuns
            .Select(run => ResolveRunStyle(run, styles))
            .Where(style => style is not null)
            .ToList();
        if (resolvedStyles.Count != styledRuns.Count)
        {
            warnings.Add($"{location}: one or more run styleName values could not be resolved; unresolved cell run styling was ignored.");
            return null;
        }

        var distinct = resolvedStyles
            .Select(StyleFingerprint)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (distinct.Count > 1)
        {
            warnings.Add($"{location}: multiple different run styles in one table cell cannot be rendered with exact inline fidelity; use apply_operations for precise cell text styling.");
            return null;
        }

        return CloneStyle(resolvedStyles[0]!);
    }

    private static DocumentModel.TextStyleDefinition? ResolveRunStyle(
        DocumentModel.Run run,
        IReadOnlyDictionary<string, DocumentModel.TextStyleDefinition> styles)
    {
        if (run.Style is not null)
        {
            return run.Style;
        }

        return !string.IsNullOrWhiteSpace(run.StyleName)
               && styles.TryGetValue(run.StyleName.Trim(), out var style)
            ? style
            : null;
    }

    private static string StyleFingerprint(DocumentModel.TextStyleDefinition style)
        => string.Join(
            "|",
            style.FontName ?? string.Empty,
            style.FontSize?.ToString("0.###") ?? string.Empty,
            style.FontSizeUnit ?? string.Empty,
            style.Bold?.ToString() ?? string.Empty,
            style.Italic?.ToString() ?? string.Empty,
            style.Underline?.ToString() ?? string.Empty,
            style.ColorHex ?? string.Empty);

    private static DocumentModel.TextStyleDefinition CloneStyle(DocumentModel.TextStyleDefinition style)
        => new()
        {
            Name = style.Name,
            FontName = style.FontName,
            FontSize = style.FontSize,
            FontSizeUnit = style.FontSizeUnit,
            Bold = style.Bold,
            Italic = style.Italic,
            Underline = style.Underline,
            ColorHex = style.ColorHex,
            Paragraph = style.Paragraph
        };

    private static string BuildCellLocation(string? tableId, int rowIndex, int columnIndex)
        => $"table '{(string.IsNullOrWhiteSpace(tableId) ? "<auto>" : tableId)}' cell ({rowIndex}, {columnIndex})";

    private static HashSet<int> CollectTableIds(DocumentModel.Document document)
        => document.Sections
            .SelectMany(section => section.Blocks)
            .Select(block => block.Table?.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => int.TryParse(id, out var parsed) ? parsed : 0)
            .Where(id => id >= 10)
            .ToHashSet();

    private static string? ResolveTableId(DocumentModel.Table table, HashSet<int> tableIds)
    {
        if (!string.IsNullOrWhiteSpace(table.Id))
        {
            return table.Id.Trim();
        }

        for (var candidate = 10; candidate <= short.MaxValue; candidate++)
        {
            if (tableIds.Add(candidate))
            {
                table.Id = candidate.ToString();
                return table.Id;
            }
        }

        throw new InvalidOperationException("No TX Text Control table ids are available.");
    }

    private static string? ResolveBodyStyleName(DocumentAutomationOptions? options)
        => !string.IsNullOrWhiteSpace(options?.StyleRoles?.Body)
            ? options.StyleRoles.Body.Trim()
            : string.IsNullOrWhiteSpace(options?.DefaultParagraphStyleName)
                ? null
                : options.DefaultParagraphStyleName.Trim();

    private static string? ResolveTitleStyleName(DocumentAutomationOptions? options)
        => !string.IsNullOrWhiteSpace(options?.StyleRoles?.Title)
            ? options.StyleRoles.Title.Trim()
            : null;

    private static string? ResolveDefaultTableStyleName(DocumentAutomationOptions? options)
        => options?.TableStylePresets
            .FirstOrDefault(preset => !string.IsNullOrWhiteSpace(preset.Name))
            ?.Name
            .Trim();

    private static Dictionary<string, DocumentModel.TextStyleDefinition> BuildStyleDictionary(
        DocumentModel.Document document,
        DocumentAutomationOptions? options)
    {
        var styles = new Dictionary<string, DocumentModel.TextStyleDefinition>(StringComparer.OrdinalIgnoreCase);
        if (options is not null)
        {
            foreach (var style in options.StylePresets)
            {
                if (!string.IsNullOrWhiteSpace(style.Name))
                {
                    styles[style.Name.Trim()] = style;
                }
            }
        }

        foreach (var style in document.Styles)
        {
            if (!string.IsNullOrWhiteSpace(style.Name) && style.Text is not null)
            {
                styles[style.Name.Trim()] = style.Text;
            }
        }

        return styles;
    }
}
