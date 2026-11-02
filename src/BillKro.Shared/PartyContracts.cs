namespace BillKro.Shared;

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

    /// <summary>Days allowed to pay a credit bill before it is overdue.</summary>
    public int CreditDays { get; init; } = 30;
    public decimal OpeningBalance { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Accessible name in lists and combo boxes.</summary>
    public override string ToString() => Name;
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

    /// <summary>Days allowed to pay a credit bill before it is overdue.</summary>
    public int CreditDays { get; init; } = 30;
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

    /// <summary>Days the supplier allows us to pay.</summary>
    public int CreditDays { get; init; } = 30;
    public decimal OpeningBalance { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Accessible name in lists and combo boxes.</summary>
    public override string ToString() => Name;
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

    /// <summary>Days the supplier allows us to pay.</summary>
    public int CreditDays { get; init; } = 30;
    public decimal OpeningBalance { get; init; }
    public bool IsActive { get; init; } = true;
}
