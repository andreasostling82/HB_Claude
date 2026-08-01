using HB_Claude.Models;
using HB_Claude.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HB_Claude.Pages;

public class AdminModel : PageModel
{
    private readonly ApiService _api;
    public AdminModel(ApiService api) => _api = api;

    // Konton som får se admin-översikten.
    public static readonly string[] AdminEpost =
        { "niclass@bjorlund.com", "andreas@ingared.nu" };

    public List<KontoRad> Konton { get; set; } = new();

    public int TotaltKonton => Konton.Count;
    public int KontonMedLag => Konton.Count(k => k.AntalLag > 0);
    public int TotaltLag => Konton.Sum(k => k.AntalLag);
    public int TotaltMatcher => Konton.Sum(k => k.AntalMatcher);

    private string Epost => HttpContext.Session.GetString("epost") ?? "";

    public static bool ÄrAdmin(string? epost) =>
        epost != null && AdminEpost.Contains(epost, StringComparer.OrdinalIgnoreCase);

    [BindProperty] public string MalEpost { get; set; } = "";
    public string Meddelande { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("user")))
            return RedirectToPage("/Index");
        if (!ÄrAdmin(Epost))
            return RedirectToPage("/Events");

        Konton = await _api.GetKontoOversikt();
        return Page();
    }

    public async Task<IActionResult> OnPostStatusAsync(string status)
    {
        if (!await GateAsync()) return RedirectToPage("/Index");
        if (ÄrAdmin(MalEpost))
            Meddelande = "Administratörskonton kan inte inaktiveras.";
        else
        {
            var nyStatus = status == "inaktiv" ? "inaktiv" : "aktiv";
            await _api.SetUserStatus(MalEpost, nyStatus);
            Meddelande = $"{MalEpost} är nu {nyStatus}.";
        }
        Konton = await _api.GetKontoOversikt();
        return Page();
    }

    public async Task<IActionResult> OnPostRaderaAsync()
    {
        if (!await GateAsync()) return RedirectToPage("/Index");
        if (ÄrAdmin(MalEpost))
            Meddelande = "Administratörskonton kan inte raderas.";
        else
        {
            await _api.DeleteUserCascade(MalEpost);
            Meddelande = $"{MalEpost} och kontots data har raderats.";
        }
        Konton = await _api.GetKontoOversikt();
        return Page();
    }

    // Returnerar false om aktuell session inte är en inloggad admin.
    private async Task<bool> GateAsync()
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("user")) || !ÄrAdmin(Epost))
            return false;
        await Task.CompletedTask;
        return true;
    }
}
