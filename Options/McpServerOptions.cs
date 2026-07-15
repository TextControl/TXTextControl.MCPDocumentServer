namespace TxTextControl.McpServer.Options;

public sealed class McpServerOptions
{
    public const string SectionName = "McpServer";

    public string Name { get; set; } = "TX Text Control Document MCP Server";
    public string BasePath { get; set; } = "samples";
    public int SessionMaxAgeHours { get; set; } = 12;
}
