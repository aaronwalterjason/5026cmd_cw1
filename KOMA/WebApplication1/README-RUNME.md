# KOMA — how to run this

This is a real, runnable ASP.NET Core Razor Pages project (not just a folder of files to
merge in) — Identity/RBAC, a SQL Server DbContext, a shared Bootstrap layout, and the
Administrator module fully wired end to end. Staff and Customer pages aren't built yet —
your teammates add `Pages/Staff/` and `Pages/Customer/` the same way, and their entities
onto the same `ApplicationDbContext`.

## Prerequisites

- Visual Studio 2026 with the **ASP.NET and web development** workload (or the .NET SDK + any editor)
- SQL Server (LocalDB, which ships with Visual Studio, or a full instance via SQL Server Management Studio)
- Check your installed SDK version: open a terminal and run `dotnet --version`. This project
  targets **net10.0** to match this machine's installed .NET SDK. If your SDK is a different
  major version, open `Koma.csproj` and change `TargetFramework` and the five package
  versions to match (e.g. `net9.0` / `9.0.x`, or `net8.0` / `8.0.x`).

## First run

1. Open `KOMA.slnx` in Visual Studio (or run `dotnet restore` from the `WebApplication1` folder).
2. Confirm the connection string in `appsettings.json`. The default targets LocalDB and needs
   no setup. To use a full SQL Server instance via SSMS instead, replace it with something like:
   ```
   Server=YOUR_SERVER\SQLEXPRESS;Database=KOMA;Trusted_Connection=True;TrustServerCertificate=True
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
   | Administrator | `admin@koma.local` | `Admin#12345` |
   | Staff | `staff@koma.local` | `Staff#12345` |
   | Customer | `customer@koma.local` | `Customer#12345` |

   **Change or remove these before you submit** — see `Data/SeedData.cs`.
5. Log in as `admin@koma.local` and open **Admin** in the nav bar. Dining Areas, Tables, Menu
   Categories, and Menu Items are full working CRUD — try creating a dining area, then a table
   inside it, then a category and an item.

## What's actually wired up vs. what's still a stub

| Page | Status |
|---|---|
| Dining Areas / Tables / Menu Categories / Menu Items | **Working** — full CRUD against the real database |
| Settings (hours + announcement) | **Working** — persists to a `RestaurantSettings` row |
| Accounts & Roles | **Working** — lists real Identity users, role changes save |
| Dashboard | **Working** — live counts from the database |
| Reports | **Shell only** — filter form works, but the query is blocked on Reservations/Orders tables. Wire it in once those `DbSet<>`s exist on `ApplicationDbContext` (see the TODO in `Pages/Admin/Reports/Index.cshtml.cs`) |
| Analytics dashboard, CSV/PDF/Excel export | **Not started** — Advanced tier, Week 9 |
| Promotions (discounts linked to menu items) | Model exists (`Models/Promotion.cs`, `DbSet<Promotion>`); no CRUD pages yet — Advanced tier |

## How Staff and Customer plug in

- Add `Pages/Staff/` and `Pages/Customer/` the same way `Pages/Admin/` is
  structured. Each role folder can have its own `_ViewImports.cshtml` and `_ViewStart.cshtml` (copy the
  ones under `Pages/Admin/`, just change the namespace).
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

Full context — role split, WBS, Gantt, marking-criteria checklist — is in the team's **KOMA
Project Plan** and **Engineer A Playbook** documents.

## Shared model organisation

Restaurant entities live directly in Models with the KOMA.Models namespace. Administrator page handlers stay under Pages/Admin/Pages, and database setup stays under Data. The solution and project filenames use KOMA; the code namespace remains KOMA.

## Existing local data

The code namespace, assembly and page branding use KOMA. The existing database connection and secret-store ID are retained so existing local data remains accessible. Existing account passwords are unchanged. Startup creates the new @koma.local demo accounts if missing; older accounts remain available with their existing credentials. No database reset is needed for this rename.

## Page folder structure

Administrator screens now live under Pages/Admin. The dashboard is Pages/Admin/Dashboard.cshtml at /Admin/Dashboard. Use asp-page with /Admin/... and an empty asp-area for administrator navigation. Customer and staff folders are Pages/Customer and Pages/Staff. Keep the built-in Identity area references for login, registration and account management. Role authorization attributes remain on administrator PageModels.

## Account profiles

ApplicationUser holds the common name, email, phone and Identity password hash. Customer, Staff and Administrator are separate profile models linked by UserId, matching the proposed EERD. Their tables are Customers, Staffs and Administrators. Role-specific fields can be added later without duplicating login details.

Services/AccountService creates accounts and changes roles in a database transaction. Use it for future account-writing features. It keeps one matching profile and one role through these application operations; foreign keys alone do not enforce that rule against direct database edits. Role changes currently replace the old profile. Revisit role changes before adding bookings or other records linked to profiles, so historical records cannot be lost.

On the next application start, AddAccountProfiles creates the three tables. Startup fills matching profiles for existing accounts and assigns Customer to older accounts with no role. Existing passwords remain unchanged. An existing account with multiple or unsupported roles stops startup with an explicit error instead of silently choosing a role.

Administrator, staff and customer screens remain under Pages. Areas/Identity contains only the override of the framework registration page, which creates a Customer profile and Customer role. Other Identity pages still come from the Identity package. Public registration does not offer staff or administrator roles.

The temporary-database checks can be run from the SE workspace with:

    dotnet run --project Project/KOMA.AccountChecks/KOMA.AccountChecks.csproj

These checks create a uniquely named KOMA_AccountChecks database, verify migration and account behaviour, then remove that test database. They do not use KOMADb.
