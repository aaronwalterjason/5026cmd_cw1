using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Models.Admin;

namespace RROMS.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Engineer A's Admin module entities.
        public DbSet<DiningArea> DiningAreas => Set<DiningArea>();
        public DbSet<RestaurantTable> RestaurantTables => Set<RestaurantTable>();
        public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
        public DbSet<MenuItem> MenuItems => Set<MenuItem>();
        public DbSet<Promotion> Promotions => Set<Promotion>();
        public DbSet<RestaurantSettings> RestaurantSettings => Set<RestaurantSettings>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

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
