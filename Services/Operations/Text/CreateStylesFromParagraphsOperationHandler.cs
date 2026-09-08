using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class CreateStylesFromParagraphsOperationHandler : IDocumentOperationHandler
{
    public string Type => BasicTextCapabilityPack.CreateStylesFromParagraphs;
    public string CapabilityPack => BasicTextCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = BasicTextCapabilityPack.CreateStylesFromParagraphs,
        CapabilityPack = BasicTextCapabilityPack.PackName,
        Description = "Finds paragraphs with common character formatting, groups equal character and paragraph attributes, reuses equivalent styles, and creates styles for new formatting groups.",
        Intent = "Use when the user asks to convert direct paragraph formatting into reusable styles or create styles from the document's existing paragraphs.",
        RequiredProperties = ["type"],
        OptionalProperties = ["styleNamePrefix", "minimumOccurrences", "includeStyledParagraphs"],
        ModelEffects = ["Creates native paragraph styles and links qualifying paragraphs to equivalent styles without changing their appearance."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var tx = context.TextControl;
        int minimumOccurrences = operation.MinimumOccurrences ?? 1;
        if (minimumOccurrences < 1)
        {
            throw new ArgumentException("minimumOccurrences must be at least 1.");
        }
        string prefix = string.IsNullOrWhiteSpace(operation.StyleNamePrefix)
            ? "Generated Style"
            : operation.StyleNamePrefix.Trim();

        var candidates = new List<ParagraphCandidate>();
        for (int paragraphIndex = 0; paragraphIndex < tx.Paragraphs.Count; paragraphIndex++)
        {
            TXTextControl.Paragraph paragraph = tx.Paragraphs[paragraphIndex + 1];
            if (!operation.IncludeStyledParagraphs && IsExplicitlyStyled(paragraph.FormattingStyle))
            {
                continue;
            }
            paragraph.Select();
            Selection selection = tx.Selection;
            if (!selection.IsCommonValueSelected(Selection.Attribute.All))
            {
                continue;
            }
            candidates.Add(new ParagraphCandidate(paragraphIndex, CreateFingerprint(selection, paragraph.Format)));
        }

        ParagraphStyle[] existingStyles = tx.ParagraphStyles.Cast<ParagraphStyle>().ToArray();
        var createdNames = new List<string>();
        var reusedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appliedIndexes = new List<int>();
        int nextName = 1;

        foreach (var group in candidates.GroupBy(candidate => candidate.Fingerprint))
        {
            ParagraphCandidate[] members = group.ToArray();
            if (members.Length < minimumOccurrences)
            {
                continue;
            }

            ParagraphStyle? style = existingStyles.FirstOrDefault(candidate => CreateFingerprint(candidate) == group.Key);
            if (style is null)
            {
                string name;
                do
                {
                    name = $"{prefix} {nextName++}";
                }
                while (DocumentOperationFormatter.FindParagraphStyle(tx, name) is not null);

                style = CreateStyle(name, group.Key, tx);
                tx.ParagraphStyles.Add(style);
                createdNames.Add(style.Name);
                context.Styles[style.Name] = DocumentStyleUtilities.ToDefinition(style);
                context.Document.Styles.Add(new Style
                {
                    Name = style.Name,
                    Type = "paragraph",
                    BasedOn = style.BaseStyle?.Name,
                    Text = DocumentStyleUtilities.ToDefinition(style),
                    Paragraph = DocumentStyleUtilities.ToParagraphDefinition(style.ParagraphFormat)
                });
                existingStyles = [.. existingStyles, style];
            }
            else
            {
                reusedNames.Add(style.Name);
            }

            foreach (ParagraphCandidate member in members)
            {
                tx.Paragraphs[member.Index + 1].FormattingStyle = style.Name;
                appliedIndexes.Add(member.Index);
            }
        }

        var modelParagraphs = ParagraphTargetUtilities.EnumerateModelParagraphs(context.Document).ToList();
        foreach (int paragraphIndex in appliedIndexes)
        {
            if (paragraphIndex < modelParagraphs.Count)
            {
                modelParagraphs[paragraphIndex].StyleName = tx.Paragraphs[paragraphIndex + 1].FormattingStyle;
            }
        }

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Applied reusable styles to {appliedIndexes.Count} paragraphs; created {createdNames.Count} styles and reused {reusedNames.Count} existing styles.",
            TargetType = "styles",
            Location = "paragraphs[*].formattingStyle",
            Metadata = new Dictionary<string, object?>
            {
                ["createdStyleNames"] = createdNames,
                ["reusedStyleNames"] = reusedNames.OrderBy(name => name).ToArray(),
                ["paragraphIndexes"] = appliedIndexes,
                ["skippedMixedFormattingParagraphs"] = tx.Paragraphs.Count - candidates.Count,
                ["minimumOccurrences"] = minimumOccurrences
            }
        };
    }

    private static bool IsExplicitlyStyled(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && !name.Equals("Normal", StringComparison.OrdinalIgnoreCase)
           && !name.Equals("[Normal]", StringComparison.OrdinalIgnoreCase);

    private static ParagraphStyle CreateStyle(string name, StyleFingerprint fingerprint, ServerTextControl tx)
    {
        ParagraphStyle? normal = DocumentOperationFormatter.FindParagraphStyle(tx, "[Normal]");
        var style = normal is null ? new ParagraphStyle(name) : new ParagraphStyle(name, normal.Name);
        style.Bold = fingerprint.Bold;
        style.Italic = fingerprint.Italic;
        style.FontName = fingerprint.FontName;
        style.FontSize = fingerprint.FontSize;
        style.ForeColor = Color.FromArgb(fingerprint.ForeColorArgb);
        style.TextBackColor = Color.FromArgb(fingerprint.TextBackColorArgb);
        style.Strikeout = fingerprint.Strikeout;
        style.Underline = fingerprint.Underline;
        style.Capitals = fingerprint.Capitals;
        style.CharacterSpacing = fingerprint.CharacterSpacing;
        style.CharacterScaling = fingerprint.CharacterScaling;
        style.AutoBaseline = fingerprint.AutoBaseline;
        style.Baseline = fingerprint.Baseline;
        ApplyFingerprint(style.ParagraphFormat, fingerprint);
        return style;
    }

    private static void ApplyFingerprint(ParagraphFormat format, StyleFingerprint value)
    {
        format.Alignment = value.Alignment;
        format.BackColor = Color.FromArgb(value.ParagraphBackColorArgb);
        format.BottomDistance = value.BottomDistance;
        format.TopDistance = value.TopDistance;
        format.LeftIndent = value.LeftIndent;
        format.RightIndent = value.RightIndent;
        format.HangingIndent = value.HangingIndent;
        format.LineSpacing = value.LineSpacing;
        format.AbsoluteLineSpacing = value.AbsoluteLineSpacing;
        format.KeepLinesTogether = value.KeepLinesTogether;
        format.KeepWithNext = value.KeepWithNext;
        format.PageBreakBefore = value.PageBreakBefore;
        format.WidowOrphanLines = value.WidowOrphanLines;
    }

    private static StyleFingerprint CreateFingerprint(Selection selection, ParagraphFormat format) => new(
        selection.Bold, selection.Italic, selection.FontName, selection.FontSize, selection.ForeColor.ToArgb(),
        selection.TextBackColor.ToArgb(), selection.Strikeout, selection.Underline, selection.Capitals,
        selection.CharacterSpacing, selection.CharacterScaling, selection.AutoBaseline, selection.Baseline,
        format.Alignment, format.BackColor.ToArgb(), format.BottomDistance, format.TopDistance,
        format.LeftIndent, format.RightIndent, format.HangingIndent, format.LineSpacing,
        format.AbsoluteLineSpacing, format.KeepLinesTogether, format.KeepWithNext,
        format.PageBreakBefore, format.WidowOrphanLines);

    private static StyleFingerprint CreateFingerprint(ParagraphStyle style)
        => new(style.Bold, style.Italic, style.FontName, style.FontSize, style.ForeColor.ToArgb(),
            style.TextBackColor.ToArgb(), style.Strikeout, style.Underline, style.Capitals,
            style.CharacterSpacing, style.CharacterScaling, style.AutoBaseline, style.Baseline,
            style.ParagraphFormat.Alignment, style.ParagraphFormat.BackColor.ToArgb(),
            style.ParagraphFormat.BottomDistance, style.ParagraphFormat.TopDistance,
            style.ParagraphFormat.LeftIndent, style.ParagraphFormat.RightIndent,
            style.ParagraphFormat.HangingIndent, style.ParagraphFormat.LineSpacing,
            style.ParagraphFormat.AbsoluteLineSpacing, style.ParagraphFormat.KeepLinesTogether,
            style.ParagraphFormat.KeepWithNext, style.ParagraphFormat.PageBreakBefore,
            style.ParagraphFormat.WidowOrphanLines);

    private sealed record ParagraphCandidate(int Index, StyleFingerprint Fingerprint);

    private sealed record StyleFingerprint(
        bool Bold, bool Italic, string FontName, int FontSize, int ForeColorArgb, int TextBackColorArgb,
        bool Strikeout, FontUnderlineStyle Underline, Capitals Capitals, int CharacterSpacing,
        int CharacterScaling, AutoBaseline AutoBaseline, int Baseline, HorizontalAlignment Alignment,
        int ParagraphBackColorArgb, int BottomDistance, int TopDistance, int LeftIndent, int RightIndent,
        int HangingIndent, int LineSpacing, int AbsoluteLineSpacing, bool KeepLinesTogether,
        bool KeepWithNext, bool PageBreakBefore, int WidowOrphanLines);
}
