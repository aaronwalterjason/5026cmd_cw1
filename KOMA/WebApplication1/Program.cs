using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using KOMA.Data;
using KOMA.Models;
using KOMA.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found. Check appsettings.json.");

// AuditInterceptor writes an Admin > Activity entry whenever an admin/staff member saves a change.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddSingleton<RequestTimingInterceptor>();   // speed check (see Services/RequestTiming.cs)
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
    options.UseSqlServer(connectionString)
           .AddInterceptors(sp.GetRequiredService<AuditInterceptor>(), sp.GetRequiredService<RequestTimingInterceptor>()));

builder.Services.AddDefaultIdentity<KOMA.Models.ApplicationUser>(options =>
{
        options.SignIn.RequireConfirmedAccount = false;   // turn on once email sending is wired up
        options.Password.RequireNonAlphanumeric = false;  // relaxed for coursework demo logins
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager<AuditSignInManager>();   // also logs admin/staff sign-ins to Admin > Activity

builder.Services.AddRazorPages();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddScoped<TableSessionService>();
builder.Services.AddScoped<OrderWorkflowService>();
builder.Services.AddScoped<MenuImageService>();
// Re-check the signed-in user (role changes, locked accounts) at most once a minute.
// Zero re-checked on EVERY request (4-6 extra queries each time), which slowed every page.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(1));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
// Serve photos uploaded after the application was built.
app.UseStaticFiles();

// Speed check: time every page request and its database queries (Output window + DevTools Server Timing).
app.Use(async (ctx, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    ctx.Response.OnStarting(() =>
    {
        var db = RequestTimingInterceptor.Of(ctx);
        ctx.Response.Headers.Append("Server-Timing",
            $"total;desc=\"server total\";dur={sw.Elapsed.TotalMilliseconds:0}, sql;desc=\"{db.Count} queries\";dur={db.Ms:0}");
        return Task.CompletedTask;
    });
    await next();
    var s = RequestTimingInterceptor.Of(ctx);
    if (!Path.HasExtension(ctx.Request.Path.Value))   // pages and handlers only, not css/js/images
        app.Logger.LogWarning("KOMA timing  {Method} {Path}{Query}  {Ms:0} ms  ·  {Count} queries, {DbMs:0} ms",
            ctx.Request.Method, ctx.Request.Path, ctx.Request.QueryString, sw.Elapsed.TotalMilliseconds, s.Count, s.Ms);
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Apply pending migrations and seed roles/demo accounts on every startup.
using (var scope = app.Services.CreateScope())
{
    await SeedData.InitializeAsync(scope.ServiceProvider);
}

app.Run();

