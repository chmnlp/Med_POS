namespace MargPos.Shared;

public record ItemDto
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Barcode { get; init; }
    public string? Manufacturer { get; init; }
    public string? Category { get; init; }
    public string? Pack { get; init; }
    public string Unit { get; init; } = "PCS";
    public string? HsnCode { get; init; }
    public decimal GstRate { get; init; }
    public decimal Mrp { get; init; }
    public decimal SaleRate { get; init; }
    public decimal WholesaleRate { get; init; }
    public decimal PurchaseRate { get; init; }
    public bool IsBatchTracked { get; init; }
    public DrugSchedule Schedule { get; init; }
    public decimal ReorderLevel { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Sum of on-hand quantity across all batches (read-only).</summary>
    public decimal StockQty { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLowStock => ReorderLevel > 0 && StockQty <= ReorderLevel;
}

public record ItemUpsertRequest
{
    /// <summary>Leave blank on create to auto-generate.</summary>
    public string? Code { get; init; }
    public string Name { get; init; } = "";
    public string? Barcode { get; init; }
    public string? Manufacturer { get; init; }
    public string? Category { get; init; }
    public string? Pack { get; init; }
    public string Unit { get; init; } = "PCS";
    public string? HsnCode { get; init; }
    public decimal GstRate { get; init; }
    public decimal Mrp { get; init; }
    public decimal SaleRate { get; init; }
    public decimal WholesaleRate { get; init; }
    public decimal PurchaseRate { get; init; }
    public bool IsBatchTracked { get; init; }
    public DrugSchedule Schedule { get; init; }
    public decimal ReorderLevel { get; init; }
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Opening stock for items that are not batch-tracked. Used on create only;
    /// batch-tracked items take opening stock per batch.
    /// </summary>
    public decimal OpeningQty { get; init; }
}

public record BatchDto
{
    public int Id { get; init; }
    public int ItemId { get; init; }
    public string BatchNo { get; init; } = "";
    public DateOnly? MfgDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public decimal Mrp { get; init; }
    public decimal PurchaseRate { get; init; }
    public decimal SaleRate { get; init; }
    public decimal QtyOnHand { get; init; }
}

public record BatchUpsertRequest
{
    public string BatchNo { get; init; } = "";
    public DateOnly? MfgDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public decimal Mrp { get; init; }
    public decimal PurchaseRate { get; init; }
    public decimal SaleRate { get; init; }

    /// <summary>Opening quantity; used on create only. Later changes go through stock adjustments.</summary>
    public decimal OpeningQty { get; init; }
}
