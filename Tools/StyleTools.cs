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

/// <summary>Focused model-friendly native paragraph style operations.</summary>
[McpServerToolType]
public sealed class StyleTools(DocumentWorkflowService workflow)
{
    [McpServerTool(
        Name = "list_document_styles",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DocumentStylesResponse)),
     Description("Lists authoritative native paragraph styles in the current TX document, including exact name, base style, following style, character formatting, paragraph formatting, built-in status, and usage count. Call this before changing, renaming, deleting, or applying a style when its exact name is uncertain.")]
    public object ListDocumentStyles(string sessionId)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            return workflow.GetDocumentStyles(sessionId);
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    [McpServerTool(
        Name = "set_document_style",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Creates or updates one native named paragraph style. Call this directly when the exact style name is supplied; no document inspection is required. Example: 'change the style Heading 1 to font color red' => { sessionId, styleName: 'Heading 1', text: { colorHex: '#FF0000' } }. Existing styles are mutated in place, so all linked paragraphs inherit the change. Required: sessionId, styleName, and at least one requested change. Put character changes in text (fontName, fontSize in pt/px, bold, italic, underline, strikeout, colorHex, backgroundColorHex, characterSpacing, characterScaling, baseline, capitals). Put paragraph changes in paragraph (alignment, spaceBefore, spaceAfter, lineSpacing, absoluteLineSpacing, leftIndent, rightIndent, hangingIndent, backgroundColorHex, keepLinesTogether, keepWithNext, pageBreakBefore, widowOrphanLines). Omit unchanged properties. basedOn applies when creating a style; followingStyle may be set for either a new or existing style. Never use format_text or format_paragraph to change a named style definition.")]
    public object SetDocumentStyle(SetDocumentStyleRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.StyleName);
            if (request.Text is null
                && request.Paragraph is null
                && string.IsNullOrWhiteSpace(request.BasedOn)
                && string.IsNullOrWhiteSpace(request.FollowingStyle))
            {
                throw new ArgumentException("Specify at least one text, paragraph, basedOn, or followingStyle change.");
            }
            TextStyleDefinition style = request.Text ?? new TextStyleDefinition();
            style.Name = request.StyleName.Trim();
            style.Paragraph = request.Paragraph ?? style.Paragraph;
            return Apply(request.SessionId, new DocumentOperation
            {
                Type = BasicTextCapabilityPack.DefineStyle,
                Style = style,
                BasedOn = request.BasedOn,
                FollowingStyle = request.FollowingStyle
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    [McpServerTool(
        Name = "apply_document_style",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Applies an existing named paragraph style. Specify exactly one target: zero-based paragraphIndex, startParagraphIndex/endParagraphIndex, matchText, or allParagraphs=true. For selected editor text use matchText plus nearTextPosition; do not treat a browser offset as a paragraph index. Use allMatches only with matchText.")]
    public object ApplyDocumentStyle(ApplyDocumentStyleRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.StyleName);
            ValidateTarget(request);
            return Apply(request.SessionId, new DocumentOperation
            {
                Type = BasicTextCapabilityPack.ApplyStyleToParagraph,
                StyleName = request.StyleName,
                ParagraphIndex = request.ParagraphIndex,
                StartParagraphIndex = request.StartParagraphIndex,
                EndParagraphIndex = request.EndParagraphIndex,
                MatchText = request.MatchText,
                OccurrenceIndex = request.OccurrenceIndex,
                NearTextPosition = request.NearTextPosition,
                ReplaceAll = request.AllMatches,
                AllParagraphs = request.AllParagraphs
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    [McpServerTool(Name = "rename_document_style", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Renames a native paragraph style and preserves all paragraph links. Required: sessionId, styleName, and newStyleName. Built-in [Normal] cannot be renamed.")]
    public object RenameDocumentStyle(RenameDocumentStyleRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.StyleName);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.NewStyleName);
            return Apply(request.SessionId, new DocumentOperation
            {
                Type = BasicTextCapabilityPack.RenameStyle,
                StyleName = request.StyleName,
                NewStyleName = request.NewStyleName
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    [McpServerTool(Name = "delete_document_style", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Deletes a native paragraph style. If it is in use, replacementStyleName is required and every linked paragraph is reassigned before deletion. Built-in [Normal] cannot be deleted.")]
    public object DeleteDocumentStyle(DeleteDocumentStyleRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.StyleName);
            return Apply(request.SessionId, new DocumentOperation
            {
                Type = BasicTextCapabilityPack.DeleteStyle,
                StyleName = request.StyleName,
                ReplacementStyleName = request.ReplacementStyleName
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    [McpServerTool(Name = "create_styles_from_paragraphs", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ApplyOperationsResponse)),
     Description("Mutates the current session document by converting direct paragraph formatting into reusable native styles, following Text Control's formatting-comparison approach. Call this tool for requests such as 'convert paragraphs to styles' or 'create styles from the existing paragraph formatting'. It compares common character and paragraph attributes, reuses an equivalent existing style, or creates a uniquely named style and links every qualifying paragraph. The generated styles and paragraph links are persisted in the session document returned by get_as_base64. Mixed-format paragraphs are skipped to avoid losing inline formatting. Omit styleNamePrefix to use 'Generated Style'. Set minimumOccurrences to 2 or more only when the user wants repeated formatting groups; the default 1 includes one-off groups. includeStyledParagraphs defaults to false so existing named styles are preserved.")]
    public object CreateStylesFromParagraphs(CreateStylesFromParagraphsRequest request)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.StyleNamePrefix);
            ArgumentOutOfRangeException.ThrowIfLessThan(request.MinimumOccurrences, 1);
            return Apply(request.SessionId, new DocumentOperation
            {
                Type = BasicTextCapabilityPack.CreateStylesFromParagraphs,
                StyleNamePrefix = request.StyleNamePrefix,
                MinimumOccurrences = request.MinimumOccurrences,
                IncludeStyledParagraphs = request.IncludeStyledParagraphs
            });
        }
        catch (Exception exception)
        {
            return ToolErrorMapper.Map(exception);
        }
    }

    private object Apply(string sessionId, DocumentOperation operation) => workflow.ApplyOperations(new ApplyOperationsRequest
    {
        SessionId = sessionId,
        CreateIfMissing = false,
        Operations = [operation]
    });

    private static void ValidateTarget(ApplyDocumentStyleRequest request)
    {
        bool hasRange = request.StartParagraphIndex.HasValue || request.EndParagraphIndex.HasValue;
        int selectors = (request.ParagraphIndex.HasValue ? 1 : 0)
                        + (hasRange ? 1 : 0)
                        + (!string.IsNullOrWhiteSpace(request.MatchText) ? 1 : 0)
                        + (request.AllParagraphs ? 1 : 0);
        if (selectors != 1 || (request.EndParagraphIndex.HasValue && !request.StartParagraphIndex.HasValue))
        {
            throw new ArgumentException("Specify exactly one valid paragraph target.");
        }
        if (request.AllMatches && string.IsNullOrWhiteSpace(request.MatchText))
        {
            throw new ArgumentException("allMatches can only be used with matchText.");
        }
    }
}
