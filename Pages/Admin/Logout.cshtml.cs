using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TxTextControl.McpServer.Pages.Admin;

public sealed class LogoutModel : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Admin/Login");
    }
}
