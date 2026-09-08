using System;
using System.Collections.Generic;
using System.ComponentModel;
using ModelContextProtocol.Server;
using TxTextControl.McpServer.Models.DocumentModel;
using TxTextControl.McpServer.Models.Requests;
using TxTextControl.McpServer.Models.Responses;
using TxTextControl.McpServer.Services;
using TxTextControl.McpServer.Services.Operations;

namespace TxTextControl.McpServer.Tools;

/// <summary>Focused model-friendly paragraph operations.</summary>
[McpServerToolType]
public sealed class ParagraphTools(DocumentWorkflowService workflow)
{
    [McpServerTool(
        Name = "format_paragraph",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Primary tool for paragraph-level formatting such as 'center the selected paragraph', 'justify paragraph 2', 'center paragraphs 2 through 4', 'double-space the paragraph containing Payment', or 'apply the Heading style'. Required: sessionId; exactly one target: paragraphIndex, startParagraphIndex plus optional endParagraphIndex (all zero-based MCP indexes), matchText, or allParagraphs=true; and at least one change: styleName, alignment, spaceBefore, spaceAfter, or lineSpacing. With an editor selection, pass selectedText as matchText and the browser start as nearTextPosition; the server finds the closest authoritative TX occurrence and formats its containing paragraph. Do not call search_text_ranges and do not pass browser offsets as paragraphIndex. If matchText occurs more than once, choose with nearTextPosition or occurrenceIndex, or set allMatches=true. alignment accepts left, right, center, or justify. unit defaults to pt. The tool preserves text and returns the exact paragraphIndexes changed.")]
    public object FormatParagraph(FormatParagraphRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
            ValidateTarget(request);

            bool hasParagraphFormat = !string.IsNullOrWhiteSpace(request.Alignment)
                                      || request.SpaceBefore.HasValue
                                      || request.SpaceAfter.HasValue
                                      || request.LineSpacing.HasValue;
            if (!hasParagraphFormat && string.IsNullOrWhiteSpace(request.StyleName))
            {
                throw new ArgumentException(
                    "Specify styleName or at least one paragraph formatting property.",
                    nameof(request));
            }

            var operations = new List<DocumentOperation>();
            if (!string.IsNullOrWhiteSpace(request.StyleName))
            {
                operations.Add(CreateTargetedOperation(
                    request,
                    BasicTextCapabilityPack.ApplyStyleToParagraph,
                    request.StyleName,
                    paragraph: null));
            }

            if (hasParagraphFormat)
            {
                operations.Add(CreateTargetedOperation(
                    request,
                    BasicTextCapabilityPack.FormatParagraphs,
                    styleName: null,
                    paragraph: new ParagraphStyleDefinition
                    {
                        Alignment = request.Alignment,
                        SpaceBefore = request.SpaceBefore,
                        SpaceAfter = request.SpaceAfter,
                        LineSpacing = request.LineSpacing,
                        Unit = request.Unit
                    }));
            }

            return workflow.ApplyOperations(new ApplyOperationsRequest
            {
                SessionId = request.SessionId,
                CreateIfMissing = false,
                Operations = operations
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    private static DocumentOperation CreateTargetedOperation(
        FormatParagraphRequest request,
        string type,
        string? styleName,
        ParagraphStyleDefinition? paragraph) => new()
    {
        Type = type,
        StyleName = styleName,
        Paragraph = paragraph,
        ParagraphIndex = request.ParagraphIndex,
        StartParagraphIndex = request.StartParagraphIndex,
        EndParagraphIndex = request.EndParagraphIndex,
        MatchText = request.MatchText,
        OccurrenceIndex = request.OccurrenceIndex,
        NearTextPosition = request.NearTextPosition,
        ReplaceAll = request.AllMatches,
        AllParagraphs = request.AllParagraphs,
        MatchCase = request.MatchCase,
        WholeWord = request.WholeWord
    };

    private static void ValidateTarget(FormatParagraphRequest request)
    {
        bool hasParagraphRange = request.StartParagraphIndex.HasValue || request.EndParagraphIndex.HasValue;
        int selectorCount = (request.ParagraphIndex.HasValue ? 1 : 0)
                            + (hasParagraphRange ? 1 : 0)
                            + (!string.IsNullOrEmpty(request.MatchText) ? 1 : 0)
                            + (request.AllParagraphs ? 1 : 0);
        if (selectorCount != 1)
        {
            throw new ArgumentException(
                "Specify exactly one target: paragraphIndex, startParagraphIndex/endParagraphIndex, matchText, or allParagraphs=true.",
                nameof(request));
        }

        if (request.EndParagraphIndex.HasValue && !request.StartParagraphIndex.HasValue)
        {
            throw new ArgumentException(
                "startParagraphIndex is required when endParagraphIndex is provided.",
                nameof(request));
        }

        bool hasMatchSelector = !string.IsNullOrEmpty(request.MatchText);
        if (!hasMatchSelector
            && (request.OccurrenceIndex.HasValue || request.NearTextPosition.HasValue || request.AllMatches))
        {
            throw new ArgumentException(
                "occurrenceIndex, nearTextPosition, and allMatches can only be used with matchText.",
                nameof(request));
        }

        int matchChoiceCount = (request.OccurrenceIndex.HasValue ? 1 : 0)
                               + (request.NearTextPosition.HasValue ? 1 : 0)
                               + (request.AllMatches ? 1 : 0);
        if (matchChoiceCount > 1)
        {
            throw new ArgumentException(
                "Use only one of occurrenceIndex, nearTextPosition, or allMatches.",
                nameof(request));
        }
    }
}
