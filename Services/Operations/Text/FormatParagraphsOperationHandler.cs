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
        Description = "Applies paragraph formatting such as alignment and spaceAfter to one paragraph or all body paragraphs.",
        Intent = "Use for layout changes that affect paragraph spacing without changing text content.",
        RequiredProperties = ["type", "paragraph"],
        OptionalProperties = ["paragraphIndex"],
        Properties = new()
        {
            ["paragraph.spaceAfter"] = "Space after paragraph. Defaults to points when paragraph.unit is omitted.",
            ["paragraph.spaceBefore"] = "Space before paragraph. Defaults to points when paragraph.unit is omitted.",
            ["paragraph.alignment"] = "Optional paragraph alignment: left or right.",
            ["paragraphIndex"] = "Optional zero-based paragraph index. If omitted, all body paragraphs are formatted."
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

        var targetIndexes = ResolveTargetIndexes(context.TextControl, operation.ParagraphIndex);
        foreach (var paragraphIndex in targetIndexes)
        {
            var paragraph = context.TextControl.Paragraphs[paragraphIndex + 1];
            DocumentOperationFormatter.ApplyParagraphStyle(paragraph, operation.Paragraph);
        }

        ApplyModelParagraphFormat(context.Document, operation.Paragraph, operation.ParagraphIndex);

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = operation.ParagraphIndex.HasValue
                ? $"Formatted paragraph {operation.ParagraphIndex.Value}."
                : $"Formatted {targetIndexes.Count} paragraphs.",
            TargetType = operation.ParagraphIndex.HasValue ? "paragraph" : "paragraphs",
            Location = operation.ParagraphIndex.HasValue ? $"paragraphs[{operation.ParagraphIndex.Value}]" : "paragraphs[*]",
            Metadata = new Dictionary<string, object?>
            {
                ["paragraphIndex"] = operation.ParagraphIndex,
                ["paragraphCount"] = targetIndexes.Count,
                ["spaceBefore"] = operation.Paragraph.SpaceBefore,
                ["spaceAfter"] = operation.Paragraph.SpaceAfter,
                ["alignment"] = operation.Paragraph.Alignment,
                ["unit"] = operation.Paragraph.Unit
            }
        };
    }

    private static List<int> ResolveTargetIndexes(ServerTextControl tx, int? paragraphIndex)
    {
        if (paragraphIndex.HasValue)
        {
            if (paragraphIndex.Value < 0)
            {
                throw new ArgumentException("paragraphIndex must be >= 0.");
            }

            if (paragraphIndex.Value + 1 > tx.Paragraphs.Count)
            {
                throw new ArgumentException("paragraphIndex is out of range.");
            }

            return [paragraphIndex.Value];
        }

        return Enumerable.Range(0, tx.Paragraphs.Count).ToList();
    }

    private static void ApplyModelParagraphFormat(
        TxTextControl.McpServer.Models.DocumentModel.Document document,
        ParagraphStyleDefinition style,
        int? paragraphIndex)
    {
        var paragraphs = document.Sections
            .SelectMany(section => section.Blocks)
            .Where(block => string.Equals(block.Type, "paragraph", StringComparison.OrdinalIgnoreCase))
            .Select(block => block.Paragraph)
            .Where(paragraph => paragraph is not null)
            .Cast<TxTextControl.McpServer.Models.DocumentModel.Paragraph>()
            .ToList();

        var targetIndexes = paragraphIndex.HasValue
            ? [paragraphIndex.Value]
            : Enumerable.Range(0, paragraphs.Count).ToList();

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

            if (!string.IsNullOrWhiteSpace(style.Alignment))
            {
                paragraphs[index].ParagraphStyle.Alignment = style.Alignment.Trim();
                paragraphs[index].Alignment = style.Alignment.Trim();
            }

            paragraphs[index].ParagraphStyle.Unit = style.Unit;
        }
    }

}
