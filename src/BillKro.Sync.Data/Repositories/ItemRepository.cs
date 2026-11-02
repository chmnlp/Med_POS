using Microsoft.EntityFrameworkCore;
using BillKro.Sync.Core.Interfaces;
using BillKro.Sync.Core.Models;
using BillKro.Sync.Data.Entities;

namespace BillKro.Sync.Data.Repositories;

public class ItemRepository(SyncDbContext db) : IRepository<Item>
{
    public async Task UpsertAsync(IEnumerable<Item> records, CancellationToken ct = default)
    {
        foreach (var item in records)
        {
            var existing = await db.Items
                .FirstOrDefaultAsync(e => e.CompanyCode == item.CompanyCode && e.Code == item.Code, ct);

            if (existing is null)
            {
                db.Items.Add(Map(item));
            }
            else
            {
                existing.Name             = item.Name;
                existing.PackForm         = item.PackForm;
                existing.Manufacturer     = item.Manufacturer;
                existing.AccountCode      = item.AccountCode;
                existing.HsnCode          = item.HsnCode;
                existing.MRP              = item.MRP;
                existing.PurchaseRate     = item.PurchaseRate;
                existing.LandingCost      = item.LandingCost;
                existing.TaxRate          = item.TaxRate;
                existing.DiscountPercent  = item.DiscountPercent;
                existing.OpeningQty       = item.OpeningQty;
                existing.PackSize         = item.PackSize;
                existing.BatchNo          = item.BatchNo;
                existing.ManufacturingDate = item.ManufacturingDate;
                existing.ExpiryDate       = item.ExpiryDate;
                existing.Category         = item.Category;
                existing.UpdatedAt        = DateTime.UtcNow;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static ItemEntity Map(Item i) => new()
    {
        CompanyCode      = i.CompanyCode,
        Code             = i.Code,
        Name             = i.Name,
        PackForm         = i.PackForm,
        Manufacturer     = i.Manufacturer,
        AccountCode      = i.AccountCode,
        HsnCode          = i.HsnCode,
        MRP              = i.MRP,
        PurchaseRate     = i.PurchaseRate,
        LandingCost      = i.LandingCost,
        TaxRate          = i.TaxRate,
        DiscountPercent  = i.DiscountPercent,
        OpeningQty       = i.OpeningQty,
        PackSize         = i.PackSize,
        BatchNo          = i.BatchNo,
        ManufacturingDate = i.ManufacturingDate,
        ExpiryDate       = i.ExpiryDate,
        Category         = i.Category,
    };
}
