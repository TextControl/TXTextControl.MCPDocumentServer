using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TxTextControl.McpServer.Pages.Admin;

public sealed class McpModel : PageModel
{
    public string EndpointUrl { get; private set; } = string.Empty;

    public void OnGet()
    {
        EndpointUrl = $"{Request.Scheme}://{Request.Host}/mcp";
    }
}
