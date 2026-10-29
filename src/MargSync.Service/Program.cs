using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MargSync.Core.Configuration;
using MargSync.Core.Interfaces;
using MargSync.Core.Models;
using MargSync.Data;
using MargSync.Data.Repositories;
using MargSync.Parser;
using MargSync.Parser.Exporters;
using MargSync.Service;

var builder = Host.CreateApplicationBuilder(args);

// Windows Service support
builder.Services.AddWindowsService(o => o.ServiceName = "MargSync");

// Configuration
var syncSection = builder.Configuration.GetSection(SyncOptions.Section);
builder.Services.Configure<SyncOptions>(syncSection);

// EF Core / SQL Server
var connStr = builder.Configuration["Sync:SqlConnectionString"]
    ?? throw new InvalidOperationException("Sync:SqlConnectionString is required.");
builder.Services.AddDbContext<MargSyncDbContext>(o =>
    o.UseSqlServer(connStr, sql => sql.EnableRetryOnFailure()));

// Repositories
builder.Services.AddScoped<ISyncLogRepository, SyncLogRepository>();
builder.Services.AddScoped<IRepository<Party>, PartyRepository>();
builder.Services.AddScoped<IRepository<Item>, ItemRepository>();
builder.Services.AddScoped<IRepository<PurchaseBill>, PurchaseBillRepository>();
builder.Services.AddScoped<IRepository<SalesInvoice>, SalesInvoiceRepository>();
builder.Services.AddScoped<IRepository<StockEntry>, StockEntryRepository>();
builder.Services.AddScoped<IRepository<LedgerEntry>, LedgerEntryRepository>();
builder.Services.AddScoped<IRepository<GstEntry>, GstEntryRepository>();

// Parsers
builder.Services.AddScoped<EdeParser>();
builder.Services.AddScoped<MasterDataParser>();
builder.Services.AddScoped<SalesExportParser>();
builder.Services.AddScoped<StockExportParser>();
builder.Services.AddScoped<LedgerExportParser>();
builder.Services.AddScoped<GstExportParser>();

// Exporters (write-back to MARG)
builder.Services.AddScoped<EdeExporter>();
builder.Services.AddScoped<MasterDataExporter>();

// Orchestrator + hosted service
builder.Services.AddScoped<SyncOrchestrator>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Auto-migrate on startup
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MargSyncDbContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();
