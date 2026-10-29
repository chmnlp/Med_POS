namespace MargPos.Shared;

public record CustomerDto
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Gstin { get; init; }
    public string? Address { get; init; }
    public CustomerType Type { get; init; }
    public decimal CreditLimit { get; init; }
    public decimal OpeningBalance { get; init; }
    public bool IsActive { get; init; }
}

public record CustomerUpsertRequest
{
    /// <summary>Leave blank on create to auto-generate.</summary>
    public string? Code { get; init; }
    public string Name { get; init; } = "";
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Gstin { get; init; }
    public string? Address { get; init; }
    public CustomerType Type { get; init; }
    public decimal CreditLimit { get; init; }
    public decimal OpeningBalance { get; init; }
    public bool IsActive { get; init; } = true;
}

public record SupplierDto
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Gstin { get; init; }
    public string? DrugLicenseNo { get; init; }
    public string? Address { get; init; }
    public decimal OpeningBalance { get; init; }
    public bool IsActive { get; init; }
}

public record SupplierUpsertRequest
{
    /// <summary>Leave blank on create to auto-generate.</summary>
    public string? Code { get; init; }
    public string Name { get; init; } = "";
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Gstin { get; init; }
    public string? DrugLicenseNo { get; init; }
    public string? Address { get; init; }
    public decimal OpeningBalance { get; init; }
    public bool IsActive { get; init; } = true;
}
