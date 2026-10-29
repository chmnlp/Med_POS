using System.Text.Json;
using System.Text.Json.Serialization;

namespace MargPos.Shared;

/// <summary>JSON settings shared by server and client so enums travel as names.</summary>
public static class PosJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions o)
    {
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
