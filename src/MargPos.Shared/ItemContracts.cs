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
    public decimal HospitalRate { get; init; }
    public decimal PurchaseRate { get; init; }

    /// <summary>Default margin on landed cost used to suggest retail / wholesale rates at purchase (0 = none).</summary>
    public decimal MarginPercent { get; init; }
    public decimal WholesaleMarginPercent { get; init; }
    public bool IsBatchTracked { get; init; }
    public DrugSchedule Schedule { get; init; }
    public decimal ReorderLevel { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Sum of on-hand quantity across all batches (read-only).</summary>
    public decimal StockQty { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLowStock => ReorderLevel > 0 && StockQty <= ReorderLevel;

    /// <summary>Accessible name in lists and combo boxes.</summary>
    public override string ToString() => Name;
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
    public decimal HospitalRate { get; init; }
    public decimal PurchaseRate { get; init; }

    /// <summary>Default margin on landed cost used to suggest retail / wholesale rates at purchase (0 = none).</summary>
    public decimal MarginPercent { get; init; }
    public decimal WholesaleMarginPercent { get; init; }
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
    public decimal WholesaleRate { get; init; }
    public decimal HospitalRate { get; init; }
    public decimal QtyOnHand { get; init; }
    public string? Barcode { get; init; }
}

public record BatchUpsertRequest
{
    public string BatchNo { get; init; } = "";
    public DateOnly? MfgDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public decimal Mrp { get; init; }
    public decimal PurchaseRate { get; init; }
    public decimal SaleRate { get; init; }

    /// <summary>Lot price list for wholesale / hospital customers; 0 uses the item's rate.</summary>
    public decimal WholesaleRate { get; init; }
    public decimal HospitalRate { get; init; }

    /// <summary>Lot barcode, optional; must not be used by any other item or batch.</summary>
    public string? Barcode { get; init; }

    /// <summary>Opening quantity; used on create only. Later changes go through stock adjustments.</summary>
    public decimal OpeningQty { get; init; }
}

/// <summary>What a scanned or typed code points at: an item, and the exact batch when it was a lot barcode.</summary>
public record ScanResultDto(ItemDto Item, int? BatchId, string? BatchNo);

public record SetBarcodeRequest(string? Barcode);

/// <summary>Labels for whole items (their batches in stock, or the item's own stock) and/or specific batches.</summary>
public record LabelRequest
{
    public List<int> ItemIds { get; init; } = [];
    public List<int> BatchIds { get; init; } = [];
}

/// <summary>One label design's data. Batch-tracked lots always carry a barcode (assigned on first print).</summary>
public record LabelDto
{
    public int ItemId { get; init; }
    public int BatchId { get; init; }
    public string ItemName { get; init; } = "";
    public string? Pack { get; init; }
    public string? BatchNo { get; init; }
    public DateOnly? MfgDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public decimal Mrp { get; init; }
    public decimal SaleRate { get; init; }
    public string Barcode { get; init; } = "";
    public decimal QtyOnHand { get; init; }
}
