using AutoPartsWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsWeb.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Vehicle> Vehicles => Set<Vehicle>();
        public DbSet<Part> Parts => Set<Part>();
        public DbSet<PartBrand> PartBrands => Set<PartBrand>();
        public DbSet<PartVehicle> PartVehicles => Set<PartVehicle>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems => Set<OrderItem>();
        public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
        public DbSet<PartImage> PartImages => Set<PartImage>();
        public DbSet<AppUser> AppUsers => Set<AppUser>();
        public DbSet<SellerApplication> SellerApplications => Set<SellerApplication>();
        public DbSet<ProductQuestion> ProductQuestions => Set<ProductQuestion>();
        public DbSet<ProductReview> ProductReviews => Set<ProductReview>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<PartVehicle>()
                .HasKey(pv => new { pv.PartId, pv.VehicleId });

            modelBuilder.Entity<PartVehicle>()
                .HasOne(pv => pv.Part)
                .WithMany(p => p.PartVehicles)
                .HasForeignKey(pv => pv.PartId);

            modelBuilder.Entity<PartVehicle>()
                .HasOne(pv => pv.Vehicle)
                .WithMany(v => v.PartVehicles)
                .HasForeignKey(pv => pv.VehicleId);

            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(oi => oi.OrderId);

            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Part)
                .WithMany()
                .HasForeignKey(oi => oi.PartId);

            modelBuilder.Entity<ContactMessage>()
                .Property(c => c.Status)
                .HasDefaultValue("Yeni");

            modelBuilder.Entity<PartImage>()
                .HasOne(pi => pi.Part)
                .WithMany(p => p.PartImages)
                .HasForeignKey(pi => pi.PartId);

            modelBuilder.Entity<SellerApplication>()
                .HasOne(sa => sa.User)
                .WithMany()
                .HasForeignKey(sa => sa.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProductQuestion>()
                .HasOne(q => q.Part)
                .WithMany()
                .HasForeignKey(q => q.PartId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProductQuestion>()
                .HasOne(q => q.User)
                .WithMany()
                .HasForeignKey(q => q.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ProductReview>()
                .HasOne(r => r.Part)
                .WithMany()
                .HasForeignKey(r => r.PartId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProductReview>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
