using HB_Claude.Models;
using HB_Claude.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HB_Claude.Pages;

public class SasongsstatModel : PageModel
{
    private readonly ApiService _api;
    public SasongsstatModel(ApiService api) => _api = api;

    public List<Lag> LagLista { get; set; } = new();
    public List<SäsongsSpelarStat> Spelare { get; set; } = new();
    public List<SäsongsMvStat> Malvakter { get; set; } = new();
    public List<Match> AntalMatcher { get; set; } = new();
    public int TotaltMal { get; set; }

    [BindProperty] public string ValtLagID { get; set; } = "";

    private string UserId => HttpContext.Session.GetString("user") ?? "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        ValtLagID = HttpContext.Session.GetString("lag") ?? "";
        await LaddaData();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        if (!string.IsNullOrEmpty(ValtLagID)) HttpContext.Session.SetString("lag", ValtLagID);
        await LaddaData();
        return Page();
    }

    private async Task LaddaData()
    {
        LagLista = await _api.GetLagFromUser(UserId) ?? new();
        if (string.IsNullOrEmpty(ValtLagID)) return;

        AntalMatcher = await _api.GetMatchInfoAvslutade(ValtLagID);
        if (!AntalMatcher.Any()) return;

        // Hämta alla matchdata parallellt
        var matchData = await Task.WhenAll(AntalMatcher.Select(async m => (
            Spelare: await _api.GetHandelseSamEJ_MV(m.MatchID) ?? new(),
            Mv: await _api.GetMalvakt2(m.MatchID) ?? new()
        )));

        var spelarDict = new Dictionary<string, SäsongsSpelarStat>();
        var mvDict = new Dictionary<string, SäsongsMvStat>();

        foreach (var (spelare, mv) in matchData)
        {
            foreach (var s in spelare)
            {
                if (!spelarDict.TryGetValue(s.namn, out var stat))
                {
                    stat = new SäsongsSpelarStat { Namn = s.namn, Position = s.Position };
                    spelarDict[s.namn] = stat;
                }
                stat.Matcher++;
                stat.Mål += int.TryParse(s.Mål, out var g) ? g : 0;
                stat.Avslut += int.TryParse(s.Avslut, out var a) ? a : 0;
                stat.TekniskaFel += int.TryParse(s.TekniskaFel, out var tf) ? tf : 0;
            }

            foreach (var m in mv)
            {
                if (!mvDict.TryGetValue(m.Namn, out var stat))
                {
                    stat = new SäsongsMvStat { Namn = m.Namn };
                    mvDict[m.Namn] = stat;
                }
                stat.Matcher++;
                stat.Räddningar += int.TryParse(m.Raddningar, out var r) ? r : 0;
                stat.InsläpptaMål += int.TryParse(m.Mål, out var im) ? im : 0;
            }
        }

        Spelare = spelarDict.Values
            .OrderByDescending(s => s.Mål)
            .ThenByDescending(s => s.Avslut)
            .ToList();

        Malvakter = mvDict.Values
            .OrderByDescending(m => m.Räddningar)
            .ToList();

        TotaltMal = Spelare.Sum(s => s.Mål);
    }
}

public class SäsongsSpelarStat
{
    public string Namn { get; set; } = "";
    public string Position { get; set; } = "";
    public int Matcher { get; set; }
    public int Mål { get; set; }
    public int Avslut { get; set; }
    public int TekniskaFel { get; set; }
    public string MålPerAvslut => Avslut > 0 ? $"{(double)Mål / Avslut:0.00}" : "-";
}

public class SäsongsMvStat
{
    public string Namn { get; set; } = "";
    public int Matcher { get; set; }
    public int Räddningar { get; set; }
    public int InsläpptaMål { get; set; }
    public string Procent => (Räddningar + InsläpptaMål) > 0
        ? $"{(double)Räddningar / (Räddningar + InsläpptaMål) * 100:0.1} %"
        : "-";
}
