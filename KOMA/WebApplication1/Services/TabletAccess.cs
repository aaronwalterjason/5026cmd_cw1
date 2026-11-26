using System.Security.Cryptography;
using KOMA.Data;
using KOMA.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
namespace KOMA.Services;

public sealed class TabletAccess(ApplicationDbContext db, IMemoryCache cache, IDataProtectionProvider keys, DiningOrderService dining)
{
    private readonly IDataProtector protector = keys.CreateProtector("KOMA.Dining.Device.v2");
    private const string DeviceCookie = "Koma.Tablet.Device";
    private const string VisitCookie = "Koma.Tablet.Visit";
    private const string PauseCookie = "Koma.Tablet.Pause";
    private readonly IDataProtector pauseProtector = keys.CreateProtector("KOMA.Dining.Pause.v1");
    private sealed record PausedVisit(Guid Device, int Visit, int Table, string? Customer, DateTime Expires);
    private Grant? ReadPaused(HttpContext http, Guid device)
    {
        if (!http.Request.Cookies.TryGetValue(PauseCookie, out var value)) return null;
        try
        {
            var paused = System.Text.Json.JsonSerializer.Deserialize<PausedVisit>(pauseProtector.Unprotect(value));
            return paused != null && paused.Device == device && paused.Expires > DateTime.UtcNow
                ? new Grant(paused.Visit, paused.Table, paused.Customer) : null;
        }
        catch (Exception error) when (error is CryptographicException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
    private static readonly object Gate = new();
    private sealed record Grant(int Visit, int Table, string? Customer);
    private static CookieOptions Options(HttpContext http) => new() { HttpOnly = true, Secure = http.Request.IsHttps, SameSite = SameSiteMode.Lax, Path = "/" };
    private Guid? DeviceId(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(DeviceCookie, out var value)) return null;
        try { return Guid.Parse(protector.Unprotect(value)); } catch (CryptographicException) { return null; }
    }
    public async Task<int?> TableAsync(HttpContext http)
    {
        var id = DeviceId(http);
        if (!id.HasValue) return null;
        return await db.RestaurantTables.Where(t => t.TabletDeviceId == id && t.IsActive && t.DiningArea!.IsActive)
            .Select(t => (int?)t.Id).SingleOrDefaultAsync();
    }
    public async Task<bool> PairAsync(HttpContext http, int tableId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var table = await db.RestaurantTables.SingleOrDefaultAsync(t => t.Id == tableId && t.IsActive && t.DiningArea!.IsActive);
        if (table == null) return false;
        var old = DeviceId(http);
        if (old.HasValue) await db.RestaurantTables.Where(t => t.Id != tableId && t.TabletDeviceId == old)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.TabletDeviceId, (Guid?)null));
        Grant? resume = null;
        var active = dining.ActiveVisits().Where(s => s.RestaurantTableId == tableId);
        if (old.HasValue && old == table.TabletDeviceId)
        {
            var paused = ReadPaused(http, old.Value);
            if (paused?.Table == tableId && await active.AnyAsync(s => s.Id == paused.Visit))
                resume = paused;
        }
        if (resume == null)
        {
            // Staff can recover an anonymous bill left behind by an earlier tablet.
            var guest = await active.Where(s => s.ReservationId == null && s.CustomerName == "Guest" &&
                db.Orders.Any(o => o.TableServiceSessionId == s.Id) &&
                !db.Orders.Any(o => o.TableServiceSessionId == s.Id &&
                    (o.CustomerId != null || o.OrderType != OrderType.WalkIn)))
                .Select(s => s.Id).Take(2).ToListAsync();
            if (guest.Count == 1) resume = new Grant(guest[0], tableId, null);
        }
        var device = Guid.NewGuid();
        table.TabletDeviceId = device;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        var options = Options(http); options.MaxAge = TimeSpan.FromDays(365);
        http.Response.Cookies.Append(DeviceCookie, protector.Protect(device.ToString()), options);
        End(http);
        http.Response.Cookies.Delete(PauseCookie, Options(http));
        if (resume != null) Open(http, tableId, resume.Visit, resume.Customer);
        return true;
    }
    private const string StaffExitPin = "123456";

    public string? CheckExitPin(int? tableId, string? pin)
    {
        var key = "tablet-exit-attempts:" + tableId;
        lock (Gate)
        {
            var attempts = cache.Get<int>(key);
            if (attempts >= 5) return "Too many attempts. Try again in one minute.";
            if (pin == StaffExitPin)
            {
                cache.Remove(key);
                return null;
            }
            cache.Set(key, attempts + 1, TimeSpan.FromMinutes(1));
            return "Incorrect staff PIN.";
        }
    }

    public string Issue(string customer)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        cache.Set("account-qr:" + code, customer, TimeSpan.FromMinutes(5));
        return code;
    }
    public string? Redeem(string? code)
    {
        if (code?.Length != 48) return null;
        lock (Gate) { var key = "account-qr:" + code; var customer = cache.Get<string>(key); cache.Remove(key); return customer; }
    }
    public void Open(HttpContext http, int table, int visit, string? customer)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set("tablet-visit:" + token, new Grant(visit, table, customer), TimeSpan.FromHours(6));
        http.Response.Cookies.Append(VisitCookie, token, Options(http));
    }
    public sealed record ReservationChoice(int Table, string Customer, int Booking);
    public string OfferReservation(int table, string customer, int booking)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set("tablet-reservation:" + token, new ReservationChoice(table, customer, booking), TimeSpan.FromMinutes(5));
        return token;
    }
    public ReservationChoice? ReadReservation(string? token, int table)
    {
        if (token?.Length != 64) return null;
        var choice = cache.Get<ReservationChoice>("tablet-reservation:" + token);
        return choice?.Table == table ? choice : null;
    }
    public void ClearReservation(string token) => cache.Remove("tablet-reservation:" + token);
    public async Task<(int Visit, string? Customer)?> VisitAsync(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(VisitCookie, out var token)) return null;
        var grant = cache.Get<Grant>("tablet-visit:" + token);
        if (grant == null || grant.Table != await TableAsync(http)) return null;
        if (!await db.TableServiceSessions.AnyAsync(s => !s.IsReleased && s.Id == grant.Visit && s.RestaurantTableId == grant.Table)) return null;
        return (grant.Visit, grant.Customer);
    }
    public async Task PauseAsync(HttpContext http)
    {
        var visit = await VisitAsync(http);
        var device = DeviceId(http);
        var table = await TableAsync(http);
        if (visit.HasValue && device.HasValue && table.HasValue)
        {
            var paused = new PausedVisit(device.Value, visit.Value.Visit, table.Value, visit.Value.Customer, DateTime.UtcNow.AddHours(6));
            var options = Options(http);
            options.MaxAge = TimeSpan.FromHours(6);
            http.Response.Cookies.Append(PauseCookie, pauseProtector.Protect(System.Text.Json.JsonSerializer.Serialize(paused)), options);
        }
        End(http);
    }

    public void End(HttpContext http)
    {
        if (http.Request.Cookies.TryGetValue(VisitCookie, out var token)) cache.Remove("tablet-visit:" + token);
        http.Response.Cookies.Delete(VisitCookie, Options(http));
    }
}
