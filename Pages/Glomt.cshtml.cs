using HB_Claude.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HB_Claude.Pages;

public class GlömtModel : PageModel
{
    private readonly ApiService _api;

    [BindProperty] public string Epost { get; set; } = "";
    public string Meddelande { get; set; } = "";
    public bool Success { get; set; }

    public GlömtModel(ApiService api) => _api = api;

    public void OnGet() { }

    public async Task OnPostAsync()
    {
        if (Epost.Length < 5)
        {
            Meddelande = "Ange en giltig e-postadress!";
            return;
        }

        var testUser = new Models.User { UserName = Epost };
        if (!await _api.FinnsUser(testUser))
        {
            Meddelande = "E-postadressen finns inte registrerad!";
            return;
        }

        var pw = ApiService.RandPwd();
        var hash = await _api.GetHash(pw);

        var user = new Models.User
        {
            UserName = Epost,
            Password = hash.Trim('"')
        };

        if (_api.SkickaMail(Epost, "MatchMate - nytt lösenord", $"Ditt nya lösenord är: {pw}"))
        {
            await _api.NewPwdEP(user);
            Meddelande = "Ett nytt lösenord har skickats till din e-post!";
            Success = true;
        }
        else
        {
            Meddelande = "Kunde inte skicka e-post. Försök igen senare.";
        }
    }
}
