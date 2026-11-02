using Microsoft.EntityFrameworkCore;
using BillKro.Sync.Core.Interfaces;
using BillKro.Sync.Core.Models;
using BillKro.Sync.Data.Entities;

namespace BillKro.Sync.Data.Repositories;

public class PartyRepository(SyncDbContext db) : IRepository<Party>
{
    public async Task UpsertAsync(IEnumerable<Party> records, CancellationToken ct = default)
    {
        foreach (var p in records)
        {
            var existing = await db.Parties
                .FirstOrDefaultAsync(e => e.CompanyCode == p.CompanyCode && e.Code == p.Code, ct);

            if (existing is null)
            {
                db.Parties.Add(Map(p));
            }
            else
            {
                existing.Name           = p.Name;
                existing.Type           = p.Type;
                existing.AccountCode    = p.AccountCode;
                existing.OpeningBalance = p.OpeningBalance;
                existing.DrCr           = p.DrCr;
                existing.BankName       = p.BankName;
                existing.GstNumber      = p.GstNumber;
                existing.Phone          = p.Phone;
                existing.Address        = p.Address;
                existing.UpdatedAt      = DateTime.UtcNow;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static PartyEntity Map(Party p) => new()
    {
        CompanyCode    = p.CompanyCode,
        Code           = p.Code,
        Name           = p.Name,
        Type           = p.Type,
        AccountCode    = p.AccountCode,
        OpeningBalance = p.OpeningBalance,
        DrCr           = p.DrCr,
        BankName       = p.BankName,
        GstNumber      = p.GstNumber,
        Phone          = p.Phone,
        Address        = p.Address,
    };
}
