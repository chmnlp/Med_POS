using BillKro.Data;
using BillKro.Server.Auth;
using BillKro.Server.Endpoints;
using BillKro.Server.Infrastructure;
using BillKro.Shared;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWindowsService(o => o.ServiceName = "BillKro Server");

var connStr = builder.Configuration.GetConnectionString("Pos")
    ?? throw new InvalidOperationException("ConnectionStrings:Pos is required.");
builder.Services.AddDbContext<PosDbContext>(o => o.UseSqlServer(connStr, PosDbContextOptions.ConfigureSqlServer));

builder.Services.AddPosAuth(JwtSettings.Load(builder.Configuration));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddScoped<ErpLinkService>();
builder.Services.AddHostedService<ErpLinkWorker>();
builder.Services.ConfigureHttpJsonOptions(o => PosJson.Configure(o.SerializerOptions));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuth();
app.MapUsers();
app.MapItems();
app.MapParties();
app.MapDashboard();
app.MapSales();
app.MapSettings();
app.MapPurchases();
app.MapStock();
app.MapPurchaseReturns();
app.MapAccounts();
app.MapReports();
app.MapErp();

await DbSeeder.InitialiseAsync(app.Services);
await app.RunAsync();

public partial class Program;
