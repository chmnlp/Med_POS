using BillKro.Shared;

namespace BillKro.Data;

/// <summary>Single-row settings for exchanging files with MARG ERP.</summary>
public class ErpLinkSettings
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    public string? MethodFolder { get; set; }
    public string CompanyCode { get; set; } = "001";
    public DateOnly? SendFrom { get; set; }
    public int PollMinutes { get; set; } = 5;
    public DateTime? LastRunAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Outbox: one row per document written to MARG's folder, so nothing is sent twice.</summary>
public class ErpExport
{
    public int Id { get; set; }

    /// <summary>"Purchase" for now.</summary>
    public string Kind { get; set; } = "";
    public int DocumentId { get; set; }
    public string DocumentNo { get; set; } = "";
    public string? FileName { get; set; }
    public ErpExportStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}