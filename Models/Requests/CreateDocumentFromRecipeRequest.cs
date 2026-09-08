using System.Text.Json.Serialization;

namespace TxTextControl.McpServer.Models.Requests;

public sealed class CreateDocumentFromRecipeRequest
{
    [JsonPropertyName("recipeName"), JsonRequired]
    public string RecipeName { get; set; } = string.Empty;
}
