namespace TxTextControl.McpServer.Options;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "admin";
}
