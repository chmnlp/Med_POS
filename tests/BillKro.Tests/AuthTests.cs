using System.Net;
using BillKro.Server.Endpoints;
using BillKro.Shared;

namespace BillKro.Tests;

[Collection(PosApiCollection.Name)]
public class AuthTests(PosApiFixture api)
{
    [Fact]
    public async Task Health_is_anonymous()
    {
        var res = await api.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Api_requires_a_token()
    {
        var res = await api.CreateClient().GetAsync("/api/items");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task New_user_must_change_password_before_using_the_api()
    {
        var username = PosApiFixture.Unique("new");
        (await api.Admin.PostJson("/api/users", new UserCreateRequest(username, "New User", UserRole.Cashier, "Temp#1234")))
            .EnsureSuccessStatusCode();

        var login = await api.LoginAsync(api.CreateClient(), username, "Temp#1234");
        Assert.True(login.MustChangePassword);

        var pending = api.ClientWithToken(login.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.GetAsync("/api/items")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await pending.GetAsync("/api/auth/me")).StatusCode);

        var full = await api.ChangePasswordAsync(login, "Temp#1234", "Changed#1234");
        Assert.Equal(HttpStatusCode.OK, (await full.GetAsync("/api/items")).StatusCode);

        // The pre-change token is revoked by the security stamp rotation.
        Assert.Equal(HttpStatusCode.Unauthorized, (await pending.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Change_password_rejects_short_or_wrong_current_password()
    {
        var (client, _) = await api.CreateUserClientAsync(UserRole.Cashier);
        var res = await client.PostJson("/api/auth/change-password", new ChangePasswordRequest("wrong", "abc"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("CurrentPassword", body);
        Assert.Contains("NewPassword", body);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account()
    {
        var (_, user) = await api.CreateUserClientAsync(UserRole.Cashier);
        var client = api.CreateClient();

        for (var i = 0; i < AuthEndpoints.MaxFailedLogins; i++)
            await client.PostJson("/api/auth/login", new LoginRequest(user.Username, "bad-password"));

        var res = await client.PostJson("/api/auth/login", new LoginRequest(user.Username, PosApiFixture.UserPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Contains("locked", await res.Content.ReadAsStringAsync());

        // Admin reset clears the lockout.
        (await api.Admin.PostJson($"/api/users/{user.Id}/reset-password", new ResetPasswordRequest("Reset#1234")))
            .EnsureSuccessStatusCode();
        var login = await api.LoginAsync(client, user.Username, "Reset#1234");
        Assert.True(login.MustChangePassword);
    }

    [Fact]
    public async Task Cashier_permissions()
    {
        var (cashier, _) = await api.CreateUserClientAsync(UserRole.Cashier);

        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PostJson("/api/items", new ItemUpsertRequest { Name = "X", Unit = "PCS" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PostJson("/api/suppliers", new SupplierUpsertRequest { Name = "X" })).StatusCode);

        var created = await cashier.PostJson("/api/customers", new CustomerUpsertRequest { Name = "Walk-in Ravi" });
        var customer = await created.Read<CustomerDto>();
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.DeleteAsync($"/api/customers/{customer.Id}")).StatusCode);
    }

    [Fact]
    public async Task Manager_can_manage_items_but_not_users()
    {
        var (manager, _) = await api.CreateUserClientAsync(UserRole.Manager);
        var res = await manager.PostJson("/api/items", new ItemUpsertRequest { Name = PosApiFixture.Unique("Item"), Unit = "PCS" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_session_is_revoked()
    {
        var (client, user) = await api.CreateUserClientAsync(UserRole.Cashier);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/items")).StatusCode);

        (await api.Admin.PutJson($"/api/users/{user.Id}", new UserUpdateRequest(user.FullName, user.Role, IsActive: false)))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/items")).StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_lock_themselves_out()
    {
        var me = await api.Admin.GetJson<UserDto>("/api/auth/me");
        var res = await api.Admin.PutJson($"/api/users/{me.Id}", new UserUpdateRequest(me.FullName, UserRole.Cashier, true));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }
}
