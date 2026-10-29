using MargPos.Shared;

namespace MargPos.Data;

public abstract class EntityBase
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class User : EntityBase
{
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }

    /// <summary>Rotated on password change, reset or deactivation; invalidates issued tokens.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
}

public class Item : EntityBase
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Barcode { get; set; }
    public string? Manufacturer { get; set; }
    public string? Category { get; set; }
    public string? Pack { get; set; }
    public string Unit { get; set; } = "PCS";
    public string? HsnCode { get; set; }
    public decimal GstRate { get; set; }
    public decimal Mrp { get; set; }
    public decimal SaleRate { get; set; }
    public decimal WholesaleRate { get; set; }
    public decimal PurchaseRate { get; set; }
    public bool IsBatchTracked { get; set; }
    public DrugSchedule Schedule { get; set; }
    public decimal ReorderLevel { get; set; }
    public bool IsActive { get; set; } = true;

    public List<Batch> Batches { get; set; } = [];
}

/// <summary>
/// Unit of stock. Every item holds stock in batches; items that are not batch-tracked
/// have a single batch with <see cref="DefaultBatchNo"/>.
/// </summary>
public class Batch : EntityBase
{
    public const string DefaultBatchNo = "-";

    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public string BatchNo { get; set; } = DefaultBatchNo;
    public DateOnly? MfgDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal Mrp { get; set; }
    public decimal PurchaseRate { get; set; }
    public decimal SaleRate { get; set; }
    public decimal QtyOnHand { get; set; }
}

public class Customer : EntityBase
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Gstin { get; set; }
    public string? Address { get; set; }
    public CustomerType Type { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Supplier : EntityBase
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Gstin { get; set; }
    public string? DrugLicenseNo { get; set; }
    public string? Address { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;
}
