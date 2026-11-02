# BillKro Rx + BillKro Sync

Point-of-sale for pharmacy / FMCG shops that works alongside MARG ERP.

| Project | Role |
|---|---|
| `src/BillKro.Server` | API + database host. Runs on the shop's server PC as a Windows service. |
| `src/BillKro.Desktop` | WPF billing/back-office app for each counter PC. |
| `src/BillKro.Data` / `BillKro.Shared` | EF Core model (`pos` schema) and API contracts. |
| `src/BillKro.Sync.*` | MARG ↔ SQL Server CSV sync service (to be connected to the POS in a later phase). |
| `tests/BillKro.Tests` | API integration tests (run against SQL Server LocalDB) and unit tests. |

## Shop setup

**Server PC** (needs SQL Server or SQL Server Express):

1. Set the connection string in `BillKro.Server/appsettings.json` → `ConnectionStrings:Pos`.
2. Publish and install as a service (from an elevated prompt):
   ```
   dotnet publish src/BillKro.Server -c Release -o C:\BillKro\Server
   sc.exe create "BillKro Server" binPath= "C:\BillKro\Server\BillKro.Server.exe" start= auto
   sc.exe start "BillKro Server"
   ```
   The service account needs access to SQL Server. The database and tables are created on first start.
3. Allow counters to connect: `netsh advfirewall firewall add rule name="BillKro" dir=in action=allow protocol=TCP localport=5080`

**Each counter PC:** run `BillKro.Desktop.exe`, open **Server** on the sign-in screen and enter `http://SERVER-PC-NAME:5080`.

**First sign-in:** `admin` / `admin@123` — you must choose a new password immediately. Then create Manager and Cashier accounts under **Users**.

**Before the first sale:** an admin fills in store details under **Settings** (name, GSTIN, drug licence, invoice footer). On each counter, pick the invoice printer and paper (80 mm thermal or A4) under **Settings → This counter**; use *Preview sample bill* to check the layout.

## Billing keys

F2 new bill · F3 item search · F4 quantity · F6 customer · F8 hold · F9 resume held bill · F12 pay · Del remove line.
Type `3*` before a barcode to add 3. Quantities are taken from the nearest-expiry batches first and split across batches automatically.

## Stock

- **Purchases → Receive goods:** choose the supplier, then type lines (item search, batch, expiry MM/YY, qty, free, rate excl. GST) or **Import EDE file…** to load the distributor's H/T/F bill. Lines whose distributor code isn't recognised are shown in red — *Edit / match line*, pick your item, *Add line*. The mapping is remembered for that supplier. Free goods lower the landed cost, which becomes the batch's purchase rate.
- **Lots and prices:** stock, MRP, retail / wholesale / hospital rates and landed cost are kept per lot. Receiving a batch number with a *different MRP* creates a new lot (PC24A/2) so stock already on the shelf keeps its price; the same MRP tops up the lot and its cost becomes the weighted average. Give items a retail / wholesale margin % and purchase entry suggests the selling rate (cost × (1 + margin) × (1 + GST), capped at MRP).
- **Supplier returns:** raise a debit note for expired / damaged goods (*Load expiry returns* lists stock from that supplier that is expired or near expiry) or against a specific purchase (Purchases → Return to supplier). Stock and the supplier balance go down; the debit note prints on A4.
- **Items & Stock → Adjust stock:** damage, expiry write-off, loss, or *physical count correction* (enter the quantity counted; the difference is posted). **Stock history** shows every movement with a running balance.
- **Stock reports:** expiry (with already-expired), reorder suggestions with last supplier and rate, stock valuation at cost and MRP, and **profit** by day or item (sales ex GST − lot cost recorded at sale, net of returns). All export to CSV for Excel.

## Accounts

- **Accounts → Customers owe us:** what each customer owes, aged by how overdue their unpaid bills are (payments clear the oldest bills first; due = bill date + the customer's credit days). *Receive payment* (cash / UPI / card / cheque / bank transfer) prints a receipt; *Statement* shows and prints the ledger for any period; *Copy reminder* puts a polite payment reminder on the clipboard for WhatsApp/SMS. Overdue totals also show on the Dashboard.
- **Accounts → We owe suppliers** (managers): payables with the same ageing, *Pay supplier* and supplier statements.
- Receipts and supplier payments can be cancelled by a manager with a reason; the amount goes back on the account. Credit sales are checked against the credit limit *after* receipts, so paying frees up credit.
- **Sales history → Credit notes** reprints the credit note for a sales return (it is also shown right after the return).

## Day end and reports

- **Day end** (every user): your counter's bills, sales by mode (cash / UPI / card / credit), returns and cash received or paid out. Enter the opening float and count the drawer by notes and coins; the app shows the expected cash and any shortage or excess. *Close my counter* records it (once per user per day) and prints a closing slip. Managers can view all counters together and see everyone's closings.
- **Sales & GST** (managers): sales summary for any period (by mode, day, cashier, top items) and GST working figures for GSTR-1 — B2B invoice-wise, B2C rate-wise, HSN summary, credit notes, document ranges — plus purchase tax (input credit) with debit notes. *Export CSV files…* writes each section to a folder for your accountant or the GST offline tool.
- **Dashboard:** today's and this month's sales; managers also see gross profit.

## Data Exchange

MARG stays the accounting system. The **Data Exchange** page (managers) exchanges files with it:

- **Purchases → MARG (automatic).** Each purchase is written once as an EDE file (`POS_<purchase no>.csv`) into MARG's `Method\` folder; import it in MARG from the purchase screen like a distributor's file. A log stops any purchase being sent twice, even if MARG deletes the file after import. *Send now* sends at once.
  - Admin settings: switch on, the Method folder **as seen from the server PC** (use a share such as `\\ERP-PC\ERP\Method`; mapped drive letters do not work for a service), the company code written in the file, and the date to send from (pick the day you start entering purchases in the POS, so older bills already in MARG are not sent again). *Test folder* checks the server can write there.
  - The server runs as a Windows service; give its account (e.g. run the service as a shop user, or grant the server PC's computer account) change rights on that share.
- **Enter in MARG** lists what MARG cannot import from a file: debit notes (supplier returns) and purchases cancelled in the POS after they were sent. Enter each in MARG, then *Mark done*.
- **Sales to MARG.** MARG has no known sales import format, so *Export sales register* gives a CSV of every item sold (cancelled bills left out) for keying in or checking against MARG.
- **Import masters from MARG.** Export masters from MARG (the C/H/T file, like `master.csv`) and choose it: the preview shows items and parties first. New items are created with MARG's stock as opening stock (one lot per batch; a second MRP for the same batch becomes `BATCH/2`). Items already in the POS (same code) only get name and prices refreshed — stock is never changed by a re-import. Parties can be imported as customers or suppliers, optionally only one MARG party type; existing parties (same name) keep their balances.
- POS masters are not sent to MARG: several fields of MARG's master file are not decoded yet.

## Roles

| | Cashier | Manager | Admin |
|---|:-:|:-:|:-:|
| Billing, returns, held bills, add/edit customers | ✓ | ✓ | ✓ |
| Change rates on a bill; discounts above the store limit | | ✓ | ✓ |
| Cancel invoices; items, batches, suppliers; deactivate customers | | ✓ | ✓ |
| Purchases, supplier returns, stock adjustments, stock & profit reports | | ✓ | ✓ |
| Receive customer payments, statements, reminders | ✓ | ✓ | ✓ |
| Pay suppliers, payables, cancel receipts / payments | | ✓ | ✓ |
| Close own counter (day end) | ✓ | ✓ | ✓ |
| All counters' day end, sales & GST reports, profit on dashboard | | ✓ | ✓ |
| Data Exchange: send purchases, import masters, sales register | | ✓ | ✓ |
| Users, store details, Data Exchange settings | | | ✓ |

## Development

```
dotnet build BillKro.Sync.slnx
dotnet test tests/BillKro.Tests          # needs SQL Server LocalDB, or set BILLKRO_TEST_SQL
dotnet run --project src/BillKro.Server  # http://localhost:5080
dotnet run --project src/BillKro.Desktop
```

New migration: `dotnet ef migrations add <Name> --project src/BillKro.Data --startup-project src/BillKro.Data`

## Files written at runtime

- Server signing key: `%ProgramData%\BillKro\jwt.key` (unique per install; deleting it signs everyone out).
- Client settings / error log: `%LocalAppData%\BillKro\client.json`, `%LocalAppData%\BillKro\logs\`.

## Known limits (current phase)

- Traffic between counters and server is plain HTTP on the shop LAN. Enable HTTPS before exposing the server beyond the LAN.
- EDE files: only the first bill in a multi-bill file is imported. The H-record field layout is inferred from MARG's demo file; always check the invoice no./date the import fills in.
- An opening balance is treated as a bill dated the day the customer / supplier was created, so it ages from then.
- Receipts are applied to the oldest bills automatically; there is no manual bill-by-bill allocation.
- The GST report gives working figures and CSVs, not the GST portal JSON upload file; check figures before filing. B2C supplies are reported at the store's state (walk-in buyers have no GSTIN, so no B2C-large).
- A counter can be closed once per day; it cannot be reopened (bills made after closing still appear in reports).
- Profit for sales made before this version uses the lot cost at upgrade time (cost was not recorded per sale before).
- Lots exist only for batch-tracked items; untracked items keep one stock record whose price updates on each purchase.
- Rates are GST-inclusive (MRP-style retail pricing).
- Data Exchange: the EDE and master file layouts are inferred from MARG's demo files. Before switching automatic sending on, send one purchase and check it imports correctly in MARG; check a master import's preview (MRP, stock, GST) before importing.
