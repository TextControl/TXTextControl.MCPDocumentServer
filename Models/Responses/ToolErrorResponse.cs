namespace TxTextControl.McpServer.Models.Responses;

public sealed class ToolErrorResponse
{
    public bool IsError { get; init; } = true;
    public string Code { get; init; } = "internal_error";
    public string Message { get; init; } = "An unexpected error occurred.";
}
