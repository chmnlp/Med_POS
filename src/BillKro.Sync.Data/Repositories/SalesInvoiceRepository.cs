using Microsoft.EntityFrameworkCore;
using BillKro.Sync.Core.Interfaces;
using BillKro.Sync.Core.Models;
using BillKro.Sync.Data.Entities;

namespace BillKro.Sync.Data.Repositories;

public class SalesInvoiceRepository(SyncDbContext db) : IRepository<SalesInvoice>
{
    public async Task UpsertAsync(IEnumerable<SalesInvoice> records, CancellationToken ct = default)
    {
        foreach (var inv in records)
        {
            var existing = await db.SalesInvoices
                .Include(i => i.Lines)
                .FirstOrDefaultAsync(
                    e => e.CompanyCode == inv.CompanyCode && e.InvoiceNumber == inv.InvoiceNumber, ct);

            if (existing is null)
            {
                db.SalesInvoices.Add(Map(inv));
            }
            else
            {
                existing.CustomerName  = inv.CustomerName;
                existing.InvoiceDate   = inv.InvoiceDate;
                existing.TotalAmount   = inv.TotalAmount;
                existing.TotalDiscount = inv.TotalDiscount;
                existing.TotalTax      = inv.TotalTax;
                existing.NetAmount     = inv.NetAmount;
                existing.IrnNumber     = inv.IrnNumber;
                existing.EwayBillNumber = inv.EwayBillNumber;
                db.SalesInvoiceLines.RemoveRange(existing.Lines);
                existing.Lines = inv.Lines.Select(MapLine).ToList();
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static SalesInvoiceEntity Map(SalesInvoice i) => new()
    {
        CompanyCode    = i.CompanyCode,
        InvoiceNumber  = i.InvoiceNumber,
        CustomerCode   = i.CustomerCode,
        CustomerName   = i.CustomerName,
        InvoiceDate    = i.InvoiceDate,
        TotalAmount    = i.TotalAmount,
        TotalDiscount  = i.TotalDiscount,
        TotalTax       = i.TotalTax,
        NetAmount      = i.NetAmount,
        IrnNumber      = i.IrnNumber,
        EwayBillNumber = i.EwayBillNumber,
        Lines          = i.Lines.Select(MapLine).ToList(),
    };

    private static SalesInvoiceLineEntity MapLine(SalesInvoiceLine l) => new()
    {
        ItemCode  = l.ItemCode,
        ItemName  = l.ItemName,
        BatchNo   = l.BatchNo,
        ExpiryDate = l.ExpiryDate,
        Quantity  = l.Quantity,
        Rate      = l.Rate,
        MRP       = l.MRP,
        Discount  = l.Discount,
        TaxRate   = l.TaxRate,
        TaxAmount = l.TaxAmount,
        LineTotal = l.LineTotal,
    };
}
