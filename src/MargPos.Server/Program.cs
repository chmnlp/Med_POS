using MargPos.Data;
using MargPos.Server.Auth;
using MargPos.Server.Endpoints;
using MargPos.Server.Infrastructure;
using MargPos.Shared;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWindowsService(o => o.ServiceName = "MargPos Server");

var connStr = builder.Configuration.GetConnectionString("Pos")
    ?? throw new InvalidOperationException("ConnectionStrings:Pos is required.");
builder.Services.AddDbContext<PosDbContext>(o => o.UseSqlServer(connStr, PosDbContextOptions.ConfigureSqlServer));

builder.Services.AddPosAuth(JwtSettings.Load(builder.Configuration));
builder.Services.AddProblemDetails();
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

await DbSeeder.InitialiseAsync(app.Services);
await app.RunAsync();

public partial class Program;
