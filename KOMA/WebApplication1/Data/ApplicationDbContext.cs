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
        public DbSet<DiningArea> DiningAreas => Set<DiningArea>();
        public DbSet<RestaurantTable> RestaurantTables => Set<RestaurantTable>();
        public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
        public DbSet<MenuItem> MenuItems => Set<MenuItem>();
        public DbSet<Promotion> Promotions => Set<Promotion>();
        public DbSet<RestaurantSettings> RestaurantSettings => Set<RestaurantSettings>();
        public DbSet<Reservation> Reservations => Set<Reservation>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

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
        }
    }
}
