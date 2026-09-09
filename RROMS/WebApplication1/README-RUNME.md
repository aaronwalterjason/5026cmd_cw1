# RROMS — how to run this

This is a real, runnable ASP.NET Core Razor Pages project (not just a folder of files to
merge in) — Identity/RBAC, a SQL Server DbContext, a shared Bootstrap layout, and the
Administrator module fully wired end to end. Staff and Customer areas aren't built yet —
your teammates add `Areas/Staff/` and `Areas/Customer/` the same way, and their entities
onto the same `ApplicationDbContext`.

## Prerequisites

- Visual Studio 2026 with the **ASP.NET and web development** workload (or the .NET SDK + any editor)
- SQL Server (LocalDB, which ships with Visual Studio, or a full instance via SQL Server Management Studio)
- Check your installed SDK version: open a terminal and run `dotnet --version`. This project
  targets **net10.0** to match this machine's installed .NET SDK. If your SDK is a different
  major version, open `WebApplication1.csproj` and change `TargetFramework` and the five package
  versions to match (e.g. `net9.0` / `9.0.x`, or `net8.0` / `8.0.x`).

## First run

1. Open `RROMS.slnx` in Visual Studio (or run `dotnet restore` from the `WebApplication1` folder).
2. Confirm the connection string in `appsettings.json`. The default targets LocalDB and needs
   no setup. To use a full SQL Server instance via SSMS instead, replace it with something like:
   ```
   Server=YOUR_SERVER\SQLEXPRESS;Database=RROMS;Trusted_Connection=True;TrustServerCertificate=True
   ```
3. Create the first migration and database (from a terminal in the `WebApplication1` folder):
   ```
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```
   (If `dotnet ef` isn't found: `dotnet tool install --global dotnet-ef`.)
4. Run the project (F5 in Visual Studio, or `dotnet run`). On first startup the app applies
   any pending migrations and seeds three demo accounts automatically:

   | Role | Email | Password |
   |---|---|---|
   | Administrator | `admin@rroms.local` | `Admin#12345` |
   | Staff | `staff@rroms.local` | `Staff#12345` |
   | Customer | `customer@rroms.local` | `Customer#12345` |

   **Change or remove these before you submit** — see `Data/SeedData.cs`.
5. Log in as `admin@rroms.local` and open **Admin** in the nav bar. Dining Areas, Tables, Menu
   Categories, and Menu Items are full working CRUD — try creating a dining area, then a table
   inside it, then a category and an item.

## What's actually wired up vs. what's still a stub

| Page | Status |
|---|---|
| Dining Areas / Tables / Menu Categories / Menu Items | **Working** — full CRUD against the real database |
| Settings (hours + announcement) | **Working** — persists to a `RestaurantSettings` row |
| Accounts & Roles | **Working** — lists real Identity users, role changes save |
| Dashboard | **Working** — live counts from the database |
| Reports | **Shell only** — filter form works, but the query is blocked on Reservations/Orders tables. Wire it in once those `DbSet<>`s exist on `ApplicationDbContext` (see the TODO in `Areas/Admin/Pages/Reports/Index.cshtml.cs`) |
| Analytics dashboard, CSV/PDF/Excel export | **Not started** — Advanced tier, Week 9 |
| Promotions (discounts linked to menu items) | Model exists (`Models/Admin/Promotion.cs`, `DbSet<Promotion>`); no CRUD pages yet — Advanced tier |

## How Staff and Customer plug in

- Add `Areas/Staff/Pages/` and `Areas/Customer/Pages/` the same way `Areas/Admin/Pages/` is
  structured. Each area gets its own `_ViewImports.cshtml` and `_ViewStart.cshtml` (copy the
  ones under `Areas/Admin/Pages/`, just change the namespace).
- Add a `Staff` / `Customer` link to the nav bar in `Pages/Shared/_Layout.cshtml` next to the
  existing Admin one (follow the `@if (User.IsInRole("Administrator"))` pattern already there),
  and extend the `Context.Request.Path.StartsWithSegments(...)` block if Staff/Customer want
  their own side-nav like Admin has.
- Add their entities (`Reservation`, `Order`, `OrderItem`, `Feedback`, ...) to
  `Data/ApplicationDbContext.cs` as new `DbSet<>` properties, then run
  `dotnet ef migrations add AddReservationsAndOrders` — EF Core tracks everyone's tables in one
  migration history as long as you're all working against the same `ApplicationDbContext`.
- `[Authorize(Roles = "Staff")]` / `[Authorize(Roles = "Customer")]` on their PageModels — the
  roles already exist from `SeedData.cs`.

## A note on merge conflicts

Three people adding `DbSet<>` properties to the same `ApplicationDbContext.cs` and running
migrations independently **will** produce conflicting migration files. Agree on an order (e.g.
whoever's ready first adds their migration, the others rebase and re-generate), or have one
person own running `dotnet ef migrations add` each time, at least until you're past the
foundation weeks.

Full context — role split, WBS, Gantt, marking-criteria checklist — is in the team's **RROMS
Project Plan** and **Engineer A Playbook** documents.
