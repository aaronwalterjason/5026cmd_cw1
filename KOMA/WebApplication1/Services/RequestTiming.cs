using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KOMA.Services;

// Speed check for every page: counts the database queries a request runs and how long they took,
// then (see Program.cs) shows it in two places:
//   - Visual Studio Output / console:  "KOMA timing  GET /Admin/Orders  412 ms  ·  17 queries, 95 ms"
//   - Browser DevTools > Network > (request) > Timing > Server Timing
// Remove the two lines in Program.cs to turn it off.
public sealed class RequestTimingInterceptor(IHttpContextAccessor http) : DbCommandInterceptor
{
    public sealed class Stat { public int Count; public double Ms; }
    private const string Key = "koma.timing";

    public static Stat Of(HttpContext ctx)
    {
        if (ctx.Items[Key] is Stat s) return s;
        s = new Stat();
        ctx.Items[Key] = s;
        return s;
    }

    private void Add(CommandExecutedEventData e)
    {
        var ctx = http.HttpContext;
        if (ctx is null) return;
        var s = Of(ctx);
        lock (s) { s.Count++; s.Ms += e.Duration.TotalMilliseconds; }
    }

    public override DbDataReader ReaderExecuted(DbCommand c, CommandExecutedEventData e, DbDataReader r) { Add(e); return r; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand c, CommandExecutedEventData e, DbDataReader r, CancellationToken t = default) { Add(e); return new(r); }
    public override object? ScalarExecuted(DbCommand c, CommandExecutedEventData e, object? r) { Add(e); return r; }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand c, CommandExecutedEventData e, object? r, CancellationToken t = default) { Add(e); return new(r); }
    public override int NonQueryExecuted(DbCommand c, CommandExecutedEventData e, int r) { Add(e); return r; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand c, CommandExecutedEventData e, int r, CancellationToken t = default) { Add(e); return new(r); }
}
