using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class FormatTextOccurrencesOperationHandler : IDocumentOperationHandler
{
    public string Type => BasicTextCapabilityPack.FormatTextOccurrences;
    public string CapabilityPack => BasicTextCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = BasicTextCapabilityPack.FormatTextOccurrences,
        CapabilityPack = BasicTextCapabilityPack.PackName,
        Description = "Formats text occurrences by search text without requiring client-side character offsets.",
        Intent = "Use for requests such as 'make every instance of jon bold' or 'color all totals red'.",
        RequiredProperties = ["type", "matchText", "style"],
        OptionalProperties = ["matchCase", "wholeWord", "maxOccurrences"],
        Properties = new()
        {
            ["matchText"] = "Text to find and format.",
            ["style"] = "Inline TextStyleDefinition to apply to each occurrence.",
            ["matchCase"] = "Optional case-sensitive matching flag.",
            ["wholeWord"] = "Optional whole-word matching flag.",
            ["maxOccurrences"] = "Optional limit for the number of matches to format."
        },
        Example = new()
        {
            ["type"] = BasicTextCapabilityPack.FormatTextOccurrences,
            ["matchText"] = "jon",
            ["wholeWord"] = true,
            ["style"] = new Dictionary<string, object?> { ["bold"] = true }
        },
        ModelEffects = ["Splits paragraph runs where needed and applies inline run styles to matching text."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        if (string.IsNullOrEmpty(operation.MatchText))
        {
            throw new ArgumentException("matchText is required.");
        }

        if (operation.Style is null)
        {
            throw new ArgumentException("style is required.");
        }

        var tx = context.TextControl;
        var occurrences = TextOccurrenceUtilities.FindOccurrences(
            tx,
            operation.MatchText,
            operation.MatchCase,
            operation.WholeWord,
            operation.MaxOccurrences);

        if (occurrences.Count == 0)
        {
            throw new ArgumentException(
                $"No text matched '{operation.MatchText}'. Inspect the current document and retry with exact text.");
        }

        foreach (var occurrence in occurrences)
        {
            tx.Selection = new Selection(occurrence.Start, occurrence.Length);
            var selection = tx.Selection;
            DocumentOperationFormatter.ApplyStyle(selection, operation.Style);
            tx.Selection = selection;
        }

        TextOccurrenceUtilities.FormatModelOccurrences(
            context.Document,
            operation.MatchText,
            operation.MatchCase,
            operation.WholeWord,
            operation.MaxOccurrences,
            operation.Style);

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Formatted {occurrences.Count} occurrence(s) of '{operation.MatchText}'.",
            TargetType = "textOccurrences",
            Location = "document.text",
            Metadata = new Dictionary<string, object?>
            {
                ["matchText"] = operation.MatchText,
                ["matchCase"] = operation.MatchCase,
                ["wholeWord"] = operation.WholeWord,
                ["occurrenceCount"] = occurrences.Count,
                ["ranges"] = occurrences.Select(occurrence => new Dictionary<string, object?>
                {
                    ["start"] = occurrence.Start,
                    ["length"] = occurrence.Length
                }).ToList()
            }
        };
    }
}
