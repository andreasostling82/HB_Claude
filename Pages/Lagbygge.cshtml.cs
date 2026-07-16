using HB_Claude.Models;
using HB_Claude.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HB_Claude.Pages;

public class LagbyggeModel : PageModel
{
    private readonly ApiService _api;
    public LagbyggeModel(ApiService api) => _api = api;

    public List<Lag> LagLista { get; set; } = new();
    public List<Spelare> SpelareLista { get; set; } = new();

    [BindProperty] public string ValtLagID { get; set; } = "";
    [BindProperty] public string LagNamn { get; set; } = "";
    [BindProperty] public string HemmaHall { get; set; } = "";
    [BindProperty] public string Serie { get; set; } = "";
    [BindProperty] public string KM { get; set; } = "M";

    [BindProperty] public string ValtSpelarID { get; set; } = "";
    [BindProperty] public string SpFörnamn { get; set; } = "";
    [BindProperty] public string SpEfternamn { get; set; } = "";
    [BindProperty] public string SpNummer { get; set; } = "";
    [BindProperty] public string SpXNummer { get; set; } = "";
    [BindProperty] public string SpPosition { get; set; } = "VB";

    public string Meddelande { get; set; } = "";
    private string UserId => HttpContext.Session.GetString("user") ?? "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        ValtLagID = HttpContext.Session.GetString("lag") ?? "";
        await LaddaLag();
        if (!string.IsNullOrEmpty(ValtLagID))
        {
            await FyllLagInfo();
            await LaddaSpelare();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        if (!string.IsNullOrEmpty(ValtLagID)) HttpContext.Session.SetString("lag", ValtLagID);
        await LaddaLag();
        if (!string.IsNullOrEmpty(ValtLagID))
        {
            await FyllLagInfo();
            await LaddaSpelare();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSparaLagAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        var lag = new Lag
        {
            LagID = ValtLagID,
            Namn = LagNamn,
            HemmaHall = HemmaHall,
            Serie = Serie,
            KM = KM,
            UserID = UserId
        };
        await _api.EditLag(lag);
        Meddelande = "Lag sparat!";
        await LaddaLag();
        await FyllLagInfo();
        await LaddaSpelare();
        return Page();
    }

    public async Task<IActionResult> OnPostRadereLagAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        var lag = new Lag { LagID = ValtLagID };
        await _api.DelLag(lag);
        ValtLagID = "";
        HttpContext.Session.Remove("lag");
        Meddelande = "Laget har tagits bort.";
        await LaddaLag();
        return Page();
    }

    public async Task<IActionResult> OnPostLaggTillSpelareAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        var sp = new Spelare
        {
            LagID = ValtLagID,
            Förnamn = SpFörnamn.Trim(),
            Efternamn = SpEfternamn.Trim(),
            Nummer = SpNummer.Trim(),
            XNummer = SpXNummer.Trim(),
            Position = SpPosition
        };
        await _api.AddPlayer(sp);
        await LaddaLag();
        await FyllLagInfo();
        await LaddaSpelare();
        return Page();
    }

    public async Task<IActionResult> OnPostSparaSpelareAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        var sp = new Spelare
        {
            SpelareID = ValtSpelarID,
            LagID = ValtLagID,
            Förnamn = SpFörnamn.Trim(),
            Efternamn = SpEfternamn.Trim(),
            Nummer = SpNummer.Trim(),
            XNummer = SpXNummer.Trim(),
            Position = SpPosition
        };
        await _api.UpdateSpelare(sp);
        await LaddaLag();
        await FyllLagInfo();
        await LaddaSpelare();
        return Page();
    }

    public async Task<IActionResult> OnPostRaderaSpelareAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        await _api.DelPlayer(ValtSpelarID);
        ValtSpelarID = "";
        await LaddaLag();
        await FyllLagInfo();
        await LaddaSpelare();
        return Page();
    }

    private async Task LaddaLag()
    {
        LagLista = await _api.GetLagFromUser(UserId) ?? new();
        if (string.IsNullOrEmpty(ValtLagID) && LagLista.Any())
            ValtLagID = LagLista.First().LagID;
    }

    private async Task FyllLagInfo()
    {
        var lag = (await _api.GetLagFromUser(UserId))?.FirstOrDefault(l => l.LagID == ValtLagID);
        if (lag != null)
        {
            LagNamn = lag.Namn;
            HemmaHall = lag.HemmaHall;
            Serie = lag.Serie;
            KM = lag.KM;
        }
    }

    private async Task LaddaSpelare()
    {
        var sps = await _api.GetListaOfSpelare(ValtLagID);
        SpelareLista = sps?.OrderBy(s => { int.TryParse(s.Nummer, out var n); return n; }).ToList() ?? new();
    }
}
