using MargPos.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MargPos.Server.Infrastructure;

public static class DbHelpers
{
    /// <summary>
    /// Saves changes and maps a unique-index violation (a race the pre-checks missed) to 409.
    /// Returns null on success.
    /// </summary>
    public static async Task<IResult?> SaveOrConflictAsync(this PosDbContext db, string conflictMessage, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Results.Problem(conflictMessage, statusCode: StatusCodes.Status409Conflict);
        }
    }

    /// <summary>Next code of the form PREFIX00001, based on the highest numeric suffix in use.</summary>
    public static async Task<string> NextCodeAsync(IQueryable<string> codes, string prefix, CancellationToken ct)
    {
        var existing = await codes.Where(c => c.StartsWith(prefix)).ToListAsync(ct);
        var max = existing
            .Select(c => int.TryParse(c[prefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{prefix}{max + 1:D5}";
    }

    public static IResult NotFound(string what) =>
        Results.Problem($"{what} not found.", statusCode: StatusCodes.Status404NotFound);

    public static IResult Conflict(string message) =>
        Results.Problem(message, statusCode: StatusCodes.Status409Conflict);

    public static (int Page, int PageSize) Paging(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 50, 1, 200));

    /// <summary>LIKE pattern for "contains", with wildcard characters in user input escaped.</summary>
    public static string Contains(string q) =>
        "%" + q.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
}
