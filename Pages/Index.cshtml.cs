using HB_Claude.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HB_Claude.Pages;

public class IndexModel : PageModel
{
    private readonly ApiService _api;

    [BindProperty] public string Epost { get; set; } = "";
    [BindProperty] public string Lösenord { get; set; } = "";
    public string Felmeddelande { get; set; } = "";

    public IndexModel(ApiService api) => _api = api;

    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetString("user") != null)
            return RedirectToPage("/Events");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string action)
    {
        if (Epost == "demo" && Lösenord == "mm")
        {
            HttpContext.Session.SetString("user", "2");
            HttpContext.Session.SetString("typ", "1");
            HttpContext.Session.SetString("epost", "demo");
            return Redirect(action switch { "lagbygge" => "/Lagbygge", "stats" => "/Stats", _ => "/Events" });
        }

        var testUser = new Models.User { UserName = Epost };
        if (!await _api.FinnsUser(testUser))
        {
            Felmeddelande = "Användarnamnet existerar inte!";
            return Page();
        }

        var us = await _api.AuthenticateUser(Epost, Lösenord);

        if (us == null)
        {
            Felmeddelande = "Felaktigt användarnamn eller lösenord!";
            return Page();
        }

        HttpContext.Session.SetString("user", us.UserID);
        HttpContext.Session.SetString("typ", us.typ);
        HttpContext.Session.SetString("epost", us.UserName);
        return Redirect(action switch { "lagbygge" => "/Lagbygge", "stats" => "/Stats", _ => "/Events" });
    }
}
