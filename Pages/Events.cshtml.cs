using HB_Claude.Models;
using HB_Claude.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HB_Claude.Pages;

public class EventsModel : PageModel
{
    private readonly ApiService _api;

    public EventsModel(ApiService api) => _api = api;

    // Dropdowns
    public List<Lag> LagLista { get; set; } = new();
    public List<Match> MatchLista { get; set; } = new();
    public List<HBSpelare> SpelareLista { get; set; } = new();

    // State
    [BindProperty] public string ValtLagID { get; set; } = "";
    [BindProperty] public string ValtMatchID { get; set; } = "";
    [BindProperty] public string AktivSpelareID { get; set; } = "";
    [BindProperty] public string AktivSpelarPosition { get; set; } = "";
    [BindProperty] public string HandelseTyp { get; set; } = "";
    [BindProperty] public string ZonVal { get; set; } = "0";
    [BindProperty] public string FasVal { get; set; } = "1";
    [BindProperty] public int MatchTid { get; set; }

    // Display
    public string MatchStatus { get; set; } = "";
    public bool IsPaus { get; set; } = true;
    public string TidText { get; set; } = "00:00";
    public string StatusText { get; set; } = "";
    public string SummaryText { get; set; } = "";
    public string Felmeddelande { get; set; } = "";
    public List<EventsTyp> Händelser { get; set; } = new();

    private string UserId => HttpContext.Session.GetString("user") ?? "";
    private string SessionLag => HttpContext.Session.GetString("lag") ?? "";
    private string SessionMatch => HttpContext.Session.GetString("match") ?? "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");

        ValtLagID = SessionLag;
        ValtMatchID = SessionMatch;
        MatchTid = int.TryParse(HttpContext.Session.GetString("matchTid"), out var t) ? t : 0;
        MatchStatus = HttpContext.Session.GetString("matchStatus") ?? "";
        IsPaus = HttpContext.Session.GetString("isPaus") != "false";

        await LaddaLag();
        if (!string.IsNullOrEmpty(ValtLagID))
            await LaddaMatchOchSpelare();
        if (!string.IsNullOrEmpty(ValtMatchID))
            await LaddaHändelser();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        SparaSesstion();
        await LaddaLag();
        if (!string.IsNullOrEmpty(ValtLagID))
            await LaddaMatchOchSpelare();
        if (!string.IsNullOrEmpty(ValtMatchID))
            await LaddaHändelser();
        return Page();
    }

    public async Task<IActionResult> OnPostStartStopAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        SparaSesstion();

        var matchId = HttpContext.Session.GetString("match") ?? ValtMatchID;
        if (string.IsNullOrEmpty(matchId)) { await LaddaAllt(); return Page(); }

        if (MatchStatus == "Pågående")
        {
            // Stop
            await _api.SetMatchStatus(matchId, "Avslutad");
            HttpContext.Session.SetString("matchStatus", "Avslutad");
            MatchStatus = "Avslutad";
            IsPaus = true;
            HttpContext.Session.SetString("isPaus", "true");
        }
        else
        {
            // Start
            var h = new Händelse
            {
                MatchID = matchId,
                SpelareID = "0",
                HändelseID = "0",
                Tids = "0",
                Fas = "0",
                Zon = "0"
            };
            await _api.AddHändelse(h);
            await _api.SetMatchStatus(matchId, "Pågående");
            HttpContext.Session.SetString("matchStatus", "Pågående");
            HttpContext.Session.SetString("matchStart", DateTime.Now.ToString("O"));
            MatchStatus = "Pågående";
            MatchTid = 0;
            HttpContext.Session.SetString("matchTid", "0");
            HttpContext.Session.SetString("matchTidFor", matchId);
            IsPaus = false;
            HttpContext.Session.SetString("isPaus", "false");
        }

        await LaddaAllt();
        return Page();
    }

    public async Task<IActionResult> OnPostPausFortsattAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        SparaSesstion();

        IsPaus = HttpContext.Session.GetString("isPaus") != "false";
        IsPaus = !IsPaus;
        HttpContext.Session.SetString("isPaus", IsPaus ? "true" : "false");

        await LaddaAllt();
        return Page();
    }

    public async Task<IActionResult> OnPostHändelseAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        SparaSesstion();

        var lagId = HttpContext.Session.GetString("lag") ?? ValtLagID;
        var matchId = HttpContext.Session.GetString("match") ?? ValtMatchID;
        var spId = AktivSpelareID;

        if (string.IsNullOrEmpty(lagId)) { Felmeddelande = "Inget lag valt!"; await LaddaAllt(); return Page(); }
        if (string.IsNullOrEmpty(matchId)) { Felmeddelande = "Ingen match vald!"; await LaddaAllt(); return Page(); }
        if (string.IsNullOrEmpty(spId)) { Felmeddelande = "Ingen spelare vald!"; await LaddaAllt(); return Page(); }

        var händelse = BuildHändelse(matchId, spId);
        var result = await _api.AddMultiHändelse3(händelse);
        if (result != null)
            Händelser = result;
        else
            await LaddaHändelser();

        // Reset player selection after recording
        AktivSpelareID = "";
        AktivSpelarPosition = "";

        await LaddaLag();
        await LaddaMatchOchSpelare();
        await UppdateraSummary(matchId);
        return Page();
    }

    private Händelse BuildHändelse(string matchId, string spId)
    {
        var rawTyp = HandelseTyp;
        var isMV = AktivSpelarPosition == "MV";

        string händelseNamn;
        if (isMV)
        {
            händelseNamn = rawTyp; // MV events are already named
        }
        else
        {
            // Technical faults
            if (new[] { "Övertramp", "Offensiv_stuermer", "Felaktig_spärr", "Fot", "Stegfel",
                        "Passmiss", "Tappad_boll", "Dubbelstuds", "Övrigt_regelfel" }.Contains(rawTyp))
            {
                händelseNamn = $"TeknisktFel_{rawTyp}";
            }
            else if (rawTyp == "Assist")
            {
                händelseNamn = rawTyp;
            }
            else
            {
                // Field player shot/save events - prefix with fas
                var fasPrefix = FasVal switch { "1" => "Uppst", "2" => "Fas1", _ => "Fas2" };
                händelseNamn = $"{fasPrefix}_{rawTyp}";
            }
        }

        var zon = "0";
        if (!händelseNamn.Contains("TeknisktFel") && !händelseNamn.Contains("Straff") && !isMV)
            zon = ZonVal;

        return new Händelse
        {
            Händelsen = händelseNamn,
            MatchID = matchId,
            SpelareID = spId,
            HändelseID = "",
            Tids = MatchTid.ToString(),
            Fas = isMV ? "0" : FasVal,
            Zon = zon
        };
    }

    private void SparaSesstion()
    {
        bool matchChanged = !string.IsNullOrEmpty(ValtMatchID) &&
                            ValtMatchID != (HttpContext.Session.GetString("match") ?? "");

        if (!string.IsNullOrEmpty(ValtLagID)) HttpContext.Session.SetString("lag", ValtLagID);
        if (!string.IsNullOrEmpty(ValtMatchID)) HttpContext.Session.SetString("match", ValtMatchID);

        if (!matchChanged)
        {
            // Only save time when the match hasn't changed — on match switch the posted
            // MatchTid belongs to the old match and must not be carried over
            HttpContext.Session.SetString("matchTid", MatchTid.ToString());
            if (!string.IsNullOrEmpty(ValtMatchID))
                HttpContext.Session.SetString("matchTidFor", ValtMatchID);
        }

        MatchStatus = HttpContext.Session.GetString("matchStatus") ?? "";
        IsPaus = HttpContext.Session.GetString("isPaus") != "false";
    }

    private async Task LaddaAllt()
    {
        await LaddaLag();
        if (!string.IsNullOrEmpty(ValtLagID))
            await LaddaMatchOchSpelare();
        if (!string.IsNullOrEmpty(ValtMatchID))
            await LaddaHändelser();
    }

    private async Task LaddaLag()
    {
        LagLista = await _api.GetLagFromUser(UserId) ?? new();
        if (string.IsNullOrEmpty(ValtLagID) && LagLista.Any())
            ValtLagID = LagLista.First().LagID;
    }

    private async Task LaddaMatchOchSpelare()
    {
        var matcher = await _api.GetMatchInfoEJ_Avslutade(ValtLagID);
        MatchLista = matcher;
        if (string.IsNullOrEmpty(ValtMatchID) && MatchLista.Any())
            ValtMatchID = "";

        var spelare = await _api.GetListaOfSpelare(ValtLagID);
        if (spelare != null)
        {
            SpelareLista = spelare.Select(s => new HBSpelare
            {
                SpID = s.SpelareID,
                Namn = string.IsNullOrEmpty(s.XNummer)
                    ? $"{s.Nummer} {s.Förnamn.Trim()} {s.Efternamn.Trim()} {s.Position}"
                    : $"{s.Nummer} ({s.XNummer}) {s.Förnamn.Trim()} {s.Efternamn.Trim()} {s.Position}",
                position = s.Position,
                Nummer = s.Nummer
            }).OrderBy(s => { int.TryParse(s.Nummer, out var n); return n; }).ToList();
        }

        if (!string.IsNullOrEmpty(ValtMatchID))
        {
            var match = await _api.GetMatch(ValtLagID, ValtMatchID);
            if (match != null)
            {
                var lagNamn = await _api.GetLagNamn(UserId, ValtLagID);
                MatchStatus = match.Status;
                HttpContext.Session.SetString("matchStatus", MatchStatus);
                StatusText = $"{lagNamn} - {match.Motståndare}  Status: {match.Status}";

                if (match.Status == "Pågående")
                {
                    var sessionMatchForTid = HttpContext.Session.GetString("matchTidFor");
                    var sessionTidStr = HttpContext.Session.GetString("matchTid");

                    if (sessionMatchForTid == ValtMatchID && int.TryParse(sessionTidStr, out var st))
                    {
                        // Session holds a valid time for this match — use it so the clock
                        // doesn't regress after Paus/Fortsätt or event recording.
                        // IsPaus is already read from session; do NOT override it here.
                        MatchTid = st;
                    }
                    else
                    {
                        // First load for this match (no session data or match just changed)
                        // — initialize from the last recorded event in the DB.
                        var maxTid = await _api.GetMaxTid(ValtMatchID);
                        MatchTid = maxTid;
                        HttpContext.Session.SetString("matchTid", maxTid.ToString());
                        HttpContext.Session.SetString("matchTidFor", ValtMatchID);
                        IsPaus = false;
                        HttpContext.Session.SetString("isPaus", "false");
                    }
                }
            }
        }
    }

    private async Task LaddaHändelser()
    {
        var result = await _api.GetHandelseUppdelad2(ValtMatchID);
        if (result != null) Händelser = result;
        await UppdateraSummary(ValtMatchID);
    }

    private async Task UppdateraSummary(string matchId)
    {
        try
        {
            var mv = await _api.GetMalvakt(matchId);
            if (mv != null && mv.Any())
            {
                int antM = Händelser.Count(h => h.Mål == "1");
                int antA = Händelser.Count(h => h.Avslut == "1");
                int antF = Händelser.Count(h => h.TeknisktFel == "1");
                var mvMal = mv[0].Mål;
                var mvRadd = mv[0].Raddningar;
                var pct = double.TryParse((mv[0].Procent ?? "0").Replace(".", ","), out var p) ? p : 0;
                var avslutPct = antA > 0 ? (double)antM / antA : 0;
                SummaryText = $"Mål: {antM} - {mvMal}  Avslut: {antA} ({avslutPct:P0})  Räddningar: {mvRadd} ({pct:P0})  Fel: {antF}";
            }
        }
        catch { }
    }

    // =====================================================================
    //  Offline-stöd (JSON-endpoints som offline-events.js använder)
    // =====================================================================

    // GET /Events?handler=OfflineBundle&lagId=123
    // Referensdata som klienten cachar i IndexedDB för offline-registrering.
    public async Task<IActionResult> OnGetOfflineBundleAsync(string lagId)
    {
        if (string.IsNullOrEmpty(UserId)) return new JsonResult(new { error = "auth" }) { StatusCode = 401 };
        if (string.IsNullOrEmpty(lagId)) return new JsonResult(new { error = "lagId" }) { StatusCode = 400 };

        var spelare = await _api.GetListaOfSpelare(lagId) ?? new();
        var matcher = await _api.GetMatchInfoEJ_Avslutade(lagId);
        var eventTypes = await _api.GetEventTypes();

        var players = spelare.Select(s => new
        {
            spId = s.SpelareID,
            nummer = s.Nummer,
            xnummer = s.XNummer,
            fornamn = s.Förnamn.Trim(),
            efternamn = s.Efternamn.Trim(),
            position = s.Position
        });

        var matches = matcher.Select(m => new
        {
            matchId = m.MatchID,
            datum = m.Datum,
            motstandare = m.Motståndare,
            status = m.Status
        });

        var types = eventTypes.Select(t => new { id = t.Id, text = t.Text, isGoal = t.IsGoal });

        return new JsonResult(new { lagId, players, matches, eventTypes = types });
    }

    // POST /Events?handler=Sync  (JSON-kropp: { ops: [...] })
    // Applicerar köade offline-operationer i ordning och returnerar bekräftade clientIds.
    public async Task<IActionResult> OnPostSyncAsync([FromBody] SyncBatch batch)
    {
        if (string.IsNullOrEmpty(UserId)) return new JsonResult(new { error = "auth" }) { StatusCode = 401 };

        var result = new SyncResult();
        if (batch?.Ops == null) return new JsonResult(result);

        foreach (var op in batch.Ops)
        {
            try
            {
                switch (op.Kind)
                {
                    case "status":
                        await _api.SetMatchStatus(op.MatchId, op.Status);
                        break;

                    case "startmarker":
                        await _api.AddHändelse(new Händelse
                        {
                            MatchID = op.MatchId,
                            SpelareID = "0",
                            HändelseID = "0",
                            Tids = op.Tids,
                            Fas = "0",
                            Zon = "0"
                        }, op.ClientId);
                        break;

                    default: // "event"
                        await _api.AddMultiHändelse3(new Händelse
                        {
                            Händelsen = op.Handelsen,
                            MatchID = op.MatchId,
                            SpelareID = op.PlayerId,
                            HändelseID = "",
                            Tids = op.Tids,
                            Fas = op.Fas,
                            Zon = op.Zon
                        }, op.ClientId);
                        break;
                }
                result.Confirmed.Add(op.ClientId);
            }
            catch
            {
                // Stoppa vid första felet så ordningen bevaras – resten kan synkas senare.
                result.Failed.Add(op.ClientId);
                break;
            }
        }

        return new JsonResult(result);
    }
}
