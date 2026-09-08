using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using TxTextControl.McpServer.Models;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Services.Operations;
using TXTextControl;

namespace TxTextControl.McpServer.Services;

public sealed partial class ServerTextControlDocumentEngine
{
    public DocumentPresetStyleEngineResult LoadMarkdownWithPresetStyles(
        string markdown,
        string workingDocumentPath)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new ArgumentException("Markdown content is required.", nameof(markdown));
        }

        EnsureDirectory(workingDocumentPath);
        using var tx = CreateServerTextControl();
        ResetDocument(tx);
        ParsedMarkdownDocument parsed = ExtractMarkdownTables(markdown);
        string html = TXTextControl.Markdown.Markdown.ToHtml(parsed.Markdown);
        tx.Load(html, StringStreamType.HTMLFormat);
        MaterializeMarkdownTables(tx, parsed.Tables);
        return ApplyConfiguredPresets(tx, new DocumentState
        {
            WorkingDocumentPath = workingDocumentPath,
            Document = CreateNeutralDocument()
        }, workingDocumentPath);
    }

    public DocumentPresetStyleEngineResult ApplyPresetStyles(
        string workingDocumentPath,
        DocumentState state)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }

        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        ArgumentNullException.ThrowIfNull(state);
        using var tx = CreateServerTextControl();
        LoadWorkingDocument(tx, workingDocumentPath);
        return ApplyConfiguredPresets(tx, state, workingDocumentPath);
    }

    private DocumentPresetStyleEngineResult ApplyConfiguredPresets(
        ServerTextControl tx,
        DocumentState state,
        string workingDocumentPath)
    {
        Dictionary<string, TextStyleDefinition> styles = BuildStyleDictionary(state);
        Models.DocumentModel.Document document = EnsureDocument(state);
        EnsureDocumentStyles(document, styles);
        StyleRoleDefinition roles = _automationOptions.StyleRoles ?? new StyleRoleDefinition();
        List<string> warnings = [];
        bool pageLayoutApplied = ApplyConfiguredPageLayout(tx, document, styles);

        string[] importedStyleNames = tx.Paragraphs
            .Cast<TXTextControl.Paragraph>()
            .Select(paragraph => paragraph.FormattingStyle ?? string.Empty)
            .ToArray();
        int? firstLevelOneHeading = importedStyleNames
            .Select((styleName, index) => new { Index = index, Level = GetImportedHeadingLevel(styleName, roles) })
            .Where(candidate => candidate.Level == 1)
            .Select(candidate => (int?)candidate.Index)
            .FirstOrDefault();
        Dictionary<string, int> appliedStyles = new(StringComparer.OrdinalIgnoreCase);

        for (int paragraphIndex = 0; paragraphIndex < importedStyleNames.Length; paragraphIndex++)
        {
            string styleName = ResolveImportedPresetStyle(
                importedStyleNames[paragraphIndex],
                paragraphIndex,
                firstLevelOneHeading,
                roles);
            if (!styles.TryGetValue(styleName, out TextStyleDefinition? style))
            {
                warnings.Add($"Configured preset style '{styleName}' was not found; paragraph {paragraphIndex} was left unchanged.");
                continue;
            }

            style.Name = styleName;
            ParagraphStyle nativeStyle = DocumentOperationFormatter.EnsureParagraphStyle(tx, style);
            TXTextControl.Paragraph paragraph = tx.Paragraphs[paragraphIndex + 1];
            paragraph.FormattingStyle = nativeStyle.Name;
            if (style.Paragraph is not null)
            {
                DocumentOperationFormatter.ApplyParagraphStyle(paragraph, style.Paragraph);
            }

            appliedStyles[styleName] = appliedStyles.GetValueOrDefault(styleName) + 1;
        }

        int tablesStyled = ApplyConfiguredTablePreset(tx, warnings);
        SaveWorkingDocument(tx, workingDocumentPath);
        DocumentContentSnapshot snapshot = CreateContentSnapshot(tx);
        var updatedState = new DocumentState
        {
            WorkingDocumentPath = workingDocumentPath,
            Document = document,
            Styles = new Dictionary<string, TextStyleDefinition>(styles, StringComparer.OrdinalIgnoreCase),
            LastOperationResults = [],
            SourceContentHash = null,
            ContentSnapshot = snapshot
        };

        return new DocumentPresetStyleEngineResult(
            updatedState,
            appliedStyles,
            tablesStyled,
            pageLayoutApplied,
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private bool ApplyConfiguredPageLayout(
        ServerTextControl tx,
        Models.DocumentModel.Document document,
        IDictionary<string, TextStyleDefinition> styles)
    {
        if (_automationOptions.DefaultPageLayout is null)
        {
            return false;
        }

        var context = new DocumentOperationContext(
            tx,
            document,
            styles,
            ResolveDefaultBodyStyleName(),
            ResolveTitleStyleName());
        var handler = new SetSectionLayoutOperationHandler();
        for (int sectionIndex = 0; sectionIndex < tx.Sections.Count; sectionIndex++)
        {
            handler.Apply(context, new DocumentOperation
            {
                Type = SectionCapabilityPack.SetSectionLayout,
                SectionIndex = sectionIndex,
                PageLayout = _automationOptions.DefaultPageLayout
            }, sectionIndex);
        }

        return true;
    }

    private int ApplyConfiguredTablePreset(ServerTextControl tx, List<string> warnings)
    {
        TableStylePresetDefinition? preset = _automationOptions.TableStylePresets
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.Name));
        if (preset is null)
        {
            return 0;
        }

        int styled = 0;
        foreach (TXTextControl.Table table in tx.Tables.Cast<TXTextControl.Table>())
        {
            int rowCount = table.Rows.Count;
            int columnCount = table.Columns.Count;
            if (rowCount == 0 || columnCount == 0)
            {
                continue;
            }

            if (preset.HeaderRowIndex < 0 || preset.HeaderRowIndex >= rowCount)
            {
                warnings.Add($"Table '{table.ID}' was not styled because preset '{preset.Name}' has an invalid header row index.");
                continue;
            }

            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                var (textStyle, cellStyle) = ResolveTableCellStyles(preset, rowIndex);
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    TXTextControl.TableCell cell = table.Cells.GetItem(rowIndex + 1, columnIndex + 1);
                    if (textStyle is not null)
                    {
                        cell.Select();
                        Selection selection = tx.Selection;
                        DocumentOperationFormatter.ApplyStyle(selection, textStyle);
                        tx.Selection = selection;
                    }

                    if (cellStyle is not null)
                    {
                        DocumentOperationFormatter.ApplyCellStyle(tx, cell, cellStyle);
                    }
                }
            }

            styled++;
        }

        return styled;
    }

    private static (TextStyleDefinition? TextStyle, CellStyleDefinition? CellStyle) ResolveTableCellStyles(
        TableStylePresetDefinition preset,
        int rowIndex)
    {
        if (rowIndex == preset.HeaderRowIndex)
        {
            return (preset.HeaderStyle, preset.HeaderCellStyle);
        }

        int bodyOrdinal = rowIndex > preset.HeaderRowIndex
            ? rowIndex - preset.HeaderRowIndex - 1
            : rowIndex;
        return bodyOrdinal % 2 == 1
            ? (preset.AlternatingRowStyle ?? preset.BodyStyle, preset.AlternatingRowCellStyle ?? preset.BodyCellStyle)
            : (preset.BodyStyle, preset.BodyCellStyle);
    }

    private static string ResolveImportedPresetStyle(
        string? importedStyleName,
        int paragraphIndex,
        int? firstLevelOneHeading,
        StyleRoleDefinition roles)
    {
        string title = ResolveRoleName(roles.Title, "Title");
        string heading1 = ResolveRoleName(roles.Heading1, "Heading");
        string heading2 = ResolveRoleName(roles.Heading2, "Heading2");
        string body = ResolveRoleName(roles.Body, "Body");

        if (string.Equals(importedStyleName, title, StringComparison.OrdinalIgnoreCase)
            || string.Equals(importedStyleName, "Title", StringComparison.OrdinalIgnoreCase))
        {
            return title;
        }

        if (string.Equals(importedStyleName, heading1, StringComparison.OrdinalIgnoreCase))
        {
            return heading1;
        }

        if (string.Equals(importedStyleName, heading2, StringComparison.OrdinalIgnoreCase))
        {
            return heading2;
        }

        int? headingLevel = GetImportedHeadingLevel(importedStyleName, roles);
        if (headingLevel == 1)
        {
            return paragraphIndex == firstLevelOneHeading ? title : heading1;
        }

        return headingLevel == 2 ? heading1
            : headingLevel >= 3 ? heading2
            : body;
    }

    private static int? GetImportedHeadingLevel(string? styleName, StyleRoleDefinition roles)
    {
        if (string.IsNullOrWhiteSpace(styleName))
        {
            return null;
        }

        string normalized = styleName.Trim();
        if (string.Equals(normalized, ResolveRoleName(roles.Title, "Title"), StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Title", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(normalized, ResolveRoleName(roles.Heading1, "Heading"), StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(normalized, ResolveRoleName(roles.Heading2, "Heading2"), StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        Match match = Regex.Match(normalized, @"^(?:h|heading\s*)(?<level>\d+)$", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["level"].Value, out int level)
            ? level
            : null;
    }

    private static string ResolveRoleName(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static ParsedMarkdownDocument ExtractMarkdownTables(string markdown)
    {
        string normalized = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        var output = new List<string>(lines.Length);
        var tables = new List<MarkdownTableDefinition>();
        bool inFence = false;

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)
                || line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                output.Add(line);
                continue;
            }

            if (!inFence
                && lineIndex + 1 < lines.Length
                && TryParseMarkdownRow(line, out List<string>? header)
                && TryParseMarkdownSeparator(lines[lineIndex + 1], header.Count, out List<HorizontalAlignment>? alignments))
            {
                var rows = new List<IReadOnlyList<string>> { header };
                int candidateIndex = lineIndex + 2;
                while (candidateIndex < lines.Length
                       && TryParseMarkdownRow(lines[candidateIndex], out List<string>? row)
                       && row.Count == header.Count)
                {
                    rows.Add(row);
                    candidateIndex++;
                }

                string placeholder = $"TXMCPTABLE{tables.Count:D4}{Guid.NewGuid():N}";
                tables.Add(new MarkdownTableDefinition(placeholder, rows, alignments));
                output.Add(string.Empty);
                output.Add(placeholder);
                output.Add(string.Empty);
                lineIndex = candidateIndex - 1;
                continue;
            }

            output.Add(line);
        }

        return new ParsedMarkdownDocument(string.Join('\n', output), tables);
    }

    private static bool TryParseMarkdownSeparator(
        string line,
        int expectedColumns,
        out List<HorizontalAlignment> alignments)
    {
        alignments = [];
        if (!TryParseMarkdownRow(line, out List<string>? cells, normalizeCells: false) || cells.Count != expectedColumns)
        {
            return false;
        }

        foreach (string cell in cells)
        {
            string value = cell.Trim();
            if (!Regex.IsMatch(value, @"^:?-{3,}:?$", RegexOptions.CultureInvariant))
            {
                alignments.Clear();
                return false;
            }

            alignments.Add(value.StartsWith(':') && value.EndsWith(':')
                ? HorizontalAlignment.Center
                : value.EndsWith(':')
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left);
        }

        return true;
    }

    private static bool TryParseMarkdownRow(
        string line,
        out List<string> cells,
        bool normalizeCells = true)
    {
        cells = [];
        string value = line.Trim();
        if (!value.Contains('|', StringComparison.Ordinal))
        {
            return false;
        }

        if (value.StartsWith('|'))
        {
            value = value[1..];
        }

        if (value.EndsWith('|') && !value.EndsWith("\\|", StringComparison.Ordinal))
        {
            value = value[..^1];
        }

        var current = new StringBuilder();
        bool escaped = false;
        foreach (char character in value)
        {
            if (escaped)
            {
                current.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '|')
            {
                cells.Add(normalizeCells ? NormalizeMarkdownCell(current.ToString()) : current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (escaped)
        {
            current.Append('\\');
        }

        cells.Add(normalizeCells ? NormalizeMarkdownCell(current.ToString()) : current.ToString().Trim());
        return cells.Count > 1;
    }

    private static string NormalizeMarkdownCell(string value)
    {
        string html = TXTextControl.Markdown.Markdown.ToHtml(value.Trim());
        string text = Regex.Replace(html, "<[^>]+>", string.Empty, RegexOptions.CultureInvariant);
        return WebUtility.HtmlDecode(text).Trim();
    }

    private static void MaterializeMarkdownTables(
        ServerTextControl tx,
        IReadOnlyList<MarkdownTableDefinition> tables)
    {
        int nextTableId = 10;
        foreach (MarkdownTableDefinition definition in tables)
        {
            int placeholderStart = tx.Find(definition.Placeholder, 0, FindOptions.MatchCase);
            if (placeholderStart < 0)
            {
                throw new InvalidOperationException("A generated Markdown table placeholder could not be resolved.");
            }

            tx.Selection = new Selection(placeholderStart, definition.Placeholder.Length) { Text = string.Empty };
            tx.Selection = new Selection(placeholderStart, 0);
            while (tx.Tables.GetItem(nextTableId) is not null)
            {
                nextTableId++;
            }

            int rowCount = definition.Rows.Count;
            int columnCount = definition.Rows[0].Count;
            if (!tx.Tables.Add(rowCount, columnCount, nextTableId))
            {
                throw new InvalidOperationException("TX Text Control could not materialize a Markdown table.");
            }

            TXTextControl.Table table = tx.Tables.GetItem(nextTableId)
                ?? throw new InvalidOperationException("The materialized Markdown table could not be resolved.");
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    TXTextControl.TableCell cell = table.Cells.GetItem(rowIndex + 1, columnIndex + 1);
                    cell.Text = definition.Rows[rowIndex][columnIndex];
                    cell.Select();
                    Selection selection = tx.Selection;
                    ParagraphFormat paragraphFormat = selection.ParagraphFormat;
                    paragraphFormat.Alignment = definition.Alignments[columnIndex];
                    selection.ParagraphFormat = paragraphFormat;
                    tx.Selection = selection;
                }
            }

            nextTableId++;
        }
    }

    private sealed record ParsedMarkdownDocument(
        string Markdown,
        IReadOnlyList<MarkdownTableDefinition> Tables);

    private sealed record MarkdownTableDefinition(
        string Placeholder,
        IReadOnlyList<IReadOnlyList<string>> Rows,
        IReadOnlyList<HorizontalAlignment> Alignments);
}
