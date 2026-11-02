namespace BillKro.Shared;

/// <summary>How the POS exchanges files with MARG ERP.</summary>
public record ErpLinkSettingsDto
{
    /// <summary>Send purchases to MARG automatically.</summary>
    public bool Enabled { get; init; }

    /// <summary>MARG's import folder (its Method\ folder), e.g. \\ERP-PC\ERP\Method. Seen from the server PC.</summary>
    public string? MethodFolder { get; init; }

    /// <summary>Company / supplier code written in the EDE header (field 2).</summary>
    public string CompanyCode { get; init; } = "001";

    /// <summary>Only purchases received on or after this date are sent (so history is not re-imported).</summary>
    public DateOnly? SendFrom { get; init; }

    public int PollMinutes { get; init; } = 5;
}

/// <summary>Sent = file written to MARG's folder; DoneByHand = a manual task the user marked as entered in MARG.</summary>
public enum ErpExportStatus { Sent = 0, Failed = 1, DoneByHand = 2 }

public record ErpExportDto(int Id, string Kind, string DocumentNo, string? FileName, ErpExportStatus Status, string? Error,
    DateTime CreatedAt, bool CancelledAfterSend);

/// <summary>Something MARG cannot take from a file, which must be entered there by hand.</summary>
public record ErpManualTask(string Kind, int DocumentId, string DocumentNo, DateTime Date, string Party, decimal Amount, string Action);

public record ErpLinkStatusDto(ErpLinkSettingsDto Settings, DateTime? LastRunAt, string? LastError, int PendingPurchases,
    List<ErpExportDto> RecentExports, List<ErpManualTask> ManualTasks);

public record ErpSendResult(int Sent, int Failed, string? Error);

public record ErpFolderCheck(bool Ok, string Message);

public enum ErpPartyImport { Skip = 0, Customers = 1, Suppliers = 2 }

public record ErpMasterItemPreview(string Code, string Name, decimal Mrp, decimal PurchaseRate, decimal GstRate, string? BatchNo,
    DateOnly? ExpiryDate, decimal OpeningQty, bool Exists);

public record ErpMasterPartyPreview(string? Code, string Name, string Type, decimal OpeningBalance, bool ExistsAsCustomer, bool ExistsAsSupplier);

public record ErpMasterPreview(string? CompanyName, List<ErpMasterItemPreview> Items, List<ErpMasterPartyPreview> Parties)
{
    public int NewItems => Items.Count(i => !i.Exists);
}

public record ErpMasterImportResult(int ItemsCreated, int ItemsUpdated, int LotsCreated, int PartiesCreated, int PartiesUpdated,
    List<string> Warnings);

/// <summary>One sales line for keying into MARG (no MARG sales import format is known).</summary>
public record SalesRegisterRow(string InvoiceNo, DateTime InvoiceDate, string? Customer, string? CustomerGstin, string ItemCode,
    string ItemName, string? Hsn, string BatchNo, DateOnly? Expiry, decimal Qty, decimal Rate, decimal DiscountPercent,
    decimal GstRate, decimal Taxable, decimal Tax, decimal Amount, string PaymentModes);
