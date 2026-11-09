using System.Globalization;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KOMA.Pages;

// Available to all demo participants only when the demo feature is enabled.
// Razor Pages validates antiforgery tokens on both POST handlers.
[AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DemoTimeModel : PageModel
{
    public IActionResult OnGet() => DemoClock.Shared.Snapshot().Enabled
        ? new JsonResult(DemoClock.Shared.Snapshot()) : NotFound();

    public IActionResult OnPostSet(string? localTime)
    {
        if (!DemoClock.Shared.Snapshot().Enabled) return NotFound();
        if (!DateTime.TryParseExact(localTime, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var chosen) || !DemoClock.Shared.Set(chosen))
            return BadRequest("Choose a valid Malaysia date and time between 2000 and 2100.");
        return new JsonResult(DemoClock.Shared.Snapshot());
    }
    public IActionResult OnPostReset() => DemoClock.Shared.Reset()
        ? new JsonResult(DemoClock.Shared.Snapshot()) : NotFound();
}
