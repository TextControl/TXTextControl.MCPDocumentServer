using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class FormatParagraphsOperationHandler : IDocumentOperationHandler
{
    public string Type => BasicTextCapabilityPack.FormatParagraphs;
    public string CapabilityPack => BasicTextCapabilityPack.PackName;

    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = BasicTextCapabilityPack.FormatParagraphs,
        CapabilityPack = BasicTextCapabilityPack.PackName,
        Description = "Applies paragraph formatting such as alignment, spacing, and line spacing to targeted paragraphs.",
        Intent = "Use for paragraph-level layout changes without changing text content.",
        RequiredProperties = ["type", "paragraph"],
        OptionalProperties = ["paragraphIndex", "startParagraphIndex", "endParagraphIndex", "matchText", "occurrenceIndex", "nearTextPosition", "replaceAll", "allParagraphs"],
        Properties = new()
        {
            ["paragraph.spaceAfter"] = "Space after paragraph. Use only when the user explicitly asks for paragraph spacing.",
            ["paragraph.spaceBefore"] = "Space before paragraph. Use only when the user explicitly asks for paragraph spacing.",
            ["paragraph.lineSpacing"] = "Optional line spacing multiplier or percentage.",
            ["paragraph.alignment"] = "Optional alignment: left, right, center, or justify.",
            ["paragraphIndex"] = "Zero-based paragraph index from MCP inspection.",
            ["startParagraphIndex"] = "First zero-based paragraph in an inclusive paragraph range.",
            ["endParagraphIndex"] = "Last zero-based paragraph in an inclusive paragraph range.",
            ["matchText"] = "Text inside the target paragraph. Resolve it with TX Find rather than client offsets.",
            ["occurrenceIndex"] = "Zero-based occurrence when matchText appears more than once.",
            ["nearTextPosition"] = "Non-authoritative browser-position hint used to choose the closest match.",
            ["replaceAll"] = "With matchText, format every distinct paragraph containing a match.",
            ["allParagraphs"] = "Format every paragraph explicitly. For backward compatibility, omitting every target also means all paragraphs."
        },
        Example = new()
        {
            ["type"] = BasicTextCapabilityPack.FormatParagraphs,
            ["paragraph"] = new Dictionary<string, object?>
            {
                ["alignment"] = "right",
                ["spaceAfter"] = 20,
                ["unit"] = "pt"
            }
        },
        ModelEffects = ["Updates paragraph style metadata for model paragraphs when available."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        if (operation.Paragraph is null)
        {
            throw new ArgumentException("paragraph is required for format_paragraphs.");
        }

        if (string.IsNullOrWhiteSpace(operation.Paragraph.Alignment)
            && !operation.Paragraph.SpaceBefore.HasValue
            && !operation.Paragraph.SpaceAfter.HasValue
            && !operation.Paragraph.LineSpacing.HasValue)
        {
            throw new ArgumentException(
                "At least one paragraph formatting property is required: alignment, spaceBefore, spaceAfter, or lineSpacing.");
        }

        var targetIndexes = ParagraphTargetUtilities.ResolveTargetIndexes(
            context.TextControl,
            operation,
            allowImplicitAll: true);
        foreach (var paragraphIndex in targetIndexes)
        {
            var paragraph = context.TextControl.Paragraphs[paragraphIndex + 1];
            DocumentOperationFormatter.ApplyParagraphStyle(paragraph, operation.Paragraph);
        }

        ApplyModelParagraphFormat(context.Document, operation.Paragraph, targetIndexes);

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = targetIndexes.Count == 1
                ? $"Formatted paragraph {targetIndexes[0]}."
                : $"Formatted {targetIndexes.Count} paragraphs.",
            TargetType = targetIndexes.Count == 1 ? "paragraph" : "paragraphs",
            Location = targetIndexes.Count == 1 ? $"paragraphs[{targetIndexes[0]}]" : "paragraphs[*]",
            Metadata = new Dictionary<string, object?>
            {
                ["paragraphIndex"] = operation.ParagraphIndex,
                ["paragraphIndexes"] = targetIndexes,
                ["matchText"] = operation.MatchText,
                ["paragraphCount"] = targetIndexes.Count,
                ["spaceBefore"] = operation.Paragraph.SpaceBefore,
                ["spaceAfter"] = operation.Paragraph.SpaceAfter,
                ["lineSpacing"] = operation.Paragraph.LineSpacing,
                ["alignment"] = operation.Paragraph.Alignment,
                ["unit"] = operation.Paragraph.Unit
            }
        };
    }

    private static void ApplyModelParagraphFormat(
        TxTextControl.McpServer.Models.DocumentModel.Document document,
        ParagraphStyleDefinition style,
        IReadOnlyCollection<int> targetIndexes)
    {
        var paragraphs = ParagraphTargetUtilities.EnumerateModelParagraphs(document).ToList();

        foreach (var index in targetIndexes)
        {
            if (index < 0 || index >= paragraphs.Count)
            {
                continue;
            }

            paragraphs[index].ParagraphStyle ??= new ParagraphStyleDefinition();
            if (style.SpaceBefore.HasValue)
            {
                paragraphs[index].ParagraphStyle.SpaceBefore = style.SpaceBefore;
            }

            if (style.SpaceAfter.HasValue)
            {
                paragraphs[index].ParagraphStyle.SpaceAfter = style.SpaceAfter;
            }

            if (style.LineSpacing.HasValue)
            {
                paragraphs[index].ParagraphStyle.LineSpacing = style.LineSpacing;
            }

            if (!string.IsNullOrWhiteSpace(style.Alignment))
            {
                paragraphs[index].ParagraphStyle.Alignment = style.Alignment.Trim();
                paragraphs[index].Alignment = style.Alignment.Trim();
            }

            paragraphs[index].ParagraphStyle.Unit = style.Unit;
        }
    }

}
