using System;
using System.Collections.Generic;
using System.Globalization;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class InsertSectionBreakOperationHandler : IDocumentOperationHandler
{
    public string Type => SectionCapabilityPack.InsertSectionBreak;
    public string CapabilityPack => SectionCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = SectionCapabilityPack.InsertSectionBreak,
        CapabilityPack = SectionCapabilityPack.PackName,
        Description = "Inserts a TX Text Control section break and makes the new section current for following operations.",
        Intent = "Use between content for pages or regions that need different page sizes, orientation, margins, or headers/footers.",
        RequiredProperties = ["type"],
        OptionalProperties = ["breakKind"],
        Properties = new()
        {
            ["breakKind"] = "Optional section break kind. Supported values: beginAtNewPage/newPage, beginAtNewLine/continuous. Defaults to beginAtNewPage."
        },
        Example = new()
        {
            ["type"] = SectionCapabilityPack.InsertSectionBreak,
            ["breakKind"] = "beginAtNewPage"
        },
        ModelEffects = ["Adds a new document.sections[] entry and makes it current for following operations."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        var breakKind = ResolveBreakKind(operation.BreakKind);
        if (context.TryGetTextControl(out var tx))
        {
            tx.Sections.Add(breakKind);
        }

        context.CurrentSectionIndex += 1;
        var section = context.GetSection(context.CurrentSectionIndex);
        context.HasOpenParagraph = false;
        context.InlineDocumentEndInsertionIndex = null;
        context.DocumentEndInsertionIndex = null;

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Inserted section break and moved to section {context.CurrentSectionIndex}.",
            TargetType = "section",
            TargetId = context.CurrentSectionIndex.ToString(CultureInfo.InvariantCulture),
            Location = $"sections[{context.CurrentSectionIndex}]",
            Metadata = new Dictionary<string, object?>
            {
                ["sectionIndex"] = context.CurrentSectionIndex,
                ["sectionId"] = section.Id,
                ["breakKind"] = breakKind.ToString()
            }
        };
    }

    private static SectionBreakKind ResolveBreakKind(string? breakKind)
    {
        if (string.IsNullOrWhiteSpace(breakKind))
        {
            return SectionBreakKind.BeginAtNewPage;
        }

        return breakKind.Trim().ToLowerInvariant() switch
        {
            "beginatnewpage" or "newpage" or "page" => SectionBreakKind.BeginAtNewPage,
            "beginatnewline" or "continuous" or "newline" => SectionBreakKind.BeginAtNewLine,
            _ => throw new ArgumentException("breakKind must be beginAtNewPage or beginAtNewLine.")
        };
    }
}
