using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentModel = TxTextControl.McpServer.Models.DocumentModel;
using TXTextControl;

namespace TxTextControl.McpServer.Services.Operations;

public readonly record struct TextOccurrence(int Start, int Length);

public static class TextOccurrenceUtilities
{
    public static List<TextOccurrence> FindOccurrences(
        ServerTextControl tx,
        string matchText,
        bool matchCase,
        bool wholeWord,
        int? maxOccurrences)
    {
        if (string.IsNullOrEmpty(matchText))
        {
            throw new ArgumentException("matchText is required.");
        }

        if (maxOccurrences is <= 0)
        {
            throw new ArgumentException("maxOccurrences must be greater than 0 when provided.");
        }

        var options = (FindOptions)0;
        if (matchCase)
        {
            options |= FindOptions.MatchCase;
        }

        if (wholeWord)
        {
            options |= FindOptions.MatchWholeWord;
        }

        var occurrences = new List<TextOccurrence>();
        var start = 0;
        while (!maxOccurrences.HasValue || occurrences.Count < maxOccurrences.Value)
        {
            var found = tx.Find(matchText, start, options);
            if (found < 0)
            {
                break;
            }

            occurrences.Add(new TextOccurrence(found, matchText.Length));
            start = found + Math.Max(1, matchText.Length);
        }

        return occurrences;
    }

    public static int FormatModelOccurrences(
        DocumentModel.Document document,
        string matchText,
        bool matchCase,
        bool wholeWord,
        int? maxOccurrences,
        DocumentModel.TextStyleDefinition style)
    {
        var formatted = 0;
        foreach (var paragraph in EnumerateParagraphs(document))
        {
            if (maxOccurrences.HasValue && formatted >= maxOccurrences.Value)
            {
                break;
            }

            var newRuns = new List<DocumentModel.Run>();
            foreach (var run in paragraph.Runs)
            {
                if (maxOccurrences.HasValue && formatted >= maxOccurrences.Value)
                {
                    newRuns.Add(CloneRun(run));
                    continue;
                }

                var matches = FindStringOccurrences(
                    run.Text ?? string.Empty,
                    matchText,
                    matchCase,
                    wholeWord,
                    maxOccurrences.HasValue ? maxOccurrences.Value - formatted : null);

                if (matches.Count == 0)
                {
                    newRuns.Add(CloneRun(run));
                    continue;
                }

                var cursor = 0;
                foreach (var match in matches)
                {
                    if (match.Start > cursor)
                    {
                        newRuns.Add(CloneRun(run, run.Text.Substring(cursor, match.Start - cursor)));
                    }

                    newRuns.Add(CloneRun(
                        run,
                        run.Text.Substring(match.Start, match.Length),
                        MergeStyles(run.Style, style)));
                    cursor = match.Start + match.Length;
                    formatted++;
                }

                if (cursor < run.Text.Length)
                {
                    newRuns.Add(CloneRun(run, run.Text[cursor..]));
                }
            }

            paragraph.Runs = newRuns;
        }

        return formatted;
    }

    public static int ReplaceModelOccurrences(
        DocumentModel.Document document,
        string matchText,
        string replacementText,
        bool matchCase,
        bool wholeWord,
        int? maxOccurrences)
    {
        var replaced = 0;
        foreach (var paragraph in EnumerateParagraphs(document))
        {
            if (maxOccurrences.HasValue && replaced >= maxOccurrences.Value)
            {
                break;
            }

            foreach (var run in paragraph.Runs)
            {
                if (maxOccurrences.HasValue && replaced >= maxOccurrences.Value)
                {
                    break;
                }

                var count = maxOccurrences.HasValue ? maxOccurrences.Value - replaced : int.MaxValue;
                var (text, replacements) = ReplaceStringOccurrences(
                    run.Text ?? string.Empty,
                    matchText,
                    replacementText,
                    matchCase,
                    wholeWord,
                    count);
                run.Text = text;
                replaced += replacements;
            }
        }

        return replaced;
    }

    private static IEnumerable<DocumentModel.Paragraph> EnumerateParagraphs(DocumentModel.Document document)
        => document.Sections
            .SelectMany(section => section.Blocks)
            .Where(block => string.Equals(block.Type, "paragraph", StringComparison.OrdinalIgnoreCase))
            .Select(block => block.Paragraph)
            .Where(paragraph => paragraph is not null)
            .Select(paragraph => paragraph!);

    private static List<TextOccurrence> FindStringOccurrences(
        string text,
        string matchText,
        bool matchCase,
        bool wholeWord,
        int? maxOccurrences)
    {
        var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        var pattern = Regex.Escape(matchText);
        if (wholeWord)
        {
            pattern = $@"\b{pattern}\b";
        }

        var matches = Regex.Matches(text, pattern, options)
            .Cast<Match>()
            .Select(match => new TextOccurrence(match.Index, match.Length));

        return maxOccurrences.HasValue
            ? matches.Take(maxOccurrences.Value).ToList()
            : matches.ToList();
    }

    private static (string Text, int Count) ReplaceStringOccurrences(
        string text,
        string matchText,
        string replacementText,
        bool matchCase,
        bool wholeWord,
        int maxOccurrences)
    {
        var matches = FindStringOccurrences(text, matchText, matchCase, wholeWord, maxOccurrences);
        if (matches.Count == 0)
        {
            return (text, 0);
        }

        var result = text;
        foreach (var match in matches.OrderByDescending(match => match.Start))
        {
            result = result.Remove(match.Start, match.Length).Insert(match.Start, replacementText);
        }

        return (result, matches.Count);
    }

    private static DocumentModel.Run CloneRun(
        DocumentModel.Run run,
        string? text = null,
        DocumentModel.TextStyleDefinition? style = null)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Text = text ?? run.Text,
            StyleName = run.StyleName,
            Style = style ?? run.Style
        };

    private static DocumentModel.TextStyleDefinition MergeStyles(
        DocumentModel.TextStyleDefinition? baseStyle,
        DocumentModel.TextStyleDefinition overlay)
        => new()
        {
            Name = baseStyle?.Name ?? overlay.Name,
            FontName = overlay.FontName ?? baseStyle?.FontName,
            FontSize = overlay.FontSize ?? baseStyle?.FontSize,
            FontSizeUnit = overlay.FontSize.HasValue ? overlay.FontSizeUnit : baseStyle?.FontSizeUnit ?? overlay.FontSizeUnit,
            Bold = overlay.Bold ?? baseStyle?.Bold,
            Italic = overlay.Italic ?? baseStyle?.Italic,
            Underline = overlay.Underline ?? baseStyle?.Underline,
            ColorHex = overlay.ColorHex ?? baseStyle?.ColorHex
        };
}
