using HB_Claude.Models;
using HB_Claude.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;

namespace HB_Claude.Pages;

public class StatsModel : PageModel
{
    private readonly ApiService _api;
    public StatsModel(ApiService api) => _api = api;

    public List<Lag> LagLista { get; set; } = new();
    public List<Match> MatchLista { get; set; } = new();
    public List<HBSpelare> SpelareLista { get; set; } = new();
    public List<EventsTyp> HändelserLista { get; set; } = new();
    public List<EventsSam> SammanfattningLista { get; set; } = new();
    public List<Malvakt2> MalvaktLista { get; set; } = new();

    [BindProperty] public string ValtLagID { get; set; } = "";
    [BindProperty] public string ValtMatchID { get; set; } = "";
    [BindProperty] public string ValtSpelarID { get; set; } = "0";

    private string UserId => HttpContext.Session.GetString("user") ?? "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        ValtLagID = HttpContext.Session.GetString("lag") ?? "";
        ValtMatchID = HttpContext.Session.GetString("statsMatch") ?? "";
        await LaddaData();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        if (!string.IsNullOrEmpty(ValtLagID)) HttpContext.Session.SetString("lag", ValtLagID);
        if (!string.IsNullOrEmpty(ValtMatchID)) HttpContext.Session.SetString("statsMatch", ValtMatchID);
        await LaddaData();
        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        if (string.IsNullOrEmpty(ValtMatchID)) return RedirectToPage("/Stats");

        var events = await _api.GetHandelseUppdelad2(ValtMatchID) ?? new();
        var sb = new StringBuilder();
        sb.AppendLine("Tid;Namn;Typ;Handelse;TeknisktFel;Avslut;Zon;Mal");
        foreach (var h in events)
        {
            sb.AppendLine($"{h.Tid};{Rensa(h.Namn)};{h.Typ};{Rensa(h.Händelse)};{h.TeknisktFel};{h.Avslut};{h.Zon};{h.Mål}");
        }

        var lagNamn = LagLista.FirstOrDefault(l => l.LagID == ValtLagID)?.Namn ?? "Lag";
        var matchTitel = MatchLista.FirstOrDefault(m => m.MatchID == ValtMatchID)?.Titel ?? ValtMatchID;
        var filnamn = $"Stat_{lagNamn}_{matchTitel.Replace(" ", "_").Replace("/", "_")}.csv";

        return File(Encoding.Latin1.GetBytes(sb.ToString()), "text/csv", filnamn);
    }

    private static string Rensa(string s) =>
        s.Replace("å", "a").Replace("ä", "a").Replace("ö", "o")
         .Replace("Å", "A").Replace("Ä", "A").Replace("Ö", "O");

    private async Task LaddaData()
    {
        LagLista = await _api.GetLagFromUser(UserId) ?? new();

        if (!string.IsNullOrEmpty(ValtLagID))
        {
            MatchLista = await _api.GetMatchInfoAvslutade(ValtLagID);

            var sps = await _api.GetListaOfSpelare(ValtLagID);
            if (sps != null)
            {
                SpelareLista = new List<HBSpelare> { new() { SpID = "0", Namn = "0 Alla" } };
                SpelareLista.AddRange(sps.Select(s => new HBSpelare
                {
                    SpID = s.SpelareID,
                    Namn = $"{s.Nummer} {s.Efternamn.Trim()}",
                    position = s.Position
                }).OrderBy(s => s.Namn));
            }
        }

        if (!string.IsNullOrEmpty(ValtMatchID))
        {
            var allEvents = await _api.GetHandelseUppdelad2(ValtMatchID) ?? new();

            if (ValtSpelarID == "0" || string.IsNullOrEmpty(ValtSpelarID))
            {
                HändelserLista = allEvents;
                SammanfattningLista = await _api.GetHandelseSamEJ_MV(ValtMatchID) ?? new();
                MalvaktLista = await _api.GetMalvakt2(ValtMatchID) ?? new();
            }
            else
            {
                var spelarNamn = SpelareLista.FirstOrDefault(s => s.SpID == ValtSpelarID)?.Namn ?? "";
                HändelserLista = allEvents.Where(h => h.Namn == spelarNamn ||
                    h.Namn.Contains(spelarNamn.Split(' ').LastOrDefault() ?? "")).ToList();
            }
        }
    }
}
