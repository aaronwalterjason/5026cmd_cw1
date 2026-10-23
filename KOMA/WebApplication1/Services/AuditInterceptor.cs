using System.Security.Claims;
using System.Text.Json;
using KOMA.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KOMA.Services;

// Writes an Activity log entry whenever an ADMIN or STAFF member saves a change to the menu, bookings,
// floor layouts or settings. Customers' own actions and startup seeding (no signed-in user) are not logged.
// How: just before saving, remember the old values of every changed row; just after saving, turn them into
// ActivityLog rows (one per area) and save those too.
public class AuditInterceptor(IHttpContextAccessor http) : SaveChangesInterceptor
{
    // Set by the Activity page while reverting, so the new entry is labelled "Reverted" and linked to the original.
    public const string RevertOfKey = "audit.revertOf";
    public const string CreatedKey = "audit.created";

    private sealed record Pending(EntityEntry Entry, string State, Dictionary<string, object?>? Before, List<string> Changed);
    private List<Pending>? _pending;
    private bool _writing;

    public static string? AreaOf(Type t) => t.Name switch
    {
        nameof(MenuItem) or nameof(MenuCategory) => ActivityLog.Areas.Menu,
        nameof(Reservation) => ActivityLog.Areas.Bookings,
        nameof(FloorLayout) or nameof(DiningArea) or nameof(RestaurantTable) => ActivityLog.Areas.Floors,
        nameof(RestaurantSettings) or nameof(LandingPageContent) => ActivityLog.Areas.Settings,
        _ => null
    };

    public static (string? Id, string Role)? StaffOrAdmin(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;
        if (user.IsInRole("Administrator")) return (user.FindFirstValue(ClaimTypes.NameIdentifier), "Admin");
        if (user.IsInRole("Staff")) return (user.FindFirstValue(ClaimTypes.NameIdentifier), "Staff");
        return null;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData e, InterceptionResult<int> result)
    { Capture(e.Context); return base.SavingChanges(e, result); }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
    { Capture(e.Context); return base.SavingChangesAsync(e, result, ct); }

    public override int SavedChanges(SaveChangesCompletedEventData e, int result)
    { WriteAsync(e.Context, default).GetAwaiter().GetResult(); return base.SavedChanges(e, result); }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e, int result, CancellationToken ct = default)
    { await WriteAsync(e.Context, ct); return await base.SavedChangesAsync(e, result, ct); }

    // ---- before saving: remember what is about to change ----
    private void Capture(DbContext? ctx)
    {
        _pending = null;
        if (_writing || ctx is null || StaffOrAdmin(http.HttpContext?.User) is null) return;
        var list = new List<Pending>();
        foreach (var e in ctx.ChangeTracker.Entries())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (AreaOf(e.Metadata.ClrType) is null) continue;
            var props = e.Properties.Where(p => !ActivityFormat.Hidden.Contains(p.Metadata.Name) || p.Metadata.IsPrimaryKey()).ToList();
            if (e.State == EntityState.Modified)
            {
                // Pages that call Update() mark every field modified without knowing the old values - read them from the database.
                var original = props.All(p => p.IsModified) && props.All(p => Equals(p.OriginalValue, p.CurrentValue)) ? e.GetDatabaseValues() : null;
                object? Old(PropertyEntry p) => original is null ? p.OriginalValue : original[p.Metadata];
                var changed = props.Where(p => !p.Metadata.IsPrimaryKey() && !ActivityFormat.Hidden.Contains(p.Metadata.Name) && !Equals(Old(p), p.CurrentValue))
                                   .Select(p => p.Metadata.Name).ToList();
                if (changed.Count == 0) continue;
                list.Add(new(e, "Modified", props.ToDictionary(p => p.Metadata.Name, Old), changed));
            }
            else if (e.State == EntityState.Deleted)
                list.Add(new(e, "Deleted", props.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue), new()));
            else
                list.Add(new(e, "Added", null, new()));
        }
        if (list.Count > 0) _pending = list;
    }

    // ---- after saving: write the log entries ----
    private async Task WriteAsync(DbContext? ctx, CancellationToken ct)
    {
        var pending = _pending; _pending = null;
        var context = http.HttpContext;
        if (pending is null || ctx is null || context is null) return;
        var who = StaffOrAdmin(context.User)!.Value;
        var appUser = who.Id is null ? null : await ctx.Set<ApplicationUser>().FindAsync([who.Id], ct);
        var revertOf = context.Items[RevertOfKey] as int?;

        var logs = new List<ActivityLog>();
        foreach (var group in pending.GroupBy(p => AreaOf(p.Entry.Metadata.ClrType)!))
        {
            var snaps = new List<ActivitySnapshot>();
            foreach (var p in group)
            {
                var props = p.Entry.Properties.Where(x => !ActivityFormat.Hidden.Contains(x.Metadata.Name) || x.Metadata.IsPrimaryKey());
                var after = p.State == "Deleted" ? null : props.ToDictionary(x => x.Metadata.Name, x => x.CurrentValue);
                var values = after ?? p.Before!;
                snaps.Add(new ActivitySnapshot
                {
                    Type = p.Entry.Metadata.Name,
                    State = p.State,
                    Label = Label(p.Entry.Metadata.ClrType, values),
                    Key = Json(p.Entry.Metadata.FindPrimaryKey()!.Properties.ToDictionary(k => k.Name, k => values[k.Name])),
                    Before = p.Before is null ? null : Json(p.Before),
                    After = after is null ? null : Json(after),
                    Changed = p.Changed
                });
            }
            var (title, action, summary) = await DescribeAsync(ctx, group.Key, group.ToList(), snaps, ct);
            logs.Add(new ActivityLog
            {
                At = DateTime.Now, UserId = who.Id, UserRole = who.Role,
                UserName = Short(appUser?.FullName ?? appUser?.Email ?? context.User.Identity?.Name ?? "Unknown", 100),
                Area = group.Key, Action = revertOf.HasValue ? "Reverted" : action,
                Title = Short(title, 150), Summary = summary is null ? null : Short(summary, 300),
                SnapshotsJson = JsonSerializer.Serialize(snaps), RevertOfId = revertOf
            });
        }

        _writing = true;
        try
        {
            ctx.Set<ActivityLog>().AddRange(logs);
            await ctx.SaveChangesAsync(ct);
            context.Items[CreatedKey] = logs.Select(l => l.Id).ToList();
        }
        finally { _writing = false; }
    }

    // ---- wording ----
    private static string Label(Type t, Dictionary<string, object?> v) => t.Name switch
    {
        nameof(MenuItem) => $"{v["Name"]}",
        nameof(MenuCategory) => $"Category: {v["Name"]}",
        nameof(FloorLayout) => $"{v["Name"]}",
        nameof(DiningArea) => $"{v["Name"]}",
        nameof(RestaurantTable) => $"Table {v["TableNumber"]}",
        nameof(Reservation) => $"R-{v["Id"]:D5} · {v["CustomerName"]}",
        nameof(RestaurantSettings) => "Opening hours & announcement",
        nameof(LandingPageContent) => "Landing page",
        _ => t.Name
    };

    private static async Task<(string Title, string Action, string? Summary)> DescribeAsync(
        DbContext ctx, string area, List<Pending> items, List<ActivitySnapshot> snaps, CancellationToken ct)
    {
        if (items.Count == 1)
        {
            var p = items[0]; var s = snaps[0]; var type = p.Entry.Metadata.ClrType;
            if (p.State == "Added") return (s.Label, "Created", null);
            if (p.State == "Deleted") return (s.Label, "Deleted", null);
            string Change(string f) => $"{ActivityFormat.Value(f, p.Before![f])} → {ActivityFormat.Value(f, p.Entry.Property(f).CurrentValue)}";
            var c = p.Changed;
            if (c.SequenceEqual(new[] { "IsAvailable" }))
                return (s.Label, (bool)p.Entry.Property("IsAvailable").CurrentValue! ? "Available again" : "Sold out", null);
            if (c.SequenceEqual(new[] { "Status" }))
                return (s.Label, ActivityFormat.Value("Status", p.Entry.Property("Status").CurrentValue), Change("Status"));
            if (c.SequenceEqual(new[] { "Price" })) return (s.Label, "Price changed", Change("Price"));
            if (type == typeof(RestaurantSettings) && c.All(f => f.EndsWith("Time"))) return (s.Label, "Hours changed", string.Join(" · ", c.Select(Change)));
            return (s.Label, "Edited", string.Join(" · ", c.Take(3).Select(f => $"{ActivityFormat.Label(type, f)}: {Change(f)}")));
        }

        // Several rows in one save (e.g. the floor designer): name it after the floor and count what happened.
        var title = snaps.FirstOrDefault(s => s.Type.EndsWith(nameof(FloorLayout)))?.Label;
        if (title is null && area == ActivityLog.Areas.Floors)
        {
            var areaId = items.Select(i => i.Entry.Entity).OfType<RestaurantTable>().Select(t => (int?)t.DiningAreaId).FirstOrDefault()
                         ?? items.Select(i => i.Entry.Entity).OfType<DiningArea>().Select(a => (int?)a.Id).FirstOrDefault();
            var dining = areaId is null ? null : await ctx.Set<DiningArea>().FindAsync([areaId.Value], ct);
            var floor = dining?.FloorLayoutId is int fid ? await ctx.Set<FloorLayout>().FindAsync([fid], ct) : null;
            title = floor?.Name;
        }
        var moved = items.Count(i => i.State == "Modified" && i.Changed.All(f => f.StartsWith("Layout")));
        var edited = items.Count(i => i.State == "Modified") - moved;
        var parts = new List<string>();
        if (moved > 0) parts.Add($"{moved} moved");
        if (edited > 0) parts.Add($"{edited} edited");
        if (items.Count(i => i.State == "Added") is var a and > 0) parts.Add($"{a} added");
        if (items.Count(i => i.State == "Deleted") is var d and > 0) parts.Add($"{d} removed");
        return (title ?? snaps[0].Label, "Edited", string.Join(" · ", parts));
    }

    private static Dictionary<string, JsonElement> Json(Dictionary<string, object?> values) =>
        values.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));

    private static string Short(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
