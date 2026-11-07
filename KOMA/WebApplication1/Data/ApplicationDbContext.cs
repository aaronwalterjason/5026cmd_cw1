using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using KOMA.Models;

namespace KOMA.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Shared restaurant entities used by administrator, staff and customer workflows.
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Staff> Staffs => Set<Staff>();
        public DbSet<Administrator> Administrators => Set<Administrator>();
        public DbSet<FloorLayout> FloorLayouts => Set<FloorLayout>();
        public DbSet<DiningArea> DiningAreas => Set<DiningArea>();
        public DbSet<RestaurantTable> RestaurantTables => Set<RestaurantTable>();
        public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
        public DbSet<MenuItem> MenuItems => Set<MenuItem>();
        public DbSet<Promotion> Promotions => Set<Promotion>();
        public DbSet<RestaurantSettings> RestaurantSettings => Set<RestaurantSettings>();
        public DbSet<Reservation> Reservations => Set<Reservation>();

        // Engineer B: customer orders and their line items.
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<TableServiceSession> TableServiceSessions
            => Set<TableServiceSession>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<HelpRequest> HelpRequests => Set<HelpRequest>();   // table tablet "Call staff" etc.
        public DbSet<Shift> Shifts => Set<Shift>();                     // staff schedule: shift list (edited in Settings)
        public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
        public DbSet<ScheduleWeek> ScheduleWeeks => Set<ScheduleWeek>();
        public DbSet<BookingEmail> BookingEmails => Set<BookingEmail>();               // booking email outbox (Services/Email)

        // Engineer B: editable text/photo of the customer landing page (one row).
        public DbSet<LandingPageContent> LandingPageContents => Set<LandingPageContent>();

        // Engineer B: Admin > Activity - changes made by admins/staff and their sign-ins.
        public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Each visit belongs to one restaurant table.
            builder.Entity<TableServiceSession>()
                .HasOne(s => s.RestaurantTable)
                .WithMany()
                .HasForeignKey(s => s.RestaurantTableId)
                .OnDelete(DeleteBehavior.Restrict);

            // A visit may belong to a reservation, or be a walk-in.
            builder.Entity<TableServiceSession>()
                .HasOne(s => s.Reservation)
                .WithMany()
                .HasForeignKey(s => s.ReservationId)
                .OnDelete(DeleteBehavior.Restrict);

            // A reservation can have only one linked visit.
            // Multiple walk-in visits can have a null ReservationId.
            builder.Entity<TableServiceSession>()
                .HasIndex(s => s.ReservationId)
                .IsUnique()
                .HasFilter("[ReservationId] IS NOT NULL");

            // Supports looking up visits by table and time.
            builder.Entity<TableServiceSession>()
                .HasIndex(s => new
                {
                    s.RestaurantTableId,
                    s.StartTime,
                    s.EndTime
                });

            // Several food orders can belong to the same visit.
            builder.Entity<Order>()
                .HasOne(o => o.TableServiceSession)
                .WithMany()
                .HasForeignKey(o => o.TableServiceSessionId)
                .OnDelete(DeleteBehavior.Restrict);

            // Reservation table relationships.
            builder.Entity<Reservation>()
                .HasOne(r => r.RestaurantTable)
                .WithMany()
                .HasForeignKey(r => r.RestaurantTableId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Reservation>()
                .HasOne(r => r.CustomerUser)
                .WithMany()
                .HasForeignKey(r => r.CustomerUserId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Reservation>()
                .HasIndex(r => new
                {
                    r.RestaurantTableId,
                    r.StartTime,
                    r.EndTime
                });

            // Keep Identity key lengths consistent in runtime and design-time tooling.
            builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>()
                .Property(p => p.LoginProvider).HasMaxLength(128);
            builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>()
                .Property(p => p.ProviderKey).HasMaxLength(128);
            builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>()
                .Property(p => p.LoginProvider).HasMaxLength(128);
            builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>()
                .Property(p => p.Name).HasMaxLength(128);

            builder.Entity<Customer>().HasOne(p => p.User).WithOne(u => u.Customer)
                .HasForeignKey<Customer>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<Staff>().HasOne(p => p.User).WithOne(u => u.Staff)
                .HasForeignKey<Staff>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<Administrator>().HasOne(p => p.User).WithOne(u => u.Administrator)
                .HasForeignKey<Administrator>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);

            // Keep decimal precision explicit for SQL Server (avoids the EF Core silent-truncation warning).
            builder.Entity<MenuItem>()
                .Property(m => m.Price)
                .HasPrecision(8, 2);

            builder.Entity<Promotion>()
                .Property(p => p.DiscountPercent)
                .HasPrecision(5, 2);

            // Deleting a floor layout deletes the dining areas drawn on it (and, through them, their tables).
            builder.Entity<DiningArea>()
                .HasOne(a => a.FloorLayout).WithMany(f => f.DiningAreas)
                .HasForeignKey(a => a.FloorLayoutId)
                .OnDelete(DeleteBehavior.Cascade);

            // Orders keep their history even if the customer account or table is removed later.
            builder.Entity<Order>().HasOne(o => o.Reservation).WithMany()
                .HasForeignKey(o => o.ReservationId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Order>().HasIndex(o => o.ReservationId).IsUnique()
                .HasFilter("[ReservationId] IS NOT NULL");
            builder.Entity<Order>()
                .HasOne(o => o.Customer).WithMany()
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
            builder.Entity<Order>()
                .HasOne(o => o.RestaurantTable).WithMany()
                .HasForeignKey(o => o.RestaurantTableId)
                .OnDelete(DeleteBehavior.SetNull);
            builder.Entity<Order>().HasIndex(o => o.OrderedAt);
            builder.Entity<Order>().HasIndex(o => o.PaidAt);
            builder.Entity<HelpRequest>().HasIndex(h => h.DoneAt);
            builder.Entity<HelpRequest>()
                .HasOne(h => h.RestaurantTable).WithMany()
                .HasForeignKey(h => h.RestaurantTableId)
                .OnDelete(DeleteBehavior.SetNull);

            // Booking emails: the worker reads waiting rows by time; a reminder is never queued twice.
            builder.Entity<BookingEmail>().HasIndex(e => new { e.Status, e.SendAfter });
            builder.Entity<BookingEmail>().HasIndex(e => e.DedupKey).IsUnique().HasFilter("[DedupKey] IS NOT NULL");
            builder.Entity<BookingEmail>()
                .HasOne(e => e.Reservation).WithMany()
                .HasForeignKey(e => e.ReservationId)
                .OnDelete(DeleteBehavior.SetNull);

            // Staff schedule: one row per person per day (no row = Off). Two starter shifts; Settings will edit them.
            builder.Entity<ShiftAssignment>().HasIndex(a => new { a.UserId, a.Date }).IsUnique();
            builder.Entity<ShiftAssignment>().HasIndex(a => a.Date);
            builder.Entity<ShiftAssignment>()
                .HasOne<ApplicationUser>().WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<ShiftAssignment>()
                .HasOne(a => a.Shift).WithMany()
                .HasForeignKey(a => a.ShiftId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Shift>().HasData(
                new Shift { Id = 1, Name = "Morning", Starts = new TimeOnly(10, 0), Ends = new TimeOnly(16, 0), Needed = 2, SortOrder = 0, IsActive = true },
                new Shift { Id = 2, Name = "Evening", Starts = new TimeOnly(16, 0), Ends = new TimeOnly(23, 0), Needed = 2, SortOrder = 1, IsActive = true });

            // Deleting an order removes its lines; a menu item that appears on past orders can't be deleted.
            builder.Entity<OrderItem>()
                .HasOne(i => i.Order).WithMany(o => o.Items)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            // Activity log is read newest-first, filtered by area and by person.
            builder.Entity<ActivityLog>().HasIndex(l => l.At);
            builder.Entity<ActivityLog>().HasIndex(l => new { l.Area, l.At });
            builder.Entity<ActivityLog>().HasIndex(l => l.UserId);

            // Deleting a dish keeps past orders: the line's link is cleared, its saved ItemName/UnitPrice stay.
            builder.Entity<OrderItem>()
                .HasOne(i => i.MenuItem).WithMany()
                .HasForeignKey(i => i.MenuItemId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
