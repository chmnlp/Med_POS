using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MargPos.Shared;

namespace MargPos.Desktop.Services;

/// <summary>Lays out invoices as WPF FlowDocuments for an 80 mm thermal roll or A4, and prints them.</summary>
public class InvoicePrinter(ClientSettings settings, StoreContext store)
{
    private const double ThermalWidth = 280; // ~74 mm printable on an 80 mm roll, in 1/96 inch

    private bool IsThermal => settings.PaperSize != "A4";

    public bool IsThermalPaper => IsThermal;

    public void Print(SaleInvoiceDto invoice)
    {
        var dialog = new PrintDialog();
        var queue = FindQueue(settings.PrinterName);
        if (queue is not null) dialog.PrintQueue = queue;
        else
        {
            if (dialog.ShowDialog() != true) return;
            settings.PrinterName = dialog.PrintQueue.FullName;
            settings.Save();
        }

        var doc = Build(invoice);
        if (!IsThermal)
        {
            doc.PageWidth = dialog.PrintableAreaWidth;
            doc.PageHeight = dialog.PrintableAreaHeight;
            doc.ColumnWidth = doc.PageWidth;
        }
        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"Invoice {invoice.InvoiceNo}");
    }

    internal static PrintQueue? FindQueue(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            using var server = new LocalPrintServer();
            return server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections])
                .FirstOrDefault(q => q.FullName == name);
        }
        catch (PrintSystemException)
        {
            return null;
        }
    }

    public FlowDocument Build(SaleInvoiceDto inv)
    {
        var s = store.Settings;
        var thermal = IsThermal;
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = thermal ? 10.5 : 11.5,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            PagePadding = thermal ? new Thickness(4, 0, 4, 12) : new Thickness(48),
            PageWidth = thermal ? ThermalWidth : 794,
            ColumnWidth = thermal ? ThermalWidth : 794,
        };
        if (thermal) doc.PageHeight = 96 * 30; // one long "page" on the roll

        // Store header
        doc.Blocks.Add(Para(s.StoreName, thermal ? 15 : 20, FontWeights.Bold, TextAlignment.Center));
        var contact = new[] { s.Address, s.Phone is null ? null : $"Ph: {s.Phone}" }.Where(x => x is not null);
        doc.Blocks.Add(Para(string.Join("\n", contact), align: TextAlignment.Center));
        var regs = new[] { s.Gstin is null ? null : $"GSTIN: {s.Gstin}", s.DrugLicenseNo is null ? null : $"D.L.: {s.DrugLicenseNo}" }
            .Where(x => x is not null);
        if (regs.Any()) doc.Blocks.Add(Para(string.Join(thermal ? "\n" : "   ", regs), align: TextAlignment.Center));

        var title = inv.Status == InvoiceStatus.Cancelled ? "*** CANCELLED ***" : "TAX INVOICE";
        doc.Blocks.Add(Rule(Para(title, thermal ? 12 : 14, FontWeights.Bold, TextAlignment.Center)));

        // Bill details
        var details = new List<(string, string)>
        {
            ("Bill No", inv.InvoiceNo),
            ("Date", inv.InvoiceDate.ToString("dd-MM-yyyy HH:mm")),
        };
        if (inv.CustomerName is not null) details.Add(("Customer", inv.CustomerName));
        if (inv.CustomerPhone is not null) details.Add(("Phone", inv.CustomerPhone));
        if (inv.CustomerGstin is not null) details.Add(("GSTIN", inv.CustomerGstin));
        if (inv.PatientName is not null) details.Add(("Patient", inv.PatientName));
        if (inv.DoctorName is not null) details.Add(("Doctor", inv.DoctorName));
        doc.Blocks.Add(KeyValues(details, thermal ? 70 : 110));

        doc.Blocks.Add(thermal ? ThermalLines(inv) : A4Lines(inv));

        // Totals
        var totals = new List<(string, string)> { ("Gross", Money(inv.Gross)) };
        if (inv.Discount != 0) totals.Add(("Discount", "-" + Money(inv.Discount)));
        totals.Add(("Taxable value", Money(inv.Taxable)));
        if (inv.IsInterState) totals.Add(("IGST", Money(inv.Igst)));
        else
        {
            totals.Add(("CGST", Money(inv.Cgst)));
            totals.Add(("SGST", Money(inv.Sgst)));
        }
        if (inv.RoundOff != 0) totals.Add(("Round off", inv.RoundOff.ToString("+0.00;-0.00")));
        doc.Blocks.Add(Rule(KeyValues(totals, 0, rightAlignValues: true)));
        doc.Blocks.Add(Rule(KeyValues([("NET AMOUNT", "₹ " + Money(inv.Total))], 0, rightAlignValues: true,
            size: thermal ? 14 : 16, weight: FontWeights.Bold)));

        var paid = inv.Payments.Select(p => ($"Paid by {p.Mode}" + (p.Reference is null ? "" : $" ({p.Reference})"), Money(p.Amount))).ToList();
        if (inv.BalanceDue > 0) paid.Add(("Balance due (credit)", Money(inv.BalanceDue)));
        doc.Blocks.Add(KeyValues(paid, 0, rightAlignValues: true));

        doc.Blocks.Add(Rule(SlabTable(inv, thermal)));

        var items = inv.Lines.Count;
        var savings = inv.Lines.Sum(l => l.Mrp * l.Qty) - inv.Total;
        var note = $"{items} item(s)" + (savings > 0.5m ? $" · You saved ₹{Money(savings)} on MRP" : "");
        doc.Blocks.Add(Rule(Para(note, align: TextAlignment.Center)));
        if (s.InvoiceFooter is not null) doc.Blocks.Add(Para(s.InvoiceFooter, align: TextAlignment.Center));
        doc.Blocks.Add(Para($"Billed by {inv.CashierName}", 9, align: TextAlignment.Center));
        return doc;
    }

    private static Table ThermalLines(SaleInvoiceDto inv)
    {
        var table = NewTable(new[] { 3.4, 0.9, 1.3, 1.5 });
        var header = AddRow(table, FontWeights.Bold, "Item", "Qty", "Rate", "Amount");
        header.Cells[1].TextAlignment = header.Cells[2].TextAlignment = header.Cells[3].TextAlignment = TextAlignment.Right;

        foreach (var l in inv.Lines)
        {
            var row = AddRow(table, FontWeights.Normal, l.ItemName, Qty(l.Qty), Money(l.Rate), Money(l.Amount));
            row.Cells[1].TextAlignment = row.Cells[2].TextAlignment = row.Cells[3].TextAlignment = TextAlignment.Right;

            var parts = new List<string?> { l.Pack };
            if (l.BatchNo != "-") parts.Add($"B:{l.BatchNo}");
            if (l.ExpiryDate is { } exp) parts.Add($"Exp:{Expiry(exp)}");
            parts.Add($"MRP:{Money(l.Mrp)}");
            parts.Add($"GST:{l.GstRate:0.##}%");
            if (l.DiscountPercent > 0) parts.Add($"Disc:{l.DiscountPercent:0.##}%");
            var meta = string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
            var metaRow = new TableRow();
            metaRow.Cells.Add(new TableCell(new Paragraph(new Run(meta)) { FontSize = 8.5, Margin = new Thickness(0, 0, 0, 3) })
            { ColumnSpan = 4 });
            table.RowGroups[0].Rows.Add(metaRow);
        }
        return table;
    }

    private static Table A4Lines(SaleInvoiceDto inv)
    {
        var table = NewTable(new[] { 0.4, 2.9, 0.9, 1.2, 1.1, 0.7, 0.7, 0.9, 0.9, 0.7, 0.7, 1.1 });
        var header = AddRow(table, FontWeights.Bold, "#", "Item", "Pack", "HSN", "Batch", "Exp", "Qty", "MRP", "Rate", "Disc%", "GST%", "Amount");
        foreach (var l in inv.Lines)
        {
            var row = AddRow(table, FontWeights.Normal, l.LineNo.ToString(), l.ItemName, l.Pack ?? "", l.HsnCode ?? "",
                l.BatchNo == "-" ? "" : l.BatchNo, l.ExpiryDate is { } exp ? Expiry(exp) : "", Qty(l.Qty), Money(l.Mrp), Money(l.Rate),
                l.DiscountPercent.ToString("0.##"), l.GstRate.ToString("0.##"), Money(l.Amount));
            for (var c = 6; c < 12; c++) row.Cells[c].TextAlignment = TextAlignment.Right;
        }
        for (var c = 6; c < 12; c++) header.Cells[c].TextAlignment = TextAlignment.Right;
        return table;
    }

    private static Table SlabTable(SaleInvoiceDto inv, bool thermal)
    {
        var table = NewTable(inv.IsInterState ? new[] { 1.0, 1.6, 1.6 } : new[] { 1.0, 1.6, 1.3, 1.3 });
        table.FontSize = thermal ? 8.5 : 10;
        var header = inv.IsInterState
            ? AddRow(table, FontWeights.Bold, "GST %", "Taxable", "IGST")
            : AddRow(table, FontWeights.Bold, "GST %", "Taxable", "CGST", "SGST");
        foreach (var c in header.Cells.Skip(1)) c.TextAlignment = TextAlignment.Right;

        foreach (var slab in inv.Slabs)
        {
            var row = inv.IsInterState
                ? AddRow(table, FontWeights.Normal, slab.GstRate.ToString("0.##"), Money(slab.Taxable), Money(slab.Igst))
                : AddRow(table, FontWeights.Normal, slab.GstRate.ToString("0.##"), Money(slab.Taxable), Money(slab.Cgst), Money(slab.Sgst));
            foreach (var c in row.Cells.Skip(1)) c.TextAlignment = TextAlignment.Right;
        }
        return table;
    }

    private static Table KeyValues(IEnumerable<(string Key, string Value)> pairs, double keyWidth, bool rightAlignValues = false,
        double? size = null, FontWeight? weight = null)
    {
        var table = keyWidth > 0
            ? new Table { CellSpacing = 0, Columns = { new TableColumn { Width = new GridLength(keyWidth) }, new TableColumn() } }
            : NewTable(new[] { 2.0, 1.0 });
        if (table.RowGroups.Count == 0) table.RowGroups.Add(new TableRowGroup());
        if (size is { } fs) table.FontSize = fs;
        foreach (var (key, value) in pairs)
        {
            var row = AddRow(table, weight ?? FontWeights.Normal, key, value);
            if (rightAlignValues) row.Cells[1].TextAlignment = TextAlignment.Right;
        }
        return table;
    }

    private static Table NewTable(double[] widths)
    {
        var table = new Table { CellSpacing = 0 };
        foreach (var w in widths) table.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
        table.RowGroups.Add(new TableRowGroup());
        return table;
    }

    private static TableRow AddRow(Table table, FontWeight weight, params string[] cells)
    {
        var row = new TableRow { FontWeight = weight };
        foreach (var text in cells)
            row.Cells.Add(new TableCell(new Paragraph(new Run(text)) { Margin = new Thickness(0, 1, 2, 1) }));
        table.RowGroups[^1].Rows.Add(row);
        return row;
    }

    private static Paragraph Para(string text, double? size = null, FontWeight? weight = null,
        TextAlignment align = TextAlignment.Left)
    {
        var p = new Paragraph(new Run(text)) { TextAlignment = align, Margin = new Thickness(0, 1, 0, 1) };
        if (size is { } fs) p.FontSize = fs;
        if (weight is { } w) p.FontWeight = w;
        return p;
    }

    /// <summary>Draws a dashed-style separator above the block, like a till receipt.</summary>
    private static T Rule<T>(T block) where T : Block
    {
        block.BorderBrush = Brushes.Black;
        block.BorderThickness = new Thickness(0, 0.75, 0, 0);
        block.Padding = new Thickness(0, 3, 0, 0);
        block.Margin = new Thickness(0, 4, 0, 2);
        return block;
    }

    private static string Money(decimal value) => value.ToString("N2");

    /// <summary>An amount being taken off, without printing "-0.00" for nothing.</summary>
    private static string Minus(decimal value) => value == 0 ? Money(0) : "-" + Money(value);
    private static string Qty(decimal value) => value.ToString("0.###");

    /// <summary>MM/yy as printed on packs; invariant culture because "/" is "-" in en-IN.</summary>
    private static string Expiry(DateOnly date) => date.ToString("MM/yy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Prints any prepared document; wide documents are laid out to the printer's full page.</summary>
    public void PrintDocument(FlowDocument doc, string title, bool wide)
    {
        var dialog = new PrintDialog();
        if (FindQueue(settings.PrinterName) is { } queue) dialog.PrintQueue = queue;
        else if (dialog.ShowDialog() != true) return;

        if (wide)
        {
            doc.PageWidth = dialog.PrintableAreaWidth;
            doc.PageHeight = dialog.PrintableAreaHeight;
            doc.ColumnWidth = doc.PageWidth;
        }
        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, title);
    }

    /// <summary>Debit note for goods returned to a supplier (always A4: it goes to the distributor).</summary>
    public FlowDocument BuildDebitNote(PurchaseReturnDto r)
    {
        var s = store.Settings;
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = 11.5, Background = Brushes.White, Foreground = Brushes.Black,
            PagePadding = new Thickness(48), PageWidth = 794, ColumnWidth = 794,
        };

        doc.Blocks.Add(Para(s.StoreName, 20, FontWeights.Bold, TextAlignment.Center));
        var regs = new[] { s.Address, s.Gstin is null ? null : $"GSTIN: {s.Gstin}", s.DrugLicenseNo is null ? null : $"D.L.: {s.DrugLicenseNo}" }
            .Where(x => x is not null);
        doc.Blocks.Add(Para(string.Join("   ", regs), align: TextAlignment.Center));
        doc.Blocks.Add(Rule(Para("DEBIT NOTE — GOODS RETURNED", 14, FontWeights.Bold, TextAlignment.Center)));

        var details = new List<(string, string)>
        {
            ("Debit note no.", r.ReturnNo),
            ("Date", r.ReturnDate.ToString("dd-MM-yyyy HH:mm")),
            ("To (supplier)", r.SupplierName),
        };
        if (r.SupplierGstin is not null) details.Add(("Supplier GSTIN", r.SupplierGstin));
        if (r.PurchaseNo is not null) details.Add(("Against", $"{r.PurchaseNo} (their invoice {r.SupplierInvoiceNo})"));
        if (r.Reason is not null) details.Add(("Reason", r.Reason));
        doc.Blocks.Add(KeyValues(details, 130));

        var table = NewTable(new[] { 0.4, 3.2, 1.2, 0.8, 0.8, 1.0, 0.7, 1.0, 0.9, 1.1 });
        var header = AddRow(table, FontWeights.Bold, "#", "Item", "Batch", "Exp", "Qty", "Rate", "GST%", "Taxable", "GST", "Amount");
        for (var c = 4; c < 10; c++) header.Cells[c].TextAlignment = TextAlignment.Right;
        var n = 1;
        foreach (var l in r.Lines)
        {
            var row = AddRow(table, FontWeights.Normal, (n++).ToString(), l.ItemName, l.BatchNo,
                l.ExpiryDate is { } exp ? Expiry(exp) : "", Qty(l.Qty), l.Rate.ToString("0.00##"), l.GstRate.ToString("0.##"),
                Money(l.Taxable), Money(l.Tax), Money(l.Amount));
            for (var c = 4; c < 10; c++) row.Cells[c].TextAlignment = TextAlignment.Right;
        }
        doc.Blocks.Add(Rule(table));

        var totals = new List<(string, string)> { ("Taxable value", Money(r.Taxable)), ("GST", Money(r.Tax)) };
        if (r.RoundOff != 0) totals.Add(("Round off", r.RoundOff.ToString("+0.00;-0.00")));
        doc.Blocks.Add(Rule(KeyValues(totals, 0, rightAlignValues: true)));
        doc.Blocks.Add(Rule(KeyValues([("AMOUNT TO BE CREDITED", "₹ " + Money(r.Total))], 0, rightAlignValues: true,
            size: 16, weight: FontWeights.Bold)));

        doc.Blocks.Add(Para($"\n\nPrepared by {r.CreatedBy}\t\t\t\tReceived by (supplier's representative): ____________________", 10));
        return doc;
    }

    /// <summary>Blank document in this counter's paper format (thermal roll or A4).</summary>
    private FlowDocument NewDocument(bool forceWide = false)
    {
        var thermal = IsThermal && !forceWide;
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = thermal ? 10.5 : 11.5, Background = Brushes.White, Foreground = Brushes.Black,
            PagePadding = thermal ? new Thickness(4, 0, 4, 12) : new Thickness(48),
            PageWidth = thermal ? ThermalWidth : 794, ColumnWidth = thermal ? ThermalWidth : 794,
        };
        if (thermal) doc.PageHeight = 96 * 30;
        return doc;
    }

    private void StoreHeader(FlowDocument doc, string title, bool thermal)
    {
        var s = store.Settings;
        doc.Blocks.Add(Para(s.StoreName, thermal ? 15 : 20, FontWeights.Bold, TextAlignment.Center));
        var lines = new[] { s.Address, s.Phone is null ? null : $"Ph: {s.Phone}", s.Gstin is null ? null : $"GSTIN: {s.Gstin}" }
            .Where(x => x is not null);
        doc.Blocks.Add(Para(string.Join(thermal ? "\n" : "   ", lines), align: TextAlignment.Center));
        doc.Blocks.Add(Rule(Para(title, thermal ? 12 : 14, FontWeights.Bold, TextAlignment.Center)));
    }

    /// <summary>Payment receipt for a customer, in the counter's paper format.</summary>
    public FlowDocument BuildReceipt(ReceiptDto r)
    {
        var thermal = IsThermal;
        var doc = NewDocument();
        StoreHeader(doc, r.Status == AccountEntryStatus.Cancelled ? "*** RECEIPT CANCELLED ***" : "PAYMENT RECEIPT", thermal);
        var rows = new List<(string, string)>
        {
            ("Receipt no.", r.ReceiptNo), ("Date", r.Date.ToString("dd-MM-yyyy HH:mm")), ("Received from", r.CustomerName),
        };
        if (r.CustomerPhone is not null) rows.Add(("Phone", r.CustomerPhone));
        rows.Add(("Paid by", PaymentModeText.Of(r.Mode) + (r.Reference is null ? "" : $" ({r.Reference})")));
        if (r.Note is not null) rows.Add(("Note", r.Note));
        doc.Blocks.Add(KeyValues(rows, thermal ? 80 : 130));
        doc.Blocks.Add(Rule(KeyValues([("AMOUNT RECEIVED", "₹ " + Money(r.Amount))], 0, rightAlignValues: true,
            size: thermal ? 14 : 16, weight: FontWeights.Bold)));
        doc.Blocks.Add(KeyValues([("Balance due after this payment", Money(r.BalanceAfter))], 0, rightAlignValues: true));
        doc.Blocks.Add(Rule(Para($"Received by {r.CreatedBy}. Thank you!", 9, align: TextAlignment.Center)));
        return doc;
    }

    /// <summary>Account statement (always A4).</summary>
    public FlowDocument BuildStatement(StatementDto s, bool isCustomer)
    {
        var doc = NewDocument(forceWide: true);
        StoreHeader(doc, "STATEMENT OF ACCOUNT", thermal: false);
        var head = new List<(string, string)>
        {
            (isCustomer ? "Customer" : "Supplier", s.PartyName),
            ("Period", $"{s.From:dd-MM-yyyy} to {s.To:dd-MM-yyyy}"),
        };
        if (s.Phone is not null) head.Add(("Phone", s.Phone));
        if (s.Gstin is not null) head.Add(("GSTIN", s.Gstin));
        doc.Blocks.Add(KeyValues(head, 110));

        var table = NewTable(new[] { 1.1, 1.1, 1.4, 2.6, 1.1, 1.1, 1.2 });
        var header = AddRow(table, FontWeights.Bold, "Date", "Type", "Ref.", "Details", "Debit", "Credit", "Balance");
        for (var c = 4; c < 7; c++) header.Cells[c].TextAlignment = TextAlignment.Right;
        var opening = AddRow(table, FontWeights.SemiBold, s.From.ToString("dd-MM-yyyy"), "", "", "Opening balance", "", "", Money(s.Opening));
        opening.Cells[6].TextAlignment = TextAlignment.Right;
        foreach (var e in s.Entries)
        {
            var row = AddRow(table, FontWeights.Normal, e.Date.ToString("dd-MM-yyyy"), e.Type, e.RefNo, e.Description ?? "",
                e.Debit == 0 ? "" : Money(e.Debit), e.Credit == 0 ? "" : Money(e.Credit), Money(e.Balance));
            for (var c = 4; c < 7; c++) row.Cells[c].TextAlignment = TextAlignment.Right;
        }
        doc.Blocks.Add(Rule(table));

        var meaning = isCustomer ? (s.Closing >= 0 ? "Amount due from you" : "Amount in your favour")
                                 : (s.Closing >= 0 ? "Amount payable by us" : "Amount due to us");
        doc.Blocks.Add(Rule(KeyValues([(meaning, "₹ " + Money(Math.Abs(s.Closing)))], 0, rightAlignValues: true,
            size: 15, weight: FontWeights.Bold)));
        doc.Blocks.Add(Para($"Generated {DateTime.Now:dd-MM-yyyy HH:mm}. Please report any difference within 7 days.", 9));
        return doc;
    }

    /// <summary>Credit note for goods a customer returned, in the counter's paper format.</summary>
    public FlowDocument BuildCreditNote(CreditNoteDto n)
    {
        var thermal = IsThermal;
        var doc = NewDocument();
        StoreHeader(doc, "CREDIT NOTE (SALES RETURN)", thermal);
        var rows = new List<(string, string)>
        {
            ("Credit note", n.ReturnNo), ("Date", n.ReturnDate.ToString("dd-MM-yyyy HH:mm")),
            ("Against bill", $"{n.InvoiceNo} of {n.InvoiceDate:dd-MM-yyyy}"),
        };
        if (n.CustomerName is not null) rows.Add(("Customer", n.CustomerName));
        if (n.CustomerGstin is not null) rows.Add(("GSTIN", n.CustomerGstin));
        if (n.Reason is not null) rows.Add(("Reason", n.Reason));
        doc.Blocks.Add(KeyValues(rows, thermal ? 80 : 130));

        var table = thermal ? NewTable(new[] { 3.2, 0.9, 1.3, 1.5 }) : NewTable(new[] { 3.0, 1.1, 0.8, 0.8, 1.0, 0.7, 1.1, 1.1 });
        var header = thermal
            ? AddRow(table, FontWeights.Bold, "Item", "Qty", "Rate", "Amount")
            : AddRow(table, FontWeights.Bold, "Item", "Batch", "Exp", "Qty", "Rate", "GST%", "Taxable", "Amount");
        foreach (var c in header.Cells.Skip(thermal ? 1 : 3)) c.TextAlignment = TextAlignment.Right;
        foreach (var l in n.Lines)
        {
            var row = thermal
                ? AddRow(table, FontWeights.Normal, $"{l.ItemName} ({l.BatchNo})", Qty(l.Qty), Money(l.Rate), Money(l.Amount))
                : AddRow(table, FontWeights.Normal, l.ItemName, l.BatchNo, l.ExpiryDate is { } e ? Expiry(e) : "", Qty(l.Qty),
                    Money(l.Rate), l.GstRate.ToString("0.##"), Money(l.Taxable), Money(l.Amount));
            foreach (var c in row.Cells.Skip(thermal ? 1 : 3)) c.TextAlignment = TextAlignment.Right;
        }
        doc.Blocks.Add(Rule(table));

        var totals = new List<(string, string)> { ("Taxable value", Money(n.Taxable)), (n.IsInterState ? "IGST" : "CGST + SGST", Money(n.Tax)) };
        if (n.RoundOff != 0) totals.Add(("Round off", n.RoundOff.ToString("+0.00;-0.00")));
        doc.Blocks.Add(Rule(KeyValues(totals, 0, rightAlignValues: true)));
        var settled = n.RefundMode == RefundMode.Cash ? "REFUNDED IN CASH" : "CREDITED TO ACCOUNT";
        doc.Blocks.Add(Rule(KeyValues([(settled, "₹ " + Money(n.Total))], 0, rightAlignValues: true,
            size: thermal ? 13 : 15, weight: FontWeights.Bold)));
        doc.Blocks.Add(Para($"Processed by {n.CreatedBy}", 9, align: TextAlignment.Center));
        return doc;
    }

    /// <summary>Counter closing slip (Z-report) in the counter's paper format.</summary>
    public FlowDocument BuildDayEnd(DayEndSummaryDto s)
    {
        var thermal = IsThermal;
        var doc = NewDocument();
        StoreHeader(doc, s.Closing is null ? "DAY SUMMARY (NOT CLOSED)" : "COUNTER CLOSING", thermal);
        doc.Blocks.Add(KeyValues([("Date", s.Date.ToString("dd-MM-yyyy")), ("Counter / cashier", s.UserName),
            ("Bills", s.Bills.ToString()), ("Cancelled bills", s.CancelledBills.ToString())], thermal ? 110 : 160));

        doc.Blocks.Add(Rule(Para("Sales by payment mode", weight: FontWeights.Bold)));
        doc.Blocks.Add(KeyValues([("Cash", Money(s.CashSales)), ("UPI", Money(s.UpiSales)), ("Card", Money(s.CardSales)),
            ("Credit (on account)", Money(s.CreditSales)), ("Total sales", Money(s.SalesTotal))], 0, rightAlignValues: true));

        doc.Blocks.Add(Rule(Para("Cash drawer", weight: FontWeights.Bold)));
        var cash = new List<(string, string)>
        {
            ("Cash sales", Money(s.CashSales)), ("Less cash refunds", Minus(s.CashRefunds)),
            ("Add cash received on account", Money(s.CashReceipts)), ("Less cash paid to suppliers", Minus(s.CashPayouts)),
            ("Net cash taken", Money(s.NetCash)),
        };
        if (s.Closing is { } c)
        {
            cash.Add(("Opening float", Money(c.OpeningFloat)));
            cash.Add(("Expected in drawer", Money(c.ExpectedCash)));
            cash.Add(("Counted", Money(c.CountedCash)));
        }
        doc.Blocks.Add(KeyValues(cash, 0, rightAlignValues: true));
        if (s.Closing is { } closed)
        {
            var label = closed.Difference == 0 ? "CASH MATCHES" : closed.Difference < 0 ? "SHORT" : "OVER";
            doc.Blocks.Add(Rule(KeyValues([(label, Money(Math.Abs(closed.Difference)))], 0, rightAlignValues: true,
                size: thermal ? 13 : 15, weight: FontWeights.Bold)));
            if (closed.Denominations is not null)
                doc.Blocks.Add(Para("Notes/coins: " + closed.Denominations.Replace(",", ", ").Replace("x", " × "), 9));
            if (closed.Note is not null) doc.Blocks.Add(Para("Note: " + closed.Note, 9));
            doc.Blocks.Add(Para($"Closed {closed.ClosedAt:dd-MM-yyyy HH:mm}", 9, align: TextAlignment.Center));
        }
        doc.Blocks.Add(Rule(KeyValues([("Returns", s.Returns.ToString()), ("UPI/card/cheque received on account", Money(s.OtherReceipts))],
            0, rightAlignValues: true)));
        doc.Blocks.Add(Para("\nCashier signature ____________     Manager ____________", 9));
        return doc;
    }

    /// <summary>Fake bill for previewing the layout from Settings.</summary>
    public static SaleInvoiceDto Sample()
    {
        var totals = BillCalculator.Calculate(
            [new BillLineInput(2, 31, 0, 5), new BillLineInput(1, 118, 10, 18)], interState: false);
        return new SaleInvoiceDto
        {
            InvoiceNo = "S2627-000123", InvoiceDate = DateTime.Now, CustomerName = "Walk-in customer",
            PatientName = "R. Sharma", DoctorName = "Dr. A. Mehta", CashierName = "Sample cashier",
            Gross = totals.Gross, Discount = totals.Discount, Taxable = totals.Taxable, Cgst = totals.Cgst,
            Sgst = totals.Sgst, RoundOff = totals.RoundOff, Total = totals.Total, Slabs = [.. totals.Slabs],
            Payments = [new PaymentDto(PaymentMode.Cash, totals.Total, null)],
            Lines =
            [
                new SaleLineDto { LineNo = 1, ItemName = "Paracetamol 650 Tab", Pack = "15 TAB", HsnCode = "30049099",
                    BatchNo = "PC24A", ExpiryDate = new DateOnly(2027, 3, 31), Mrp = 33.5m, Qty = 2, Rate = 31, GstRate = 5,
                    Amount = totals.Lines[0].Amount },
                new SaleLineDto { LineNo = 2, ItemName = "Vitamin C Chewable", Pack = "30 TAB", HsnCode = "21069099",
                    BatchNo = "VC771", ExpiryDate = new DateOnly(2027, 8, 31), Mrp = 130, Qty = 1, Rate = 118, GstRate = 18,
                    DiscountPercent = 10, Amount = totals.Lines[1].Amount },
            ],
        };
    }
}
