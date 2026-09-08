using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Services.Operations;
using TXTextControl;

namespace TxTextControl.McpServer.Services;

public sealed partial class ServerTextControlDocumentEngine
{
    public IReadOnlyList<StyleInspection> GetDocumentStyleSnapshots(string workingDocumentPath)
    {
        if (string.IsNullOrWhiteSpace(workingDocumentPath))
        {
            throw new ArgumentException("A working document path is required.", nameof(workingDocumentPath));
        }
        if (!File.Exists(workingDocumentPath))
        {
            throw new FileNotFoundException("The working document was not found.", workingDocumentPath);
        }

        using var tx = CreateServerTextControl();
        LoadWorkingDocument(tx, workingDocumentPath);
        Dictionary<string, int> usageCounts = tx.Paragraphs
            .Cast<TXTextControl.Paragraph>()
            .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph.FormattingStyle))
            .GroupBy(paragraph => paragraph.FormattingStyle, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        return tx.ParagraphStyles
            .Cast<ParagraphStyle>()
            .Select(style => DocumentStyleUtilities.Inspect(style, usageCounts.GetValueOrDefault(style.Name)))
            .OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
