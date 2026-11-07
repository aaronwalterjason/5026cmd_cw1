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
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
    options.UseSqlServer(connectionString)
           .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
        options.SignIn.RequireConfirmedAccount = false;   // turn on once email sending is wired up
        options.Password.RequireNonAlphanumeric = false;  // relaxed for coursework demo logins
    })
    .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(TokenOptions.DefaultProvider)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager<AuditSignInManager>();   // also logs admin/staff sign-ins to Admin > Activity

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});
builder.Services.AddTransient<IEmailSender, UnconfiguredEmailSender>();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeAreaFolder("Identity", "/Account/Manage");
    options.Conventions.AuthorizeAreaPage("Identity", "/Account/Logout");
});
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddScoped<DiningOrderService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<TabletAccess>();
builder.Services.AddScoped<OrderWorkflowService>();
builder.Services.AddScoped<MenuImageService>();
// Check role changes on the next request, rather than retaining old role claims.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.Zero);

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


