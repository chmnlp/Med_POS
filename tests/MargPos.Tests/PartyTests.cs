using System.Net;
using MargPos.Shared;

namespace MargPos.Tests;

[Collection(PosApiCollection.Name)]
public class PartyTests(PosApiFixture api)
{
    [Fact]
    public async Task Customer_validation()
    {
        var res = await api.Admin.PostJson("/api/customers", new CustomerUpsertRequest
        {
            Name = "Bad", Gstin = "123", Phone = "12345", Email = "nope", CreditLimit = -1,
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        foreach (var field in new[] { "Gstin", "Phone", "Email", "CreditLimit" })
            Assert.Contains(field, body);
    }

    [Fact]
    public async Task Customer_create_search_and_update()
    {
        var phone = "98" + Random.Shared.Next(10_000_000, 99_999_999);
        var customer = await (await api.Admin.PostJson("/api/customers", new CustomerUpsertRequest
        {
            Name = "City Hospital", Phone = phone, Gstin = "27abcde1234f1z5", Type = CustomerType.Hospital, CreditLimit = 50000,
        })).Read<CustomerDto>();

        Assert.StartsWith("C", customer.Code);
        Assert.Equal("27ABCDE1234F1Z5", customer.Gstin);
        Assert.Equal(CustomerType.Hospital, customer.Type);

        var found = await api.Admin.GetJson<PagedResult<CustomerDto>>($"/api/customers?q={phone}");
        Assert.Equal(customer.Id, Assert.Single(found.Items).Id);

        var updated = await (await api.Admin.PutJson($"/api/customers/{customer.Id}", new CustomerUpsertRequest
        {
            Name = "City Hospital Pvt Ltd", Phone = phone, Type = CustomerType.Hospital,
        })).Read<CustomerDto>();
        Assert.Equal(customer.Code, updated.Code);
        Assert.Null(updated.Gstin);
    }

    [Fact]
    public async Task Supplier_codes_must_be_unique()
    {
        var code = PosApiFixture.Unique("SUP").ToUpperInvariant();
        (await api.Admin.PostJson("/api/suppliers", new SupplierUpsertRequest { Code = code, Name = "Alpha Pharma" }))
            .EnsureSuccessStatusCode();
        var dup = await api.Admin.PostJson("/api/suppliers", new SupplierUpsertRequest { Code = code, Name = "Beta" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }
}
