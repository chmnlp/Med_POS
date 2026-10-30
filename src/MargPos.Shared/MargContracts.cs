namespace MargPos.Shared;

/// <summary>How the POS exchanges files with MARG ERP.</summary>
public record MargLinkSettingsDto
{
    /// <summary>Send purchases to MARG automatically.</summary>
    public bool Enabled { get; init; }

    /// <summary>MARG's import folder (its Method\ folder), e.g. \\MARG-PC\MARGERP\Method. Seen from the server PC.</summary>
    public string? MethodFolder { get; init; }

    /// <summary>Company / supplier code written in the EDE header (field 2).</summary>
    public string CompanyCode { get; init; } = "001";

    /// <summary>Only purchases received on or after this date are sent (so history is not re-imported).</summary>
    public DateOnly? SendFrom { get; init; }

    public int PollMinutes { get; init; } = 5;
}

/// <summary>Sent = file written to MARG's folder; DoneByHand = a manual task the user marked as entered in MARG.</summary>
public enum MargExportStatus { Sent = 0, Failed = 1, DoneByHand = 2 }

public record MargExportDto(int Id, string Kind, string DocumentNo, string? FileName, MargExportStatus Status, string? Error,
    DateTime CreatedAt, bool CancelledAfterSend);

/// <summary>Something MARG cannot take from a file, which must be entered there by hand.</summary>
public record MargManualTask(string Kind, int DocumentId, string DocumentNo, DateTime Date, string Party, decimal Amount, string Action);

public record MargLinkStatusDto(MargLinkSettingsDto Settings, DateTime? LastRunAt, string? LastError, int PendingPurchases,
    List<MargExportDto> RecentExports, List<MargManualTask> ManualTasks);

public record MargSendResult(int Sent, int Failed, string? Error);

public record MargFolderCheck(bool Ok, string Message);

public enum MargPartyImport { Skip = 0, Customers = 1, Suppliers = 2 }

public record MargMasterItemPreview(string Code, string Name, decimal Mrp, decimal PurchaseRate, decimal GstRate, string? BatchNo,
    DateOnly? ExpiryDate, decimal OpeningQty, bool Exists);

public record MargMasterPartyPreview(string? Code, string Name, string Type, decimal OpeningBalance, bool ExistsAsCustomer, bool ExistsAsSupplier);

public record MargMasterPreview(string? CompanyName, List<MargMasterItemPreview> Items, List<MargMasterPartyPreview> Parties)
{
    public int NewItems => Items.Count(i => !i.Exists);
}

public record MargMasterImportResult(int ItemsCreated, int ItemsUpdated, int LotsCreated, int PartiesCreated, int PartiesUpdated,
    List<string> Warnings);

/// <summary>One sales line for keying into MARG (no MARG sales import format is known).</summary>
public record SalesRegisterRow(string InvoiceNo, DateTime InvoiceDate, string? Customer, string? CustomerGstin, string ItemCode,
    string ItemName, string? Hsn, string BatchNo, DateOnly? Expiry, decimal Qty, decimal Rate, decimal DiscountPercent,
    decimal GstRate, decimal Taxable, decimal Tax, decimal Amount, string PaymentModes);
