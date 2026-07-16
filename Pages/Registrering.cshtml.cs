using HB_Claude.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HB_Claude.Pages;

public class RegistreringModel : PageModel
{
    private readonly ApiService _api;

    [BindProperty] public string Epost { get; set; } = "";
    [BindProperty] public string AntalLag { get; set; } = "1";
    public string Meddelande { get; set; } = "";
    public bool Success { get; set; }

    public RegistreringModel(ApiService api) => _api = api;

    public void OnGet() { }

    public async Task OnPostAsync()
    {
        if (!int.TryParse(AntalLag, out _))
        {
            Meddelande = "Antal lag måste anges med en siffra!";
            return;
        }
        if (Epost.Length < 5)
        {
            Meddelande = "E-post måste anges!";
            return;
        }

        var testUser = new Models.User { UserName = Epost };
        if (await _api.FinnsUser(testUser))
        {
            Meddelande = "E-post existerar redan!";
            return;
        }

        var pw = ApiService.RandPwd();
        var hash = await _api.GetHash(pw);

        var user = new Models.User
        {
            UserName = Epost,
            Password = hash.Trim('"'),
            status = "aktiv",
            typ = AntalLag
        };

        await _api.NewUser(user);

        if (_api.SkickaMail(Epost, "MatchMate", $"Ditt lösenord är: {pw}"))
        {
            Meddelande = "Ditt konto är skapat! E-posten är ditt användarnamn. Lösenordet skickas till dig via e-post.";
        }
        else
        {
            Meddelande = $"Ditt konto är skapat men e-post med lösenord kunde inte skickas. Ditt tillfälliga lösenord är: {pw}";
        }
        Success = true;
    }
}
