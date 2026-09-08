using System.Text.Json.Serialization;
using TxTextControl.McpServer.Models.DocumentModel;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class SetDocumentStyleRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("styleName"), JsonRequired]
    public string StyleName { get; set; } = string.Empty;

    [JsonPropertyName("basedOn")]
    public string? BasedOn { get; set; }

    [JsonPropertyName("followingStyle")]
    public string? FollowingStyle { get; set; }

    [JsonPropertyName("text")]
    public TextStyleDefinition? Text { get; set; }

    [JsonPropertyName("paragraph")]
    public ParagraphStyleDefinition? Paragraph { get; set; }
}

public sealed class RenameDocumentStyleRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("styleName"), JsonRequired]
    public string StyleName { get; set; } = string.Empty;

    [JsonPropertyName("newStyleName"), JsonRequired]
    public string NewStyleName { get; set; } = string.Empty;
}

public sealed class DeleteDocumentStyleRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("styleName"), JsonRequired]
    public string StyleName { get; set; } = string.Empty;

    [JsonPropertyName("replacementStyleName")]
    public string? ReplacementStyleName { get; set; }
}

public sealed class CreateStylesFromParagraphsRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("styleNamePrefix")]
    public string StyleNamePrefix { get; set; } = "Generated Style";

    [JsonPropertyName("minimumOccurrences")]
    public int MinimumOccurrences { get; set; } = 1;

    [JsonPropertyName("includeStyledParagraphs")]
    public bool IncludeStyledParagraphs { get; set; }
}

public sealed class ApplyDocumentStyleRequest
{
    [JsonPropertyName("sessionId"), JsonRequired]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("styleName"), JsonRequired]
    public string StyleName { get; set; } = string.Empty;

    [JsonPropertyName("paragraphIndex")]
    public int? ParagraphIndex { get; set; }

    [JsonPropertyName("startParagraphIndex")]
    public int? StartParagraphIndex { get; set; }

    [JsonPropertyName("endParagraphIndex")]
    public int? EndParagraphIndex { get; set; }

    [JsonPropertyName("matchText")]
    public string? MatchText { get; set; }

    [JsonPropertyName("occurrenceIndex")]
    public int? OccurrenceIndex { get; set; }

    [JsonPropertyName("nearTextPosition")]
    public int? NearTextPosition { get; set; }

    [JsonPropertyName("allMatches")]
    public bool AllMatches { get; set; }

    [JsonPropertyName("allParagraphs")]
    public bool AllParagraphs { get; set; }
}
