using System;
using System.Collections.Generic;
using System.Linq;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class AppendParagraphOperationHandler : IDocumentOperationHandler
{
    public string Type => "append_paragraph";
    public string CapabilityPack => BasicTextCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = BasicTextCapabilityPack.AppendParagraph,
        CapabilityPack = BasicTextCapabilityPack.PackName,
        Description = "Appends one paragraph to the end of the document, optionally with inline styled runs.",
        Intent = "Use for normal prose, headings, captions, and inline emphasis without text-offset formatting.",
        RequiredProperties = ["type", "text or runs"],
        OptionalProperties = ["styleName", "paragraph", "runs"],
        Properties = new()
        {
            ["text"] = "Paragraph text to append.",
            ["runs"] = "Optional ordered array of { text, styleName, style } inline runs. When provided, runs are rendered instead of text. Omit run style fields unless the user explicitly asks for inline styling.",
            ["runs[].style"] = "Optional inline TextStyleDefinition for a run, such as { bold: true }. Omit unless the user explicitly asks for inline styling.",
            ["runs[].styleName"] = "Optional named style applied directly to the run. Omit unless the user explicitly asks for this named style.",
            ["styleName"] = "Optional predefined style name to apply to the whole paragraph. Omit when the prompt contains no explicit style instruction; during document creation the first unstyled title-like body paragraph uses the configured title role and later unstyled paragraphs use the configured body default.",
            ["paragraph"] = "Optional paragraph formatting override: alignment, spacing, and line spacing. The override is layered over the selected named/default style."
        },
        Example = new()
        {
            ["type"] = BasicTextCapabilityPack.AppendParagraph,
            ["styleName"] = "Body",
            ["runs"] = new object[]
            {
                new Dictionary<string, object?> { ["text"] = "Hello " },
                new Dictionary<string, object?> { ["text"] = "jon", ["style"] = new Dictionary<string, object?> { ["bold"] = true } }
            }
        },
        ModelEffects = ["Adds a document.sections[].blocks[] paragraph block with one or more runs."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var runs = NormalizeRuns(operation);
        var text = string.Concat(runs.Select(run => run.Text));
        var effectiveStyleName = string.IsNullOrWhiteSpace(operation.StyleName)
            ? context.IsAtStartOfMainBody() && LooksLikeDocumentTitle(text)
                ? context.GetTitleStyleName() ?? context.GetDefaultParagraphStyleName()
                : context.GetDefaultParagraphStyleName()
            : operation.StyleName.Trim();

        if (context.TryGetTextControl(out var tx))
        {
            var start = (tx.Text ?? string.Empty).Length;
            var prefix = context.HasOpenParagraph ? "\r\n" : string.Empty;
            var insertion = prefix + text;
            var textStart = start + prefix.Length;

            tx.Selection = new Selection(start, 0) { Text = insertion };
            context.InlineDocumentEndInsertionIndex = textStart + text.Length;

            if (text.Length > 0)
            {
                tx.Selection = new Selection(textStart, text.Length);
                var defaultSelection = tx.Selection;
                var defaultStyle = context.GetDefaultTextStyle();
                DocumentOperationFormatter.ApplyStyle(defaultSelection, defaultStyle);
                tx.Selection = defaultSelection;
                if (defaultStyle.Paragraph is not null)
                {
                    DocumentOperationFormatter.ApplyParagraphStyle(tx.Paragraphs[tx.Paragraphs.Count], defaultStyle.Paragraph);
                }
            }

            if (!string.IsNullOrWhiteSpace(effectiveStyleName))
            {
                var style = context.GetStyle(effectiveStyleName);
                style.Name = effectiveStyleName;
                DocumentOperationFormatter.EnsureParagraphStyle(tx, style);
                tx.Paragraphs[tx.Paragraphs.Count].FormattingStyle = style.Name;
                tx.Selection = new Selection(textStart, text.Length);
                var selection = tx.Selection;
                selection.FormattingStyle = style.Name;
                tx.Selection = selection;
                if (style.Paragraph is not null)
                {
                    DocumentOperationFormatter.ApplyParagraphStyle(tx.Paragraphs[tx.Paragraphs.Count], style.Paragraph);
                }
            }

            if (operation.Paragraph is not null)
            {
                DocumentOperationFormatter.ApplyParagraphStyle(
                    tx.Paragraphs[tx.Paragraphs.Count],
                    operation.Paragraph);
            }

            var runOffset = textStart;
            foreach (var run in runs)
            {
                if (run.Text.Length == 0)
                {
                    continue;
                }

                var runStyle = ResolveRunStyle(context, run);
                if (runStyle is not null)
                {
                    tx.Selection = new Selection(runOffset, run.Text.Length);
                    var selection = tx.Selection;
                    DocumentOperationFormatter.ApplyStyle(selection, runStyle);
                    tx.Selection = selection;
                }

                runOffset += run.Text.Length;
            }
        }

        context.HasOpenParagraph = true;

        var paragraph = new DocumentModel.Paragraph
        {
            Id = Guid.NewGuid().ToString("N"),
            StyleName = effectiveStyleName,
            Alignment = operation.Paragraph?.Alignment,
            ParagraphStyle = MergeParagraphStyles(
                ResolveParagraphStyle(context, effectiveStyleName),
                operation.Paragraph),
            Runs = runs
        };

        var section = context.GetCurrentSection();
        var blockIndex = section.Blocks.Count;
        section.Blocks.Add(new DocumentModel.DocumentBlock
        {
            Type = "paragraph",
            Paragraph = paragraph
        });

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = string.IsNullOrWhiteSpace(effectiveStyleName)
                ? "Appended paragraph."
                : $"Appended paragraph with style '{effectiveStyleName}'.",
            TargetType = "paragraph",
            TargetId = paragraph.Id,
            Location = $"sections[{context.CurrentSectionIndex}].blocks[{blockIndex}].paragraph",
            Metadata = new Dictionary<string, object?>
            {
                ["sectionIndex"] = context.CurrentSectionIndex,
                ["blockIndex"] = blockIndex,
                ["paragraphId"] = paragraph.Id,
                ["styleName"] = paragraph.StyleName,
                ["textLength"] = text.Length
            }
        };
    }

    private static List<DocumentModel.Run> NormalizeRuns(DocumentOperation operation)
    {
        if (operation.Runs.Count > 0)
        {
            return operation.Runs
                .Select(run => new DocumentModel.Run
                {
                    Id = string.IsNullOrWhiteSpace(run.Id) ? Guid.NewGuid().ToString("N") : run.Id,
                    Text = run.Text ?? string.Empty,
                    StyleName = string.IsNullOrWhiteSpace(run.StyleName) ? null : run.StyleName.Trim(),
                    Style = run.Style
                })
                .ToList();
        }

        return
        [
            new DocumentModel.Run
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = operation.Text ?? string.Empty,
                StyleName = string.IsNullOrWhiteSpace(operation.StyleName) ? null : operation.StyleName.Trim()
            }
        ];
    }

    private static TextStyleDefinition? ResolveRunStyle(
        DocumentOperationContext context,
        DocumentModel.Run run)
    {
        if (run.Style is not null)
        {
            return run.Style;
        }

        return string.IsNullOrWhiteSpace(run.StyleName)
            ? null
            : context.GetStyle(run.StyleName);
    }

    private static ParagraphStyleDefinition? ResolveParagraphStyle(
        DocumentOperationContext context,
        string? styleName)
    {
        if (!string.IsNullOrWhiteSpace(styleName))
        {
            return context.GetStyle(styleName).Paragraph;
        }

        return context.GetDefaultTextStyle().Paragraph;
    }

    private static ParagraphStyleDefinition? MergeParagraphStyles(
        ParagraphStyleDefinition? baseStyle,
        ParagraphStyleDefinition? overrideStyle)
    {
        if (overrideStyle is null)
        {
            return baseStyle;
        }

        return new ParagraphStyleDefinition
        {
            Alignment = overrideStyle.Alignment ?? baseStyle?.Alignment,
            SpaceBefore = overrideStyle.SpaceBefore ?? baseStyle?.SpaceBefore,
            SpaceAfter = overrideStyle.SpaceAfter ?? baseStyle?.SpaceAfter,
            LineSpacing = overrideStyle.LineSpacing ?? baseStyle?.LineSpacing,
            Unit = overrideStyle.Unit
        };
    }

    private static bool LooksLikeDocumentTitle(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 80)
        {
            return false;
        }

        if (trimmed.EndsWith(".", StringComparison.Ordinal)
            || trimmed.EndsWith("!", StringComparison.Ordinal)
            || trimmed.EndsWith("?", StringComparison.Ordinal)
            || trimmed.EndsWith(":", StringComparison.Ordinal)
            || trimmed.EndsWith(";", StringComparison.Ordinal))
        {
            return false;
        }

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 8)
        {
            return false;
        }

        var letters = trimmed.Where(char.IsLetter).ToList();
        if (letters.Count == 0)
        {
            return false;
        }

        if (letters.All(letter => !char.IsLower(letter)))
        {
            return true;
        }

        return char.IsUpper(letters[0]);
    }
}
