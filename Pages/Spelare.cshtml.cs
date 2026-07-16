using HB_Claude.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HB_Claude.Pages;

public class SpelareModel : PageModel
{
    private readonly ApiService _api;
    public SpelareModel(ApiService api) => _api = api;

    [BindProperty(SupportsGet = true)] public string Namn { get; set; } = "";

    public string LagID { get; set; } = "";
    public string LagNamn { get; set; } = "";
    public string Position { get; set; } = "";
    public bool ÄrMålvakt { get; set; }

    // Fältspelartotaler
    public int Matcher { get; set; }
    public int Mål { get; set; }
    public int Avslut { get; set; }
    public int TekniskaFel { get; set; }
    public string MålPerAvslut => Avslut > 0 ? $"{(double)Mål / Avslut:0.00}" : "-";

    // Målvaktstotaler
    public int Räddningar { get; set; }
    public int InsläpptaMål { get; set; }
    public string RäddningsProcent => (Räddningar + InsläpptaMål) > 0
        ? $"{(double)Räddningar / (Räddningar + InsläpptaMål) * 100:0.0} %"
        : "-";

    public List<SpelarMatchRad> Matchrader { get; set; } = new();

    private string UserId => HttpContext.Session.GetString("user") ?? "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        await LaddaData();
        return Page();
    }

    public async Task<IActionResult> OnGetPdfAsync()
    {
        if (string.IsNullOrEmpty(UserId)) return RedirectToPage("/Index");
        await LaddaData();
        if (string.IsNullOrEmpty(Namn) || Matcher == 0) return NotFound();

        var pdf = ByggPdf();
        var filnamn = $"{Namn.Replace(' ', '_')}_statistik.pdf";
        return File(pdf, "application/pdf", filnamn);
    }

    private async Task LaddaData()
    {
        LagID = HttpContext.Session.GetString("lag") ?? "";
        if (string.IsNullOrEmpty(LagID) || string.IsNullOrEmpty(Namn)) return;

        LagNamn = (await _api.GetLagFromUser(UserId))?.FirstOrDefault(l => l.LagID == LagID)?.Namn ?? "";

        var matcher = await _api.GetMatchInfoAvslutade(LagID);
        if (!matcher.Any()) return;

        // Hämta all matchdata parallellt, men behåll matchordningen efteråt.
        var matchData = await Task.WhenAll(matcher.Select(async m => (
            Match: m,
            Fält: await _api.GetHandelseSamEJ_MV(m.MatchID) ?? new(),
            Mv: await _api.GetMalvakt2(m.MatchID) ?? new()
        )));

        foreach (var (m, fält, mv) in matchData)
        {
            var f = fält.FirstOrDefault(x => x.namn == Namn);
            if (f != null)
            {
                Position = f.Position;
                var mål = int.TryParse(f.Mål, out var g) ? g : 0;
                var avslut = int.TryParse(f.Avslut, out var a) ? a : 0;
                var tf = int.TryParse(f.TekniskaFel, out var t) ? t : 0;

                Matcher++;
                Mål += mål;
                Avslut += avslut;
                TekniskaFel += tf;

                Matchrader.Add(new SpelarMatchRad
                {
                    Datum = m.Datum,
                    Motståndare = m.Motståndare,
                    Mål = mål,
                    Avslut = avslut,
                    TekniskaFel = tf
                });
                continue;
            }

            var g2 = mv.FirstOrDefault(x => x.Namn == Namn);
            if (g2 != null)
            {
                ÄrMålvakt = true;
                Position = "MV";
                var räd = int.TryParse(g2.Raddningar, out var r) ? r : 0;
                var im = int.TryParse(g2.Mål, out var i) ? i : 0;

                Matcher++;
                Räddningar += räd;
                InsläpptaMål += im;

                Matchrader.Add(new SpelarMatchRad
                {
                    Datum = m.Datum,
                    Motståndare = m.Motståndare,
                    Räddningar = räd,
                    InsläpptaMål = im
                });
            }
        }
    }

    // ---- PDF ----

    private static readonly string Röd = "#C8282A";
    private static readonly string Grön = "#1a8a4f";
    private static readonly string Varning = "#b8860b";
    private static readonly string Grå = "#6b6b6b";
    private static readonly string Ljusgrå = "#f4f2ee";
    private static readonly string Kant = "#ddd7cd";

    private byte[] ByggPdf()
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Black));

                page.Header().Element(ComposeHeader);
                page.Content().PaddingTop(18).Element(ComposeContent);
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("MatchMate · Säsongsstatistik · ").FontSize(8).FontColor(Grå);
                    t.Span($"Genererad {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(8).FontColor(Grå);
                });
            });
        });

        return doc.GeneratePdf();
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(2).BorderColor(Röd).PaddingBottom(8).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(Namn).FontSize(20).Bold().FontColor("#222");
                row.ConstantItem(70).AlignRight().AlignMiddle()
                    .Background(Ljusgrå).PaddingVertical(4).PaddingHorizontal(8)
                    .Text(Position).FontSize(11).SemiBold().FontColor(Röd);
            });
            if (!string.IsNullOrEmpty(LagNamn))
                col.Item().PaddingTop(2).Text(LagNamn).FontSize(11).FontColor(Grå);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(18);

            // Summakort
            col.Item().Row(row =>
            {
                row.Spacing(10);
                Kort(row, "Matcher", Matcher.ToString(), "#222");
                if (ÄrMålvakt)
                {
                    Kort(row, "Räddningar", Räddningar.ToString(), Grön);
                    Kort(row, "Insläppta mål", InsläpptaMål.ToString(), Varning);
                    Kort(row, "Räddnings-%", RäddningsProcent, "#222");
                }
                else
                {
                    Kort(row, "Mål", Mål.ToString(), Röd);
                    Kort(row, "Avslut", Avslut.ToString(), "#222");
                    Kort(row, "Mål per avslut", MålPerAvslut, "#222");
                }
            });

            if (!ÄrMålvakt && TekniskaFel > 0)
            {
                col.Item().Row(row =>
                {
                    row.Spacing(10);
                    Kort(row, "Tekniska fel", TekniskaFel.ToString(), Varning);
                    row.RelativeItem(3);
                });
            }

            // Tabell per match
            col.Item().Text("Statistik per match").FontSize(9).Bold().FontColor(Grå)
                .LetterSpacing(0.05f);
            col.Item().Element(ComposeTabell);
        });
    }

    private void Kort(RowDescriptor row, string label, string värde, string färg)
    {
        row.RelativeItem().Border(1).BorderColor(Kant).Background(Colors.White)
            .Padding(10).Column(c =>
            {
                c.Item().Text(värde).FontSize(20).Bold().FontColor(färg);
                c.Item().PaddingTop(2).Text(label.ToUpper()).FontSize(7).FontColor(Grå).LetterSpacing(0.06f);
            });
    }

    private void ComposeTabell(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(2);   // Datum
                cols.RelativeColumn(3);   // Motståndare
                cols.RelativeColumn(1.5f);
                cols.RelativeColumn(1.5f);
                cols.RelativeColumn(1.5f);
            });

            void Rubrik(string text, bool höger = false)
            {
                var cell = table.Cell().Background(Ljusgrå).BorderBottom(1).BorderColor(Kant).PaddingVertical(5).PaddingHorizontal(6);
                var t = (höger ? cell.AlignRight() : cell.AlignLeft()).Text(text.ToUpper()).FontSize(7).Bold().FontColor(Grå);
            }

            Rubrik("Datum");
            Rubrik("Motståndare");
            if (ÄrMålvakt)
            {
                Rubrik("Räddningar", true);
                Rubrik("Insläppta", true);
                Rubrik("Räddn-%", true);
            }
            else
            {
                Rubrik("Mål", true);
                Rubrik("Avslut", true);
                Rubrik("T.Fel", true);
            }

            foreach (var r in Matchrader)
            {
                void Cell(string text, bool höger = false, string? färg = null, bool bold = false)
                {
                    var cell = table.Cell().BorderBottom(1).BorderColor(Kant).PaddingVertical(5).PaddingHorizontal(6);
                    var span = (höger ? cell.AlignRight() : cell.AlignLeft())
                        .Text(text).FontSize(9).FontColor(färg ?? "#222");
                    if (bold) span.Bold();
                }

                Cell(r.Datum, färg: Grå);
                Cell(r.Motståndare);
                if (ÄrMålvakt)
                {
                    Cell(r.Räddningar.ToString(), true, Grön, true);
                    Cell(r.InsläpptaMål.ToString(), true, Varning);
                    Cell(r.RäddningsProcent, true);
                }
                else
                {
                    Cell(r.Mål.ToString(), true, r.Mål > 0 ? Röd : Grå, r.Mål > 0);
                    Cell(r.Avslut.ToString(), true);
                    Cell(r.TekniskaFel > 0 ? r.TekniskaFel.ToString() : "", true, Varning);
                }
            }
        });
    }
}

public class SpelarMatchRad
{
    public string Datum { get; set; } = "";
    public string Motståndare { get; set; } = "";

    // Fältspelare
    public int Mål { get; set; }
    public int Avslut { get; set; }
    public int TekniskaFel { get; set; }
    public string MålPerAvslut => Avslut > 0 ? $"{(double)Mål / Avslut:0.00}" : "-";

    // Målvakt
    public int Räddningar { get; set; }
    public int InsläpptaMål { get; set; }
    public string RäddningsProcent => (Räddningar + InsläpptaMål) > 0
        ? $"{(double)Räddningar / (Räddningar + InsläpptaMål) * 100:0.0} %"
        : "-";
}
