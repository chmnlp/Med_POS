# MargPOS + MargSync

Point-of-sale for pharmacy / FMCG shops that works alongside MARG ERP.

| Project | Role |
|---|---|
| `src/MargPos.Server` | API + database host. Runs on the shop's server PC as a Windows service. |
| `src/MargPos.Desktop` | WPF billing/back-office app for each counter PC. |
| `src/MargPos.Data` / `MargPos.Shared` | EF Core model (`pos` schema) and API contracts. |
| `src/MargSync.*` | MARG ↔ SQL Server CSV sync service (to be connected to the POS in a later phase). |
| `tests/MargPos.Tests` | API integration tests (run against SQL Server LocalDB) and unit tests. |

## Shop setup

**Server PC** (needs SQL Server or SQL Server Express):

1. Set the connection string in `MargPos.Server/appsettings.json` → `ConnectionStrings:Pos`.
2. Publish and install as a service (from an elevated prompt):
   ```
   dotnet publish src/MargPos.Server -c Release -o C:\MargPos\Server
   sc.exe create "MargPos Server" binPath= "C:\MargPos\Server\MargPos.Server.exe" start= auto
   sc.exe start "MargPos Server"
   ```
   The service account needs access to SQL Server. The database and tables are created on first start.
3. Allow counters to connect: `netsh advfirewall firewall add rule name="MargPos" dir=in action=allow protocol=TCP localport=5080`

**Each counter PC:** run `MargPos.Desktop.exe`, open **Server** on the sign-in screen and enter `http://SERVER-PC-NAME:5080`.

**First sign-in:** `admin` / `admin@123` — you must choose a new password immediately. Then create Manager and Cashier accounts under **Users**.

## Roles

| | Cashier | Manager | Admin |
|---|:-:|:-:|:-:|
| View items & stock, add/edit customers | ✓ | ✓ | ✓ |
| Items, batches, suppliers, deactivate customers | | ✓ | ✓ |
| Users | | | ✓ |

## Development

```
dotnet build MargSync.slnx
dotnet test tests/MargPos.Tests          # needs SQL Server LocalDB, or set MARGPOS_TEST_SQL
dotnet run --project src/MargPos.Server  # http://localhost:5080
dotnet run --project src/MargPos.Desktop
```

New migration: `dotnet ef migrations add <Name> --project src/MargPos.Data --startup-project src/MargPos.Data`

## Files written at runtime

- Server signing key: `%ProgramData%\MargPos\jwt.key` (unique per install; deleting it signs everyone out).
- Client settings / error log: `%LocalAppData%\MargPos\client.json`, `%LocalAppData%\MargPos\logs\`.

## Known limits (current phase)

- Traffic between counters and server is plain HTTP on the shop LAN. Enable HTTPS before exposing the server beyond the LAN.
- Stock quantities can only be set when an item or batch is created; billing, purchases and stock adjustments come next.
