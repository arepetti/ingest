using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ingest.IntegrationTests.Fixtures;

/// <summary>JSON helpers shared by the integration tests: a single options instance plus thin
/// request/response extension methods over <see cref="HttpClient"/>.</summary>
public static class Json
{
    /// <summary>Mirrors the API's wire format: camelCase, case-insensitive reads, string enums.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>POST <paramref name="body"/> as JSON.</summary>
    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, Options);

    /// <summary>PUT <paramref name="body"/> as JSON.</summary>
    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, Options);

    /// <summary>Read the response body as <typeparamref name="T"/>, throwing on a non-success status.</summary>
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        await response.EnsureOkAsync();
        var value = await response.Content.ReadFromJsonAsync<T>(Options);
        return value ?? throw new InvalidOperationException($"Response body deserialised to null ({typeof(T).Name}).");
    }

    /// <summary>Read the response body as a <see cref="JsonElement"/> without checking status.</summary>
    public static async Task<JsonElement> ReadJsonBodyAsync(this HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<JsonElement>(Options);
        return value;
    }

    /// <summary>Read the response body as a <see cref="JsonElement"/>, throwing on a non-success status.</summary>
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        await response.EnsureOkAsync();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Options);
    }

    /// <summary>Like <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> but includes the body in the failure message.</summary>
    public static async Task EnsureOkAsync(this HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Expected success but got {(int)response.StatusCode} {response.StatusCode} from {response.RequestMessage?.RequestUri}. Body: {body}");
    }
}
