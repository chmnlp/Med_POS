using Microsoft.EntityFrameworkCore;
using BillKro.Sync.Data.Entities;

namespace BillKro.Sync.Data;

public class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public DbSet<CompanyEntity>          Companies          => Set<CompanyEntity>();
    public DbSet<PartyEntity>            Parties            => Set<PartyEntity>();
    public DbSet<ItemEntity>             Items              => Set<ItemEntity>();
    public DbSet<PurchaseBillEntity>     PurchaseBills      => Set<PurchaseBillEntity>();
    public DbSet<PurchaseBillLineEntity> PurchaseBillLines  => Set<PurchaseBillLineEntity>();
    public DbSet<SalesInvoiceEntity>     SalesInvoices      => Set<SalesInvoiceEntity>();
    public DbSet<SalesInvoiceLineEntity> SalesInvoiceLines  => Set<SalesInvoiceLineEntity>();
    public DbSet<StockEntryEntity>       StockEntries       => Set<StockEntryEntity>();
    public DbSet<LedgerEntryEntity>      LedgerEntries      => Set<LedgerEntryEntity>();
    public DbSet<GstEntryEntity>         GstEntries         => Set<GstEntryEntity>();
    public DbSet<SyncLogEntity>          SyncLogs           => Set<SyncLogEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Composite unique indexes for upsert keys
        b.Entity<PartyEntity>()
            .HasIndex(e => new { e.CompanyCode, e.Code })
            .IsUnique();

        b.Entity<ItemEntity>()
            .HasIndex(e => new { e.CompanyCode, e.Code });

        b.Entity<PurchaseBillEntity>()
            .HasIndex(e => new { e.CompanyCode, e.BillNumber })
            .IsUnique();

        b.Entity<SalesInvoiceEntity>()
            .HasIndex(e => new { e.CompanyCode, e.InvoiceNumber })
            .IsUnique();

        b.Entity<StockEntryEntity>()
            .HasIndex(e => new { e.CompanyCode, e.ItemCode, e.BatchNo, e.AsOfDate });

        b.Entity<LedgerEntryEntity>()
            .HasIndex(e => new { e.CompanyCode, e.VoucherNumber, e.EntryDate });

        b.Entity<GstEntryEntity>()
            .HasIndex(e => new { e.CompanyCode, e.InvoiceNumber })
            .IsUnique();

        // FK relationships
        b.Entity<PurchaseBillLineEntity>()
            .HasOne(l => l.Bill)
            .WithMany(b => b.Lines)
            .HasForeignKey(l => l.PurchaseBillId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<SalesInvoiceLineEntity>()
            .HasOne(l => l.Invoice)
            .WithMany(i => i.Lines)
            .HasForeignKey(l => l.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
