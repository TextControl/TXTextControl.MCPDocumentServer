using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TxTextControl.McpServer.Pages;

public class IndexModel : PageModel
{
    public string ServerUrl { get; private set; } = string.Empty;

    public void OnGet()
    {
        ServerUrl = $"{Request.Scheme}://{Request.Host}/mcp";
    }
}
