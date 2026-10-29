using Microsoft.EntityFrameworkCore;
using MargSync.Core.Interfaces;
using MargSync.Core.Models;
using MargSync.Data.Entities;

namespace MargSync.Data.Repositories;

public class PurchaseBillRepository(MargSyncDbContext db) : IRepository<PurchaseBill>
{
    public async Task UpsertAsync(IEnumerable<PurchaseBill> records, CancellationToken ct = default)
    {
        foreach (var bill in records)
        {
            var existing = await db.PurchaseBills
                .Include(b => b.Lines)
                .FirstOrDefaultAsync(
                    e => e.CompanyCode == bill.CompanyCode && e.BillNumber == bill.BillNumber, ct);

            if (existing is null)
            {
                db.PurchaseBills.Add(Map(bill));
            }
            else
            {
                existing.SupplierName     = bill.SupplierName;
                existing.BillDate         = bill.BillDate;
                existing.DeliveryMode     = bill.DeliveryMode;
                existing.DeliveryLocation = bill.DeliveryLocation;
                existing.TotalAmount      = bill.TotalAmount;
                existing.TotalTax         = bill.TotalTax;
                existing.NetAmount        = bill.NetAmount;
                db.PurchaseBillLines.RemoveRange(existing.Lines);
                existing.Lines = bill.Lines.Select(MapLine).ToList();
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static PurchaseBillEntity Map(PurchaseBill b) => new()
    {
        CompanyCode      = b.CompanyCode,
        BillNumber       = b.BillNumber,
        SupplierCode     = b.SupplierCode,
        SupplierName     = b.SupplierName,
        BillDate         = b.BillDate,
        DeliveryMode     = b.DeliveryMode,
        DeliveryLocation = b.DeliveryLocation,
        TotalAmount      = b.TotalAmount,
        TotalTax         = b.TotalTax,
        NetAmount        = b.NetAmount,
        Lines            = b.Lines.Select(MapLine).ToList(),
    };

    private static PurchaseBillLineEntity MapLine(PurchaseBillLine l) => new()
    {
        ItemCode     = l.ItemCode,
        ItemName     = l.ItemName,
        PackForm     = l.PackForm,
        Manufacturer = l.Manufacturer,
        BatchNo      = l.BatchNo,
        ExpiryDate   = l.ExpiryDate,
        Quantity     = l.Quantity,
        TaxRate      = l.TaxRate,
        PurchaseRate = l.PurchaseRate,
        CostRate     = l.CostRate,
        MRP          = l.MRP,
        Discount     = l.Discount,
        TaxAmount    = l.TaxAmount,
    };
}
