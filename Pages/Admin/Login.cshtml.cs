using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using TxTextControl.McpServer.Options;

namespace TxTextControl.McpServer.Pages.Admin;

[AllowAnonymous]
public sealed class LoginModel : PageModel
{
    private readonly AdminOptions _options;

    public LoginModel(IOptions<AdminOptions> options)
    {
        _options = options.Value;
    }

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!IsValidCredential())
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, Username)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToPage("/Admin/Index");
    }

    private bool IsValidCredential()
        => string.Equals(Username, _options.Username, System.StringComparison.Ordinal)
           && string.Equals(Password, _options.Password, System.StringComparison.Ordinal);
}
