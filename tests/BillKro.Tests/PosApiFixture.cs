using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillKro.Data;
using BillKro.Server.Infrastructure;
using BillKro.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillKro.Tests;

/// <summary>
/// Runs the real server against a throwaway SQL Server LocalDB database
/// (override with BILLKRO_TEST_SQL). Shared by all tests in the collection.
/// </summary>
public sealed class PosApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminPassword = "Admin#2026";
    public const string UserPassword = "Secret#2026";

    private readonly string _connectionString = Environment.GetEnvironmentVariable("BILLKRO_TEST_SQL")
        ?? $@"Server=(localdb)\MSSQLLocalDB;Database=BillKroTests_{Guid.NewGuid():N};Integrated Security=True;TrustServerCertificate=True;";

    public HttpClient Admin { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Pos", _connectionString);
        // Tests send to MARG explicitly; the background sender would race them.
        builder.UseSetting("ErpLink:Background", "false");
        builder.UseSetting("Jwt:Key", "integration-test-signing-key-at-least-32-bytes!");
    }

    public async Task InitializeAsync()
    {
        var client = CreateClient();
        var login = await LoginAsync(client, DbSeeder.DefaultAdminUsername, DbSeeder.DefaultAdminPassword);
        if (!login.MustChangePassword)
            throw new InvalidOperationException("Seeded admin should be forced to change password.");

        Admin = await ChangePasswordAsync(login, DbSeeder.DefaultAdminPassword, AdminPassword);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PosDbContext>().Database.EnsureDeletedAsync();
        await DisposeAsync();
    }

    public async Task<LoginResponse> LoginAsync(HttpClient client, string username, string password)
    {
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password), PosJson.Options);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Login failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<LoginResponse>(PosJson.Options))!;
    }

    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<HttpClient> ChangePasswordAsync(LoginResponse login, string current, string next)
    {
        var client = ClientWithToken(login.Token);
        var res = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(current, next), PosJson.Options);
        res.EnsureSuccessStatusCode();
        var fresh = (await res.Content.ReadFromJsonAsync<LoginResponse>(PosJson.Options))!;
        return ClientWithToken(fresh.Token);
    }

    /// <summary>Creates a user with the given role and returns a signed-in client with a full session.</summary>
    public async Task<(HttpClient Client, UserDto User)> CreateUserClientAsync(UserRole role)
    {
        var username = $"{role.ToString().ToLowerInvariant()}{Guid.NewGuid():N}"[..20];
        var res = await Admin.PostAsJsonAsync("/api/users", new UserCreateRequest(username, $"Test {role}", role, "Temp#1234"), PosJson.Options);
        res.EnsureSuccessStatusCode();
        var user = (await res.Content.ReadFromJsonAsync<UserDto>(PosJson.Options))!;

        var login = await LoginAsync(CreateClient(), username, "Temp#1234");
        return (await ChangePasswordAsync(login, "Temp#1234", UserPassword), user);
    }

    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];
}

[CollectionDefinition(Name)]
public class PosApiCollection : ICollectionFixture<PosApiFixture>
{
    public const string Name = "PosApi";
}

public static class HttpExtensions
{
    public static Task<HttpResponseMessage> PostJson<T>(this HttpClient c, string url, T body) =>
        c.PostAsJsonAsync(url, body, PosJson.Options);

    public static Task<HttpResponseMessage> PutJson<T>(this HttpClient c, string url, T body) =>
        c.PutAsJsonAsync(url, body, PosJson.Options);

    public static async Task<T> Read<T>(this HttpResponseMessage res)
    {
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>(PosJson.Options))!;
    }

    public static async Task<T> GetJson<T>(this HttpClient c, string url) => await (await c.GetAsync(url)).Read<T>();
}
