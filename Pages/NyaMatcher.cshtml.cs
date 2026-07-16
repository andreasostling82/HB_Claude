using HB_Claude.Models;
using HB_Claude.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HB_Claude.Pages;

public class NyaMatcherModel : PageModel
{
    private readonly ApiService _api;
    public NyaMatcherModel(ApiService api) => _api = api;

    public List<Lag> LagLista { get; set; } = new();

    [BindProperty] public string ValtLagID { get; set; } = "";
    [BindProperty] public string Datum { get; set; } = "";
    [BindProperty] public string Motståndare { get; set; } = "";
    [BindProperty] public string Plats { get; set; } = "";

    public string Meddelande { get; set; } = "";
    private string UserId => HttpContext.Session.GetString("user") ?? "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        LagLista = await _api.GetLagFromUser(UserId) ?? new();
        ValtLagID = HttpContext.Session.GetString("lag") ?? "";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        LagLista = await _api.GetLagFromUser(UserId) ?? new();

        var lagId = string.IsNullOrEmpty(ValtLagID) ? HttpContext.Session.GetString("lag") ?? "" : ValtLagID;
        if (string.IsNullOrEmpty(lagId)) { Meddelande = "Välj ett lag!"; return Page(); }

        if (!DateTime.TryParse(Datum, out _)) { Meddelande = "Ange ett korrekt datum!"; return Page(); }

        var match = new Match
        {
            LagID = lagId,
            MatchID = ApiService.RandomString(),
            Datum = Datum,
            Motståndare = string.IsNullOrWhiteSpace(Motståndare) ? "Motståndarna" : Motståndare.Trim(),
            Plats = string.IsNullOrWhiteSpace(Plats) ? "Plats" : Plats.Trim(),
            Status = "Planerad"
        };

        var matchId = await _api.AddMatch(match);
        HttpContext.Session.SetString("match", matchId.Trim('"'));
        HttpContext.Session.SetString("lag", lagId);
        return RedirectToPage("/Events");
    }
}
