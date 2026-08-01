using HB_Claude.Models;
using HB_Claude.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HB_Claude.Pages;

public class BytLosenordModel : PageModel
{
    private readonly ApiService _api;
    public BytLosenordModel(ApiService api) => _api = api;

    [BindProperty] public string Nuvarande { get; set; } = "";
    [BindProperty] public string Nytt { get; set; } = "";
    [BindProperty] public string Bekrafta { get; set; } = "";

    public string Meddelande { get; set; } = "";
    public bool Success { get; set; }

    private string UserId => HttpContext.Session.GetString("user") ?? "";
    private string Epost => HttpContext.Session.GetString("epost") ?? "";

    public IActionResult OnGet()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");

        if (Epost == "demo")
        {
            Meddelande = "Demokontot kan inte byta lösenord.";
            return Page();
        }

        var cur = (Nuvarande ?? "").Trim();
        var ny = (Nytt ?? "").Trim();
        var bekr = (Bekrafta ?? "").Trim();

        if (cur.Length == 0 || ny.Length == 0 || bekr.Length == 0)
        {
            Meddelande = "Fyll i alla fält.";
            return Page();
        }
        if (ny.Length < 6)
        {
            Meddelande = "Det nya lösenordet måste vara minst 6 tecken.";
            return Page();
        }
        if (ny != bekr)
        {
            Meddelande = "De nya lösenorden matchar inte.";
            return Page();
        }
        if (ny == cur)
        {
            Meddelande = "Det nya lösenordet får inte vara samma som det nuvarande.";
            return Page();
        }

        // Verifiera nuvarande lösenord med samma logik som inloggningen.
        var user = await _api.AuthenticateUser(Epost, cur);
        if (user == null)
        {
            Meddelande = "Nuvarande lösenord är felaktigt.";
            return Page();
        }

        // Lagra nytt lösenord som MD5(nytt + salt) — samma schema som lösenordsåterställningen.
        var hash = (await _api.GetHash(ny)).Trim('"');
        await _api.NewPwdEP(new User { UserName = Epost, Password = hash });

        Meddelande = "Ditt lösenord har uppdaterats.";
        Success = true;
        return Page();
    }
}
