using Microsoft.EntityFrameworkCore;
using BillKro.Sync.Core.Interfaces;
using BillKro.Sync.Core.Models;
using BillKro.Sync.Data.Entities;

namespace BillKro.Sync.Data.Repositories;

public class StockEntryRepository(SyncDbContext db) : IRepository<StockEntry>
{
    public async Task UpsertAsync(IEnumerable<StockEntry> records, CancellationToken ct = default)
    {
        foreach (var s in records)
        {
            var existing = await db.StockEntries.FirstOrDefaultAsync(
                e => e.CompanyCode == s.CompanyCode
                  && e.ItemCode == s.ItemCode
                  && e.BatchNo == s.BatchNo
                  && e.AsOfDate == s.AsOfDate, ct);

            if (existing is null)
                db.StockEntries.Add(Map(s));
            else
            {
                existing.ClosingQty   = s.ClosingQty;
                existing.ClosingValue = s.ClosingValue;
                existing.MRP          = s.MRP;
                existing.UpdatedAt    = DateTime.UtcNow;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static StockEntryEntity Map(StockEntry s) => new()
    {
        CompanyCode  = s.CompanyCode,
        ItemCode     = s.ItemCode,
        ItemName     = s.ItemName,
        BatchNo      = s.BatchNo,
        ExpiryDate   = s.ExpiryDate,
        ClosingQty   = s.ClosingQty,
        ClosingValue = s.ClosingValue,
        MRP          = s.MRP,
        AsOfDate     = s.AsOfDate,
    };
}

public class LedgerEntryRepository(SyncDbContext db) : IRepository<LedgerEntry>
{
    public async Task UpsertAsync(IEnumerable<LedgerEntry> records, CancellationToken ct = default)
    {
        foreach (var l in records)
        {
            bool exists = await db.LedgerEntries.AnyAsync(
                e => e.CompanyCode == l.CompanyCode
                  && e.VoucherNumber == l.VoucherNumber
                  && e.EntryDate == l.EntryDate
                  && e.AccountCode == l.AccountCode, ct);
            if (!exists)
                db.LedgerEntries.Add(Map(l));
        }
        await db.SaveChangesAsync(ct);
    }

    private static LedgerEntryEntity Map(LedgerEntry l) => new()
    {
        CompanyCode   = l.CompanyCode,
        AccountCode   = l.AccountCode,
        AccountName   = l.AccountName,
        EntryDate     = l.EntryDate,
        VoucherType   = l.VoucherType,
        VoucherNumber = l.VoucherNumber,
        Narration     = l.Narration,
        DebitAmount   = l.DebitAmount,
        CreditAmount  = l.CreditAmount,
    };
}

public class GstEntryRepository(SyncDbContext db) : IRepository<GstEntry>
{
    public async Task UpsertAsync(IEnumerable<GstEntry> records, CancellationToken ct = default)
    {
        foreach (var g in records)
        {
            var existing = await db.GstEntries.FirstOrDefaultAsync(
                e => e.CompanyCode == g.CompanyCode && e.InvoiceNumber == g.InvoiceNumber, ct);
            if (existing is null)
                db.GstEntries.Add(Map(g));
            else
            {
                existing.TaxableValue   = g.TaxableValue;
                existing.CgstAmount     = g.CgstAmount;
                existing.SgstAmount     = g.SgstAmount;
                existing.IgstAmount     = g.IgstAmount;
                existing.IrnNumber      = g.IrnNumber;
                existing.ReturnPeriod   = g.ReturnPeriod;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static GstEntryEntity Map(GstEntry g) => new()
    {
        CompanyCode    = g.CompanyCode,
        InvoiceNumber  = g.InvoiceNumber,
        InvoiceDate    = g.InvoiceDate,
        GstinSupplier  = g.GstinSupplier,
        GstinRecipient = g.GstinRecipient,
        HsnCode        = g.HsnCode,
        TaxableValue   = g.TaxableValue,
        CgstRate       = g.CgstRate,
        SgstRate       = g.SgstRate,
        IgstRate       = g.IgstRate,
        CgstAmount     = g.CgstAmount,
        SgstAmount     = g.SgstAmount,
        IgstAmount     = g.IgstAmount,
        IrnNumber      = g.IrnNumber,
        ReturnPeriod   = g.ReturnPeriod,
    };
}
