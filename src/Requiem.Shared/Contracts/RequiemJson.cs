using System.Text.Json;
using System.Text.Json.Serialization;

namespace Requiem.Shared.Contracts;

/// <summary>JSON settings shared by the API and the browser client, so enums travel as names.</summary>
public static class RequiemJson
{
    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
