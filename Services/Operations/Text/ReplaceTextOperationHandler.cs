using System;
using System.Collections.Generic;
using System.Linq;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public sealed class ReplaceTextOperationHandler : IDocumentOperationHandler
{
    public string Type => BasicTextCapabilityPack.ReplaceText;
    public string CapabilityPack => BasicTextCapabilityPack.PackName;
    public DocumentOperationDescriptor Descriptor { get; } = new()
    {
        Type = BasicTextCapabilityPack.ReplaceText,
        CapabilityPack = BasicTextCapabilityPack.PackName,
        Description = "Replaces text occurrences by search text without requiring client-side character offsets.",
        Intent = "Use for semantic search/replace requests.",
        RequiredProperties = ["type", "matchText", "replacementText"],
        OptionalProperties = ["matchCase", "wholeWord", "maxOccurrences"],
        Properties = new()
        {
            ["matchText"] = "Text to find.",
            ["replacementText"] = "Replacement text.",
            ["matchCase"] = "Optional case-sensitive matching flag.",
            ["wholeWord"] = "Optional whole-word matching flag.",
            ["maxOccurrences"] = "Optional limit for the number of matches to replace."
        },
        Example = new()
        {
            ["type"] = BasicTextCapabilityPack.ReplaceText,
            ["matchText"] = "draft",
            ["replacementText"] = "final",
            ["wholeWord"] = true
        },
        ModelEffects = ["Updates matching text inside paragraph runs."],
        RequiresTxExecution = true
    };

    public OperationResult Apply(DocumentOperationContext context, DocumentOperation operation, int index)
    {
        if (string.IsNullOrEmpty(operation.MatchText))
        {
            throw new ArgumentException("matchText is required.");
        }

        if (operation.ReplacementText is null)
        {
            throw new ArgumentException("replacementText is required.");
        }

        var tx = context.TextControl;
        var occurrences = TextOccurrenceUtilities.FindOccurrences(
            tx,
            operation.MatchText,
            operation.MatchCase,
            operation.WholeWord,
            operation.MaxOccurrences);

        foreach (var occurrence in occurrences.OrderByDescending(occurrence => occurrence.Start))
        {
            tx.Selection = new Selection(occurrence.Start, occurrence.Length)
            {
                Text = operation.ReplacementText
            };
        }

        TextOccurrenceUtilities.ReplaceModelOccurrences(
            context.Document,
            operation.MatchText,
            operation.ReplacementText,
            operation.MatchCase,
            operation.WholeWord,
            operation.MaxOccurrences);

        return new OperationResult
        {
            Index = index,
            Type = Type,
            Detail = $"Replaced {occurrences.Count} occurrence(s) of '{operation.MatchText}'.",
            TargetType = "textOccurrences",
            Location = "document.text",
            Metadata = new Dictionary<string, object?>
            {
                ["matchText"] = operation.MatchText,
                ["replacementText"] = operation.ReplacementText,
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
