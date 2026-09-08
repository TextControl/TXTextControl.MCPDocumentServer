using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TxTextControl.McpServer.Services.Admin;

namespace TxTextControl.McpServer.Pages.Admin;

public sealed class SettingsModel(ServerSettingsService settings) : PageModel
{
    [BindProperty]
    public ServerSettingsSnapshot Settings { get; set; } = new();

    [BindProperty]
    public string? NewAdminPassword { get; set; }

    public string? StatusMessage { get; private set; }
    public string? ErrorMessage { get; private set; }

    public void OnGet() => Settings = settings.Get();

    public IActionResult OnPost()
    {
        try
        {
            settings.Save(Settings, NewAdminPassword);
            StatusMessage = "Settings saved. Restart the server to apply host, worker-pool, logging, or storage changes.";
            Settings = settings.Get();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }

        NewAdminPassword = null;
        return Page();
    }
}
