using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillKro.Data;

public class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public const string Schema = "pos";

    public DbSet<User> Users => Set<User>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SaleInvoice> SaleInvoices => Set<SaleInvoice>();
    public DbSet<SaleInvoiceLine> SaleInvoiceLines => Set<SaleInvoiceLine>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<StoreSettings> StoreSettings => Set<StoreSettings>();
    public DbSet<HeldBill> HeldBills => Set<HeldBill>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<ItemAlias> ItemAliases => Set<ItemAlias>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnLine> PurchaseReturnLines => Set<PurchaseReturnLine>();
    public DbSet<CustomerReceipt> CustomerReceipts => Set<CustomerReceipt>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<DayClose> DayCloses => Set<DayClose>();
    public DbSet<ErpLinkSettings> ErpLinkSettings => Set<ErpLinkSettings>();
    public DbSet<ErpExport> ErpExports => Set<ErpExport>();

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
            Money(e.Property(x => x.HospitalRate));
            Cost(e.Property(x => x.PurchaseRate));
            Percent(e.Property(x => x.MarginPercent));
            Percent(e.Property(x => x.WholesaleMarginPercent));
            Qty(e.Property(x => x.ReorderLevel));
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
            e.HasIndex(x => x.Name);
        });

        b.Entity<Batch>(e =>
        {
            e.Property(x => x.BatchNo).HasMaxLength(30).IsRequired();
            Money(e.Property(x => x.Mrp));
            Cost(e.Property(x => x.PurchaseRate));
            Money(e.Property(x => x.SaleRate));
            Money(e.Property(x => x.WholesaleRate));
            Money(e.Property(x => x.HospitalRate));
            Qty(e.Property(x => x.QtyOnHand));
            e.HasOne(x => x.Item).WithMany(x => x.Batches).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ItemId, x.BatchNo }).IsUnique();
            e.HasIndex(x => x.ExpiryDate);
            e.Property(x => x.Barcode).HasMaxLength(50);
            e.HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
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

        ConfigureSales(b);
        ConfigureStock(b);
        ConfigureAccounts(b);
    }

    private static void ConfigureAccounts(ModelBuilder b)
    {
        b.Entity<CustomerReceipt>(e =>
        {
            e.Property(x => x.ReceiptNo).HasMaxLength(16).IsRequired();
            e.Property(x => x.FinancialYear).HasMaxLength(4).IsRequired();
            e.Property(x => x.Reference).HasMaxLength(50);
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.CancelReason).HasMaxLength(200);
            Money(e.Property(x => x.Amount));
            e.HasIndex(x => x.ReceiptNo).IsUnique();
            e.HasIndex(x => new { x.CustomerId, x.Date });
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ErpLinkSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.MethodFolder).HasMaxLength(260);
            e.Property(x => x.CompanyCode).HasMaxLength(20).IsRequired();
            e.Property(x => x.LastError).HasMaxLength(500);
        });

        b.Entity<ErpExport>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            e.Property(x => x.DocumentNo).HasMaxLength(30).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(100);
            e.Property(x => x.Error).HasMaxLength(500);
            // A document is sent at most once; failed attempts update the same row.
            e.HasIndex(x => new { x.Kind, x.DocumentId }).IsUnique();
        });

        b.Entity<DayClose>(e =>
        {
            e.HasIndex(x => new { x.Date, x.UserId }).IsUnique();
            e.Property(x => x.Denominations).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(200);
            foreach (var p in new[] { "OpeningFloat", "NetCash", "ExpectedCash", "CountedCash", "SalesTotal" })
                e.Property<decimal>(p).HasPrecision(18, 2);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SupplierPayment>(e =>
        {
            e.Property(x => x.PaymentNo).HasMaxLength(16).IsRequired();
            e.Property(x => x.FinancialYear).HasMaxLength(4).IsRequired();
            e.Property(x => x.Reference).HasMaxLength(50);
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.CancelReason).HasMaxLength(200);
            Money(e.Property(x => x.Amount));
            e.HasIndex(x => x.PaymentNo).IsUnique();
            e.HasIndex(x => new { x.SupplierId, x.Date });
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureStock(ModelBuilder b)
    {
        b.Entity<Purchase>(e =>
        {
            e.Property(x => x.PurchaseNo).HasMaxLength(16).IsRequired();
            e.Property(x => x.FinancialYear).HasMaxLength(4).IsRequired();
            e.Property(x => x.SupplierInvoiceNo).HasMaxLength(30).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.CancelReason).HasMaxLength(200);
            foreach (var p in new[] { "Taxable", "Tax", "RoundOff", "Total" })
                e.Property<decimal>(p).HasPrecision(18, 2);
            e.HasIndex(x => x.PurchaseNo).IsUnique();
            e.HasIndex(x => new { x.SupplierId, x.SupplierInvoiceNo });
            e.HasIndex(x => x.ReceivedAt);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne(x => x.Purchase).HasForeignKey(x => x.PurchaseId);
        });

        b.Entity<PurchaseLine>(e =>
        {
            e.Property(x => x.ItemCode).HasMaxLength(30).IsRequired();
            e.Property(x => x.ItemName).HasMaxLength(200).IsRequired();
            e.Property(x => x.BatchNo).HasMaxLength(30).IsRequired();
            Qty(e.Property(x => x.Qty));
            Qty(e.Property(x => x.FreeQty));
            e.Property(x => x.Rate).HasPrecision(18, 4);
            e.Property(x => x.DiscountPercent).HasPrecision(5, 2);
            e.Property(x => x.GstRate).HasPrecision(5, 2);
            foreach (var p in new[] { "Mrp", "SaleRate", "WholesaleRate", "HospitalRate", "Taxable", "Tax", "Amount" })
                e.Property<decimal>(p).HasPrecision(18, 2);
            Cost(e.Property(x => x.LandingCost));
            e.Property(x => x.MarginPercent).HasPrecision(7, 2);
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PurchaseReturn>(e =>
        {
            e.Property(x => x.ReturnNo).HasMaxLength(16).IsRequired();
            e.Property(x => x.FinancialYear).HasMaxLength(4).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(200);
            foreach (var p in new[] { "Taxable", "Tax", "RoundOff", "Total" })
                e.Property<decimal>(p).HasPrecision(18, 2);
            e.HasIndex(x => x.ReturnNo).IsUnique();
            e.HasIndex(x => x.ReturnDate);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Purchase).WithMany().HasForeignKey(x => x.PurchaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ReturnId);
        });

        b.Entity<PurchaseReturnLine>(e =>
        {
            e.Property(x => x.ItemName).HasMaxLength(200).IsRequired();
            e.Property(x => x.BatchNo).HasMaxLength(30).IsRequired();
            Qty(e.Property(x => x.Qty));
            Cost(e.Property(x => x.Rate));
            e.Property(x => x.GstRate).HasPrecision(5, 2);
            Money(e.Property(x => x.Taxable));
            Money(e.Property(x => x.Tax));
            Money(e.Property(x => x.Amount));
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PurchaseLine).WithMany().HasForeignKey(x => x.PurchaseLineId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StockAdjustment>(e =>
        {
            e.Property(x => x.AdjustmentNo).HasMaxLength(16).IsRequired();
            e.Property(x => x.Note).HasMaxLength(200);
            Qty(e.Property(x => x.QtyChange));
            e.HasIndex(x => x.AdjustmentNo).IsUnique();
            e.HasIndex(x => x.Date);
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ItemAlias>(e =>
        {
            e.Property(x => x.SupplierItemCode).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.SupplierId, x.SupplierItemCode }).IsUnique();
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureSales(ModelBuilder b)
    {
        b.Entity<SaleInvoice>(e =>
        {
            e.Property(x => x.InvoiceNo).HasMaxLength(16).IsRequired(); // GST limit for invoice numbers
            e.Property(x => x.FinancialYear).HasMaxLength(4).IsRequired();
            e.Property(x => x.CustomerName).HasMaxLength(200);
            e.Property(x => x.CustomerPhone).HasMaxLength(20);
            e.Property(x => x.CustomerGstin).HasMaxLength(15);
            e.Property(x => x.DoctorName).HasMaxLength(100);
            e.Property(x => x.PatientName).HasMaxLength(100);
            e.Property(x => x.CancelReason).HasMaxLength(200);
            foreach (var p in new[] { "Gross", "Discount", "Taxable", "Cgst", "Sgst", "Igst", "RoundOff", "Total", "BalanceDue" })
                e.Property<decimal>(p).HasPrecision(18, 2);
            e.HasIndex(x => x.InvoiceNo).IsUnique();
            e.HasIndex(x => x.InvoiceDate);
            e.HasIndex(x => x.CustomerId);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId);
            e.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.InvoiceId);
            e.HasMany(x => x.Returns).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SaleInvoiceLine>(e =>
        {
            e.Property(x => x.ItemCode).HasMaxLength(30).IsRequired();
            e.Property(x => x.ItemName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Pack).HasMaxLength(30);
            e.Property(x => x.HsnCode).HasMaxLength(10);
            e.Property(x => x.BatchNo).HasMaxLength(30).IsRequired();
            e.Property(x => x.GstRate).HasPrecision(5, 2);
            e.Property(x => x.DiscountPercent).HasPrecision(5, 2);
            Qty(e.Property(x => x.Qty));
            Qty(e.Property(x => x.ReturnedQty));
            Cost(e.Property(x => x.CostRate));
            foreach (var p in new[] { "Mrp", "Rate", "Gross", "Discount", "Taxable", "Tax", "Amount" })
                e.Property<decimal>(p).HasPrecision(18, 2);
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SalePayment>(e =>
        {
            Money(e.Property(x => x.Amount));
            e.Property(x => x.Reference).HasMaxLength(50);
        });

        b.Entity<SaleReturn>(e =>
        {
            e.Property(x => x.ReturnNo).HasMaxLength(16).IsRequired();
            e.Property(x => x.FinancialYear).HasMaxLength(4).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(200);
            foreach (var p in new[] { "Taxable", "Tax", "RoundOff", "Total" })
                e.Property<decimal>(p).HasPrecision(18, 2);
            e.HasIndex(x => x.ReturnNo).IsUnique();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ReturnId);
        });

        b.Entity<SaleReturnLine>(e =>
        {
            Qty(e.Property(x => x.Qty));
            Money(e.Property(x => x.Taxable));
            Money(e.Property(x => x.Tax));
            Money(e.Property(x => x.Amount));
            e.HasOne(x => x.InvoiceLine).WithMany().HasForeignKey(x => x.InvoiceLineId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StockMovement>(e =>
        {
            Qty(e.Property(x => x.QtyChange));
            e.Property(x => x.RefNo).HasMaxLength(30);
            e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.BatchId, x.CreatedAt });
            e.HasIndex(x => new { x.ItemId, x.CreatedAt });
        });

        b.Entity<DocumentSequence>(e =>
        {
            e.HasKey(x => new { x.Kind, x.FinancialYear });
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.FinancialYear).HasMaxLength(4);
        });

        b.Entity<StoreSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.StoreName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Email).HasMaxLength(100);
            e.Property(x => x.Gstin).HasMaxLength(15);
            e.Property(x => x.DrugLicenseNo).HasMaxLength(100);
            e.Property(x => x.StateCode).HasMaxLength(2);
            e.Property(x => x.InvoicePrefix).HasMaxLength(4).IsRequired();
            e.Property(x => x.InvoiceFooter).HasMaxLength(500);
            e.Property(x => x.MaxCashierDiscountPercent).HasPrecision(5, 2);
            e.Ignore(x => x.EffectiveStateCode);
        });

        b.Entity<HeldBill>(e =>
        {
            e.Property(x => x.Label).HasMaxLength(100).IsRequired();
            e.Property(x => x.Payload).IsRequired();
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
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

    /// <summary>Landed unit cost keeps 4 decimals (cost spread over free goods rarely divides evenly).</summary>
    private static void Cost(PropertyBuilder<decimal> p) => p.HasPrecision(18, 4);
    private static void Percent(PropertyBuilder<decimal> p) => p.HasPrecision(7, 2);
}
