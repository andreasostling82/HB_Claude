using HB_Claude.Models;
using HB_Claude.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HB_Claude.Pages;

public class NyttLagModel : PageModel
{
    private readonly ApiService _api;
    public NyttLagModel(ApiService api) => _api = api;

    [BindProperty] public string LagNamn { get; set; } = "";
    [BindProperty] public string HemmaHall { get; set; } = "";
    [BindProperty] public string Serie { get; set; } = "";
    [BindProperty] public string KM { get; set; } = "M";

    public string Meddelande { get; set; } = "";
    public bool Success { get; set; }

    private string UserId => HttpContext.Session.GetString("user") ?? "";

    public IActionResult OnGet()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");

        if (string.IsNullOrWhiteSpace(LagNamn))
        {
            Meddelande = "Lagnamn måste anges!";
            return Page();
        }

        var lag = new Lag
        {
            Namn = LagNamn.Trim(),
            HemmaHall = HemmaHall.Trim(),
            Serie = Serie.Trim(),
            KM = KM,
            UserID = UserId
        };

        if (await _api.AddLag(lag))
        {
            Meddelande = "Laget har skapats!";
            Success = true;
        }
        else
        {
            Meddelande = "Kunde inte skapa laget. Försök igen.";
        }

        return Page();
    }
}
