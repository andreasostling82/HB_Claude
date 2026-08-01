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
    [BindProperty] public string SpPosition { get; set; } = "MV";

    [BindProperty] public string DelegatEpost { get; set; } = "";
    public List<string> Delegater { get; set; } = new();

    public string Meddelande { get; set; } = "";
    private string UserId => HttpContext.Session.GetString("user") ?? "";
    private string Epost => HttpContext.Session.GetString("epost") ?? "";

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
        if (!int.TryParse(SpNummer?.Trim(), out _))
        {
            Meddelande = "Ange ett nummer för spelaren.";
            await LaddaLag();
            await FyllLagInfo();
            await LaddaSpelare();
            return Page();
        }
        var sp = new Spelare
        {
            LagID = ValtLagID,
            Förnamn = (SpFörnamn ?? "").Trim(),
            Efternamn = (SpEfternamn ?? "").Trim(),
            Nummer = (SpNummer ?? "").Trim(),
            XNummer = (SpXNummer ?? "").Trim(),
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
        if (!int.TryParse(SpNummer?.Trim(), out _))
        {
            Meddelande = "Ange ett nummer för spelaren.";
            await LaddaLag();
            await FyllLagInfo();
            await LaddaSpelare();
            return Page();
        }
        var sp = new Spelare
        {
            SpelareID = ValtSpelarID,
            LagID = ValtLagID,
            Förnamn = (SpFörnamn ?? "").Trim(),
            Efternamn = (SpEfternamn ?? "").Trim(),
            Nummer = (SpNummer ?? "").Trim(),
            XNummer = (SpXNummer ?? "").Trim(),
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

    // Ge en e-postadress samma åtkomst till lagen som inloggat konto (delegat).
    // Skapar ett konto och mailar lösenord om adressen saknar konto.
    public async Task<IActionResult> OnPostBjudInAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        if (string.IsNullOrEmpty(ValtLagID)) ValtLagID = HttpContext.Session.GetString("lag") ?? "";

        var epost = (DelegatEpost ?? "").Trim();
        if (epost.Length < 5 || !epost.Contains('@'))
            Meddelande = "Ange en giltig e-postadress.";
        else if (string.Equals(epost, Epost, StringComparison.OrdinalIgnoreCase))
            Meddelande = "Du kan inte bjuda in dig själv.";
        else
        {
            var befintlig = await _api.GetUserByEmail(epost);
            string inviteId;
            if (befintlig == null)
            {
                var pwd = ApiService.RandPwd();
                var hash = (await _api.GetHash(pwd)).Trim('"');
                inviteId = await _api.CreateUser(epost, hash);
                var mailat = _api.SkickaMail(epost, "MatchMate – du har fått åtkomst",
                    $"Du har fått åtkomst till lagen i MatchMate.\nLogga in med din e-post och lösenordet: {pwd}");
                Meddelande = mailat
                    ? $"{epost} har fått åtkomst och ett lösenord har mailats."
                    : $"{epost} har fått åtkomst. Kunde inte maila lösenordet – be personen använda 'Glömt lösenord'.";
            }
            else
            {
                inviteId = befintlig.UserID;
                Meddelande = $"{epost} har nu åtkomst till dina lag.";
            }
            await _api.AddDelegate(inviteId, UserId);
        }

        await LaddaLag();
        if (!string.IsNullOrEmpty(ValtLagID)) { await FyllLagInfo(); await LaddaSpelare(); }
        return Page();
    }

    public async Task<IActionResult> OnPostTaBortDelegatAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        if (string.IsNullOrEmpty(ValtLagID)) ValtLagID = HttpContext.Session.GetString("lag") ?? "";

        var befintlig = await _api.GetUserByEmail((DelegatEpost ?? "").Trim());
        if (befintlig != null)
        {
            await _api.RemoveDelegate(befintlig.UserID, UserId);
            Meddelande = $"{befintlig.UserName} har inte längre åtkomst.";
        }

        await LaddaLag();
        if (!string.IsNullOrEmpty(ValtLagID)) { await FyllLagInfo(); await LaddaSpelare(); }
        return Page();
    }

    private async Task LaddaLag()
    {
        LagLista = await _api.GetLagFromUser(UserId) ?? new();
        if (string.IsNullOrEmpty(ValtLagID) && LagLista.Any())
            ValtLagID = LagLista.First().LagID;
        Delegater = await _api.GetDelegatesFor(UserId);
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
