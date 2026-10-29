using System.Linq.Expressions;
using MargPos.Data;
using MargPos.Server.Auth;
using MargPos.Server.Infrastructure;
using MargPos.Shared;
using Microsoft.EntityFrameworkCore;

namespace MargPos.Server.Endpoints;

public static class PartyEndpoints
{
    public static void MapParties(this IEndpointRouteBuilder app)
    {
        // Cashiers add and edit customers at the counter; deactivation is a manager action.
        var c = app.MapGroup("/api/customers");
        c.MapGet("/", ListCustomers);
        c.MapGet("/{id:int}", GetCustomer);
        c.MapPost("/", CreateCustomer);
        c.MapPut("/{id:int}", UpdateCustomer);
        c.MapDelete("/{id:int}", DeactivateCustomer).RequireAuthorization(Policies.Manager);

        var s = app.MapGroup("/api/suppliers");
        s.MapGet("/", ListSuppliers);
        s.MapGet("/{id:int}", GetSupplier);
        s.MapPost("/", CreateSupplier).RequireAuthorization(Policies.Manager);
        s.MapPut("/{id:int}", UpdateSupplier).RequireAuthorization(Policies.Manager);
        s.MapDelete("/{id:int}", DeactivateSupplier).RequireAuthorization(Policies.Manager);
    }

    // ---------- Customers ----------

    private const string CustomerDup = "A customer with this code already exists.";

    private static readonly Expression<Func<Customer, CustomerDto>> CustomerDto = x => new CustomerDto
    {
        Id = x.Id, Code = x.Code, Name = x.Name, Phone = x.Phone, Email = x.Email, Gstin = x.Gstin,
        Address = x.Address, Type = x.Type, CreditLimit = x.CreditLimit, OpeningBalance = x.OpeningBalance,
        IsActive = x.IsActive,
    };

    private static async Task<PagedResult<CustomerDto>> ListCustomers(PosDbContext db, string? q, CustomerType? type,
        bool? includeInactive, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = DbHelpers.Paging(page, pageSize);
        var query = db.Customers.AsNoTracking();
        if (includeInactive != true) query = query.Where(x => x.IsActive);
        if (type is not null) query = query.Where(x => x.Type == type);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = DbHelpers.Contains(q);
            query = query.Where(x => EF.Functions.Like(x.Name, like) || EF.Functions.Like(x.Code, like)
                || (x.Phone != null && EF.Functions.Like(x.Phone, like))
                || (x.Gstin != null && EF.Functions.Like(x.Gstin, like)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((p - 1) * size).Take(size)
            .Select(CustomerDto).ToListAsync(ct);
        return new PagedResult<CustomerDto>(rows, total, p, size);
    }

    private static async Task<IResult> GetCustomer(int id, PosDbContext db, CancellationToken ct) =>
        await db.Customers.AsNoTracking().Where(x => x.Id == id).Select(CustomerDto).FirstOrDefaultAsync(ct) is { } dto
            ? Results.Ok(dto) : DbHelpers.NotFound("Customer");

    private static async Task<IResult> CreateCustomer(CustomerUpsertRequest req, PosDbContext db, CancellationToken ct)
    {
        var errors = Validate(req);
        if (errors.Any) return errors.ToResult();

        var customer = new Customer();
        Apply(customer, req);
        customer.Code = Errors.Clean(req.Code)?.ToUpperInvariant()
            ?? await DbHelpers.NextCodeAsync(db.Customers.Select(x => x.Code), "C", ct);
        if (await db.Customers.AnyAsync(x => x.Code == customer.Code, ct)) return DbHelpers.Conflict(CustomerDup);

        db.Customers.Add(customer);
        if (await db.SaveOrConflictAsync(CustomerDup, ct) is { } conflict) return conflict;
        return Results.Created($"/api/customers/{customer.Id}",
            await db.Customers.AsNoTracking().Where(x => x.Id == customer.Id).Select(CustomerDto).FirstAsync(ct));
    }

    private static async Task<IResult> UpdateCustomer(int id, CustomerUpsertRequest req, PosDbContext db, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([id], ct);
        if (customer is null) return DbHelpers.NotFound("Customer");
        var errors = Validate(req);
        if (errors.Any) return errors.ToResult();

        Apply(customer, req);
        if (Errors.Clean(req.Code) is { } code) customer.Code = code.ToUpperInvariant();
        if (await db.Customers.AnyAsync(x => x.Code == customer.Code && x.Id != id, ct)) return DbHelpers.Conflict(CustomerDup);

        if (await db.SaveOrConflictAsync(CustomerDup, ct) is { } conflict) return conflict;
        return Results.Ok(await db.Customers.AsNoTracking().Where(x => x.Id == id).Select(CustomerDto).FirstAsync(ct));
    }

    private static async Task<IResult> DeactivateCustomer(int id, PosDbContext db, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([id], ct);
        if (customer is null) return DbHelpers.NotFound("Customer");
        customer.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Errors Validate(CustomerUpsertRequest req)
    {
        var e = new Errors()
            .Required(nameof(req.Name), req.Name, 200)
            .MaxLength(nameof(req.Code), req.Code, 30)
            .Phone(nameof(req.Phone), req.Phone)
            .Email(nameof(req.Email), req.Email)
            .Gstin(nameof(req.Gstin), req.Gstin)
            .MaxLength(nameof(req.Address), req.Address, 500)
            .NonNegative(nameof(req.CreditLimit), req.CreditLimit);
        if (!Enum.IsDefined(req.Type)) e.Add(nameof(req.Type), "Unknown customer type.");
        return e;
    }

    private static void Apply(Customer c, CustomerUpsertRequest req)
    {
        c.Name = req.Name.Trim();
        c.Phone = Errors.Clean(req.Phone);
        c.Email = Errors.Clean(req.Email);
        c.Gstin = Errors.Clean(req.Gstin)?.ToUpperInvariant();
        c.Address = Errors.Clean(req.Address);
        c.Type = req.Type;
        c.CreditLimit = req.CreditLimit;
        c.OpeningBalance = req.OpeningBalance;
        c.IsActive = req.IsActive;
    }

    // ---------- Suppliers ----------

    private const string SupplierDup = "A supplier with this code already exists.";

    private static readonly Expression<Func<Supplier, SupplierDto>> SupplierDto = x => new SupplierDto
    {
        Id = x.Id, Code = x.Code, Name = x.Name, Phone = x.Phone, Email = x.Email, Gstin = x.Gstin,
        DrugLicenseNo = x.DrugLicenseNo, Address = x.Address, OpeningBalance = x.OpeningBalance, IsActive = x.IsActive,
    };

    private static async Task<PagedResult<SupplierDto>> ListSuppliers(PosDbContext db, string? q, bool? includeInactive,
        int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = DbHelpers.Paging(page, pageSize);
        var query = db.Suppliers.AsNoTracking();
        if (includeInactive != true) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = DbHelpers.Contains(q);
            query = query.Where(x => EF.Functions.Like(x.Name, like) || EF.Functions.Like(x.Code, like)
                || (x.Phone != null && EF.Functions.Like(x.Phone, like))
                || (x.Gstin != null && EF.Functions.Like(x.Gstin, like)));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((p - 1) * size).Take(size)
            .Select(SupplierDto).ToListAsync(ct);
        return new PagedResult<SupplierDto>(rows, total, p, size);
    }

    private static async Task<IResult> GetSupplier(int id, PosDbContext db, CancellationToken ct) =>
        await db.Suppliers.AsNoTracking().Where(x => x.Id == id).Select(SupplierDto).FirstOrDefaultAsync(ct) is { } dto
            ? Results.Ok(dto) : DbHelpers.NotFound("Supplier");

    private static async Task<IResult> CreateSupplier(SupplierUpsertRequest req, PosDbContext db, CancellationToken ct)
    {
        var errors = Validate(req);
        if (errors.Any) return errors.ToResult();

        var supplier = new Supplier();
        Apply(supplier, req);
        supplier.Code = Errors.Clean(req.Code)?.ToUpperInvariant()
            ?? await DbHelpers.NextCodeAsync(db.Suppliers.Select(x => x.Code), "S", ct);
        if (await db.Suppliers.AnyAsync(x => x.Code == supplier.Code, ct)) return DbHelpers.Conflict(SupplierDup);

        db.Suppliers.Add(supplier);
        if (await db.SaveOrConflictAsync(SupplierDup, ct) is { } conflict) return conflict;
        return Results.Created($"/api/suppliers/{supplier.Id}",
            await db.Suppliers.AsNoTracking().Where(x => x.Id == supplier.Id).Select(SupplierDto).FirstAsync(ct));
    }

    private static async Task<IResult> UpdateSupplier(int id, SupplierUpsertRequest req, PosDbContext db, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FindAsync([id], ct);
        if (supplier is null) return DbHelpers.NotFound("Supplier");
        var errors = Validate(req);
        if (errors.Any) return errors.ToResult();

        Apply(supplier, req);
        if (Errors.Clean(req.Code) is { } code) supplier.Code = code.ToUpperInvariant();
        if (await db.Suppliers.AnyAsync(x => x.Code == supplier.Code && x.Id != id, ct)) return DbHelpers.Conflict(SupplierDup);

        if (await db.SaveOrConflictAsync(SupplierDup, ct) is { } conflict) return conflict;
        return Results.Ok(await db.Suppliers.AsNoTracking().Where(x => x.Id == id).Select(SupplierDto).FirstAsync(ct));
    }

    private static async Task<IResult> DeactivateSupplier(int id, PosDbContext db, CancellationToken ct)
    {
        var supplier = await db.Suppliers.FindAsync([id], ct);
        if (supplier is null) return DbHelpers.NotFound("Supplier");
        supplier.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Errors Validate(SupplierUpsertRequest req) => new Errors()
        .Required(nameof(req.Name), req.Name, 200)
        .MaxLength(nameof(req.Code), req.Code, 30)
        .Phone(nameof(req.Phone), req.Phone)
        .Email(nameof(req.Email), req.Email)
        .Gstin(nameof(req.Gstin), req.Gstin)
        .MaxLength(nameof(req.DrugLicenseNo), req.DrugLicenseNo, 50)
        .MaxLength(nameof(req.Address), req.Address, 500);

    private static void Apply(Supplier s, SupplierUpsertRequest req)
    {
        s.Name = req.Name.Trim();
        s.Phone = Errors.Clean(req.Phone);
        s.Email = Errors.Clean(req.Email);
        s.Gstin = Errors.Clean(req.Gstin)?.ToUpperInvariant();
        s.DrugLicenseNo = Errors.Clean(req.DrugLicenseNo)?.ToUpperInvariant();
        s.Address = Errors.Clean(req.Address);
        s.OpeningBalance = req.OpeningBalance;
        s.IsActive = req.IsActive;
    }
}
