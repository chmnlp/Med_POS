using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MargPos.Data;

public class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public const string Schema = "pos";

    public DbSet<User> Users => Set<User>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<User>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(50).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(100).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
            e.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
        });

        b.Entity<Item>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Barcode).HasMaxLength(50);
            e.Property(x => x.Manufacturer).HasMaxLength(100);
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.Pack).HasMaxLength(30);
            e.Property(x => x.Unit).HasMaxLength(20).IsRequired();
            e.Property(x => x.HsnCode).HasMaxLength(10);
            e.Property(x => x.GstRate).HasPrecision(5, 2);
            Money(e.Property(x => x.Mrp));
            Money(e.Property(x => x.SaleRate));
            Money(e.Property(x => x.WholesaleRate));
            Money(e.Property(x => x.PurchaseRate));
            Qty(e.Property(x => x.ReorderLevel));
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
            e.HasIndex(x => x.Name);
        });

        b.Entity<Batch>(e =>
        {
            e.Property(x => x.BatchNo).HasMaxLength(30).IsRequired();
            Money(e.Property(x => x.Mrp));
            Money(e.Property(x => x.PurchaseRate));
            Money(e.Property(x => x.SaleRate));
            Qty(e.Property(x => x.QtyOnHand));
            e.HasOne(x => x.Item).WithMany(x => x.Batches).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ItemId, x.BatchNo }).IsUnique();
            e.HasIndex(x => x.ExpiryDate);
        });

        b.Entity<Customer>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(100);
            e.Property(x => x.Gstin).HasMaxLength(15);
            e.Property(x => x.Address).HasMaxLength(500);
            Money(e.Property(x => x.CreditLimit));
            Money(e.Property(x => x.OpeningBalance));
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Phone);
            e.HasIndex(x => x.Name);
        });

        b.Entity<Supplier>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(100);
            e.Property(x => x.Gstin).HasMaxLength(15);
            e.Property(x => x.DrugLicenseNo).HasMaxLength(50);
            e.Property(x => x.Address).HasMaxLength(500);
            Money(e.Property(x => x.OpeningBalance));
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Name);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampUpdated();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        StampUpdated();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private void StampUpdated()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
            if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAt = now;
        }
    }

    private static void Money(PropertyBuilder<decimal> p) => p.HasPrecision(18, 2);
    private static void Qty(PropertyBuilder<decimal> p) => p.HasPrecision(18, 3);
}
