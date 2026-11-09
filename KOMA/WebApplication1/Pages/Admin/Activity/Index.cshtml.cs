using System.Text.Json;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace KOMA.Pages.Admin.Activity;

// Admin > Activity: changes made by admins and staff (menu, bookings, floors, settings) and their sign-ins.
// Left: filterable list grouped by day. Right: the selected change as a before | after table, with Revert for
// menu and settings changes.
[Authorize(Roles = "Administrator")]
public class IndexModel(ApplicationDbContext _db, UserManager<ApplicationUser> users, AccountService accounts) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }     // all | menu | bookings | floors | settings | signins
    [BindProperty(SupportsGet = true)] public string? Who { get; set; }     // everyone | admins | staff
    [BindProperty(SupportsGet = true)] public int Days { get; set; } = 7;   // 1, 7, 30 or 0 = all time
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty(SupportsGet = true)] public int Take { get; set; } = 60;

    public record TabDef(string Key, string Label, string? Area, string Color);
    public static readonly TabDef[] Tabs =
    {
        new("all", "All", null, "#b1b4ab"),
        new("menu", "Menu", ActivityLog.Areas.Menu, "#974535"),
        new("bookings", "Bookings", ActivityLog.Areas.Bookings, "#7a5a2a"),
        new("floors", "Floors", ActivityLog.Areas.Floors, "#4d6372"),
        new("settings", "Settings", ActivityLog.Areas.Settings, "#8fa1ad"),
        new("signins", "Sign-ins", ActivityLog.Areas.SignIns, "#b1b4ab"),
    };
    public static string ColorOf(string area) => Tabs.FirstOrDefault(t => t.Area == area)?.Color ?? "#b1b4ab";
    public string TabKey => Tabs.Any(t => t.Key == Tab) ? Tab! : "all";
    public string WhoKey => Who is "admins" or "staff" ? Who : "everyone";

    public record Row(string? Item, string Field, string Before, string After);

    public List<ActivityLog> Logs { get; private set; } = new();
    public bool HasMore { get; private set; }
    public ActivityLog? Selected { get; private set; }
    public List<Row> Rows { get; private set; } = new();
    public List<ActivityLog> SignInHistory { get; private set; } = new();
    public int HistoryCount { get; private set; }
    public bool CanRevert { get; private set; }
    public string? RevertNote { get; private set; }

    public async Task OnGetAsync()
    {
        var q = _db.ActivityLogs.AsNoTracking().AsQueryable();
        if (Days > 0) q = q.Where(l => l.At >= KOMA.Services.DemoClock.Shared.MalaysiaNow.Date.AddDays(1 - Days));
        if (WhoKey == "admins") q = q.Where(l => l.UserRole == "Admin");
        if (WhoKey == "staff") q = q.Where(l => l.UserRole == "Staff");
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            q = q.Where(l => l.Title.Contains(term) || l.UserName.Contains(term) || (l.Summary != null && l.Summary.Contains(term)));
        }
        var area = Tabs.First(t => t.Key == TabKey).Area;
        if (area is not null) q = q.Where(l => l.Area == area);

        var page = await q.OrderByDescending(l => l.At).ThenByDescending(l => l.Id).Take(Take + 1).ToListAsync();
        HasMore = page.Count > Take;
        Logs = page.Take(Take).ToList();

        Selected = Id.HasValue ? await _db.ActivityLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == Id.Value) : Logs.FirstOrDefault();
        if (Selected is null) return;

        HistoryCount = await _db.ActivityLogs.CountAsync(l => l.Area == Selected.Area && l.Title == Selected.Title && l.Id != Selected.Id);
        if (Selected.Area == ActivityLog.Areas.SignIns)
            SignInHistory = await _db.ActivityLogs.AsNoTracking()
                .Where(l => l.Area == ActivityLog.Areas.SignIns && l.UserId == Selected.UserId)
                .OrderByDescending(l => l.At).Take(8).ToListAsync();
        else
            await BuildRowsAsync(Selected);
        (CanRevert, RevertNote) = await CheckRevertAsync(Selected);
    }

    // ---------------- Revert ----------------

    public async Task<IActionResult> OnPostRevertAsync(int logId)
    {
        var log = await _db.ActivityLogs.FindAsync(logId);
        if (log is null) return NotFound();
        var (ok, note) = await CheckRevertAsync(log);
        if (!ok)
        {
            TempData["StatusError"] = note ?? "This change can't be reverted.";
            return Back(logId);
        }

        var snaps = log.Snapshots();
        int? newId;
        if (snaps.Count == 1 && snaps[0].Type == "Role")
        {
            var user = await users.FindByIdAsync(snaps[0].Key["UserId"].GetString()!);
            var oldRole = snaps[0].Before!["Role"].GetString()!;
            var current = (await users.GetRolesAsync(user!)).FirstOrDefault() ?? "Customer";
            var result = await accounts.SetRoleAsync(user!, oldRole);
            if (!result.Succeeded)
            {
                TempData["StatusError"] = string.Join(" ", result.Errors.Select(e => e.Description));
                return Back(logId);
            }
            newId = (await Accounts.IndexModel.LogRoleChangeAsync(_db, User, user!, current, oldRole, log.Id)).Id;
        }
        else
        {
            // Order matters when a promotion and its dishes changed together:
            // 1) bring back deleted rows, 2) put fields back, 3) remove rows that were created.
            // A restored row gets a NEW id, so dishes that pointed at the old id are linked to the restored row instead.
            var restoredMap = new Dictionary<(Type, object), object>();
            var order = snaps.Select((s, i) => (s, i))
                .OrderBy(x => x.s.State switch { "Deleted" => 0, "Modified" => 1, _ => 2 })
                .ThenByDescending(x => x.i).Select(x => x.s).ToList();
            foreach (var s in order)
            {
                var et = _db.Model.FindEntityType(s.Type)!;
                var keys = et.FindPrimaryKey()!.Properties.Select(p => FromJson(s.Key[p.Name], p.ClrType)).ToArray();
                var entity = await _db.FindAsync(et.ClrType, keys);
                switch (s.State)
                {
                    case "Modified" when entity is not null:
                        var entry = _db.Entry(entity);
                        foreach (var f in s.Changed)
                        {
                            var old = FromJson(s.Before![f], entry.Property(f).Metadata.ClrType);
                            var fk = entry.Property(f).Metadata.GetContainingForeignKeys().FirstOrDefault();
                            if (old is not null && fk?.DependentToPrincipal is { } nav && restoredMap.TryGetValue((fk.PrincipalEntityType.ClrType, old), out var principal))
                                entry.Navigation(nav.Name).CurrentValue = principal;
                            else
                                entry.Property(f).CurrentValue = old;
                        }
                        break;
                    case "Added" when entity is not null:
                        _db.Remove(entity);
                        break;
                    case "Deleted" when entity is null:
                        var restored = Activator.CreateInstance(et.ClrType)!;
                        _db.Add(restored);
                        var e2 = _db.Entry(restored);
                        foreach (var p in et.GetProperties())
                        {
                            if (p.IsPrimaryKey() && p.ValueGenerated != Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never) continue;
                            if (s.Before!.TryGetValue(p.Name, out var v)) e2.Property(p.Name).CurrentValue = FromJson(v, p.ClrType);
                        }
                        // A link to something that no longer exists (e.g. a deleted promotion) is cleared.
                        foreach (var link in et.GetForeignKeys().Where(l => !l.IsRequired && l.Properties.Count == 1))
                        {
                            var fp = e2.Property(link.Properties[0].Name);
                            if (fp.CurrentValue is { } fkv && !restoredMap.ContainsKey((link.PrincipalEntityType.ClrType, fkv))
                                && await _db.FindAsync(link.PrincipalEntityType.ClrType, fkv) is null)
                                fp.CurrentValue = null;
                        }
                        var oldKey = et.FindPrimaryKey()!.Properties.Count == 1 ? FromJson(s.Key[et.FindPrimaryKey()!.Properties[0].Name], et.FindPrimaryKey()!.Properties[0].ClrType) : null;
                        if (oldKey is not null) restoredMap[(et.ClrType, oldKey)] = restored;
                        break;
                }
            }
            HttpContext.Items[AuditInterceptor.RevertOfKey] = log.Id;
            await _db.SaveChangesAsync();
            newId = (HttpContext.Items[AuditInterceptor.CreatedKey] as List<int>)?.FirstOrDefault();
        }

        log.RevertedById = newId;
        await _db.SaveChangesAsync();
        TempData["StatusMessage"] = $"Reverted the change to {log.Title}.";
        return Back(newId ?? logId);
    }

    // Only menu and settings changes can be reverted, once, by an admin. Deleted floors/areas can't be restored.
    private async Task<(bool Ok, string? Note)> CheckRevertAsync(ActivityLog log)
    {
        if (log.Area is not (ActivityLog.Areas.Menu or ActivityLog.Areas.Settings)) return (false, null);
        if (log.RevertOfId.HasValue) return (false, "This entry is itself a revert.");
        if (log.RevertedById.HasValue) return (false, "Already reverted.");
        var snaps = log.Snapshots();
        if (snaps.Count == 0) return (false, null);

        // Warn if the item has been changed again since (reverting would undo those later edits too).
        foreach (var s in snaps.Where(s => s.State == "Modified" && s.Type != "Role" && s.After is not null))
        {
            var et = _db.Model.FindEntityType(s.Type);
            if (et is null) continue;
            var entity = await _db.FindAsync(et.ClrType, et.FindPrimaryKey()!.Properties.Select(p => FromJson(s.Key[p.Name], p.ClrType)).ToArray());
            if (entity is null) return (false, "The item no longer exists.");
            var entry = _db.Entry(entity);
            if (s.Changed.Any(f => !Equals(entry.Property(f).CurrentValue, FromJson(s.After![f], entry.Property(f).Metadata.ClrType))))
                return (true, "⚠ Changed again since - reverting also undoes the later edits to these fields.");
        }
        return (true, "Safe to revert - no later changes.");
    }

    // ---------------- Before | after rows ----------------

    private async Task BuildRowsAsync(ActivityLog log)
    {
        var snaps = log.Snapshots();
        var many = snaps.Count > 1;
        foreach (var s in snaps)
        {
            if (s.Type == "Role")
            {
                Rows.Add(new(null, "Role", s.Before!["Role"].GetString()!, s.After!["Role"].GetString()!));
                continue;
            }
            var et = _db.Model.FindEntityType(s.Type);
            if (et is null) continue;
            var item = many ? s.Label : null;
            if (s.State == "Added") { Rows.Add(new(item, "—", "(didn't exist)", "Created")); continue; }
            if (s.State == "Deleted") { Rows.Add(new(item ?? s.Label, "—", "Existed", "Deleted")); continue; }

            var layout = s.Changed.Where(f => f.StartsWith("Layout")).ToList();
            if (layout.Count > 0)
            {
                string Pos(Dictionary<string, JsonElement> v) => $"{Num(v, "LayoutX")}, {Num(v, "LayoutY")} · {Num(v, "LayoutWidth")}×{Num(v, "LayoutHeight")} m";
                Rows.Add(new(item, "Position / size", Pos(s.Before!), Pos(s.After!)));
            }
            foreach (var f in s.Changed.Except(layout))
            {
                var p = et.FindProperty(f);
                if (p is null) continue;
                Rows.Add(new(item, ActivityFormat.Label(et.ClrType, f),
                    await ShowAsync(f, FromJson(s.Before![f], p.ClrType)), await ShowAsync(f, FromJson(s.After![f], p.ClrType))));
            }
        }
    }

    private static string Num(Dictionary<string, JsonElement> v, string k) =>
        v.TryGetValue(k, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble().ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "?";

    // Show names instead of ids for links (category, table, area, floor).
    private async Task<string> ShowAsync(string field, object? value)
    {
        if (value is int id)
        {
            switch (field)
            {
                case "MenuCategoryId": return (await _db.MenuCategories.FindAsync(id))?.Name ?? $"#{id}";
                case "RestaurantTableId": return (await _db.RestaurantTables.FindAsync(id)) is { } t ? $"Table {t.TableNumber}" : $"#{id}";
                case "DiningAreaId": return (await _db.DiningAreas.FindAsync(id))?.Name ?? $"#{id}";
                case "FloorLayoutId": return (await _db.FloorLayouts.FindAsync(id))?.Name ?? $"#{id}";
                case "PromotionId": return (await _db.Promotions.FindAsync(id))?.Title ?? "(deleted promotion)";
            }
        }
        return ActivityFormat.Value(field, value);
    }

    private static object? FromJson(JsonElement e, Type t) =>
        e.ValueKind == JsonValueKind.Null ? null : JsonSerializer.Deserialize(e.GetRawText(), t);

    // ---------------- Helpers for the page ----------------

    private IActionResult Back(int? id) => RedirectToPage("./Index", new RouteValueDictionary(Route(id: id)));

    private const string Keep = "\u0001";
    public Dictionary<string, string> Route(string? tab = Keep, string? who = Keep, int? days = null, int? id = null, int? take = null, bool keepSearch = true)
    {
        var r = new Dictionary<string, string>();
        var t = tab == Keep ? Tab : tab; if (!string.IsNullOrEmpty(t) && t != "all") r["tab"] = t;
        var w = who == Keep ? Who : who; if (!string.IsNullOrEmpty(w) && w != "everyone") r["who"] = w;
        var d = days ?? Days; if (d != 7) r["days"] = d.ToString();
        if (keepSearch && !string.IsNullOrWhiteSpace(Q)) r["q"] = Q!;
        if (id.HasValue) r["id"] = id.Value.ToString();
        if (take.HasValue) r["take"] = take.Value.ToString();
        return r;
    }

    public static string DayLabel(DateTime at) =>
        at.Date == KOMA.Services.DemoClock.Shared.MalaysiaNow.Date ? "Today" : at.Date == KOMA.Services.DemoClock.Shared.MalaysiaNow.Date.AddDays(-1) ? "Yesterday" : at.ToString("ddd d MMM");

    public static bool IsWarning(string action) =>
        action is "Deleted" or "Sold out" or "Failed sign-in" or "Locked out" or "Cancelled" or "Role changed";
}
