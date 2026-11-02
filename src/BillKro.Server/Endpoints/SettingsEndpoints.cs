using System.Security.Claims;
using System.Text.RegularExpressions;
using BillKro.Data;
using BillKro.Server.Auth;
using BillKro.Server.Infrastructure;
using BillKro.Shared;
using Microsoft.EntityFrameworkCore;

namespace BillKro.Server.Endpoints;

public static partial class SettingsEndpoints
{
    public const int MaxHeldPayloadLength = 256 * 1024;

    public static void MapSettings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings", Get);
        app.MapPut("/api/settings", Update).RequireAuthorization(Policies.Admin);

        var held = app.MapGroup("/api/held-bills");
        held.MapGet("/", ListHeld);
        held.MapPost("/", Hold);
        held.MapDelete("/{id:int}", DeleteHeld);
    }

    private static async Task<StoreSettingsDto> Get(PosDbContext db, CancellationToken ct) =>
        ToDto(await db.StoreSettings.AsNoTracking().FirstAsync(ct));

    private static async Task<IResult> Update(StoreSettingsDto req, PosDbContext db, CancellationToken ct)
    {
        var e = new Errors()
            .Required(nameof(req.StoreName), req.StoreName, 200)
            .MaxLength(nameof(req.Address), req.Address, 500)
            .MaxLength(nameof(req.Phone), req.Phone, 50)
            .Email(nameof(req.Email), req.Email)
            .Gstin(nameof(req.Gstin), req.Gstin)
            .MaxLength(nameof(req.DrugLicenseNo), req.DrugLicenseNo, 100)
            .MaxLength(nameof(req.InvoiceFooter), req.InvoiceFooter, 500);
        if (Errors.Clean(req.StateCode) is { } sc && !StateCodeRegex().IsMatch(sc))
            e.Add(nameof(req.StateCode), "State code must be 2 digits, e.g. 27 for Maharashtra.");
        if (string.IsNullOrWhiteSpace(req.InvoicePrefix) || !PrefixRegex().IsMatch(req.InvoicePrefix.Trim().ToUpperInvariant()))
            e.Add(nameof(req.InvoicePrefix), "Invoice prefix must be 1–4 letters.");
        if (req.MaxCashierDiscountPercent is < 0 or > 100)
            e.Add(nameof(req.MaxCashierDiscountPercent), "Cashier discount limit must be between 0 and 100%.");
        if (e.Any) return e.ToResult();

        var s = await db.StoreSettings.FirstAsync(ct);
        s.StoreName = req.StoreName.Trim();
        s.Address = Errors.Clean(req.Address);
        s.Phone = Errors.Clean(req.Phone);
        s.Email = Errors.Clean(req.Email);
        s.Gstin = Errors.Clean(req.Gstin)?.ToUpperInvariant();
        s.DrugLicenseNo = Errors.Clean(req.DrugLicenseNo);
        s.StateCode = Errors.Clean(req.StateCode);
        s.InvoicePrefix = req.InvoicePrefix.Trim().ToUpperInvariant();
        s.InvoiceFooter = Errors.Clean(req.InvoiceFooter);
        s.MaxCashierDiscountPercent = req.MaxCashierDiscountPercent;
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(s));
    }

    private static StoreSettingsDto ToDto(StoreSettings s) => new()
    {
        StoreName = s.StoreName, Address = s.Address, Phone = s.Phone, Email = s.Email, Gstin = s.Gstin,
        DrugLicenseNo = s.DrugLicenseNo, StateCode = s.EffectiveStateCode, InvoicePrefix = s.InvoicePrefix,
        InvoiceFooter = s.InvoiceFooter, MaxCashierDiscountPercent = s.MaxCashierDiscountPercent,
    };

    private static async Task<List<HeldBillDto>> ListHeld(PosDbContext db, CancellationToken ct) =>
        await db.HeldBills.AsNoTracking().OrderByDescending(h => h.CreatedAt)
            .Select(h => new HeldBillDto(h.Id, h.Label, h.Payload, h.CreatedBy.FullName, h.CreatedAt))
            .ToListAsync(ct);

    private static async Task<IResult> Hold(HoldBillRequest req, ClaimsPrincipal principal, PosDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var e = new Errors().Required(nameof(req.Label), req.Label, 100);
        if (string.IsNullOrEmpty(req.Payload) || req.Payload.Length > MaxHeldPayloadLength)
            e.Add(nameof(req.Payload), "Held bill is empty or too large.");
        if (e.Any) return e.ToResult();

        // Shop-local time, like invoice dates.
        var bill = new HeldBill
        {
            Label = req.Label.Trim(), Payload = req.Payload, CreatedByUserId = principal.GetUserId(),
            CreatedAt = clock.GetLocalNow().DateTime,
        };
        db.HeldBills.Add(bill);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/held-bills/{bill.Id}", new { bill.Id });
    }

    /// <summary>Resuming a held bill removes it; any counter may resume any held bill.</summary>
    private static async Task<IResult> DeleteHeld(int id, PosDbContext db, CancellationToken ct)
    {
        var deleted = await db.HeldBills.Where(h => h.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? DbHelpers.NotFound("Held bill") : Results.NoContent();
    }

    [GeneratedRegex(@"^\d{2}$")]
    private static partial Regex StateCodeRegex();

    [GeneratedRegex(@"^[A-Z]{1,4}$")]
    private static partial Regex PrefixRegex();
}
