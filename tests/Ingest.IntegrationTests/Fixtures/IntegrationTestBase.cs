using System.Net.Http.Json;
using Ingest.Api.Models;

namespace Ingest.IntegrationTests.Fixtures;

/// <summary>
/// Base class for the integration tests. Provides an admin-authenticated client and a handful of
/// seeding helpers (service accounts, schemas, submissions) that drive everything through the real
/// HTTP API so tests read like a client would use the system.
/// </summary>
[Collection(IngestCollection.Name)]
public abstract class IntegrationTestBase
{
    /// <summary>The shared app + Mongo fixture.</summary>
    protected IngestAppFixture Fixture { get; }

    /// <summary>A client authenticated as the bootstrap admin.</summary>
    protected HttpClient Admin { get; }

    /// <summary>Create the base, capturing the shared fixture and an admin client.</summary>
    protected IntegrationTestBase(IngestAppFixture fixture)
    {
        Fixture = fixture;
        Admin = fixture.CreateAdminClient();
    }

    /// <summary>A short unique suffix so concurrently-defined names never collide.</summary>
    protected static string Unique() => Guid.NewGuid().ToString("N")[..10];

    /// <summary>The admin account's own id (read from <c>GET /api/me</c>).</summary>
    protected async Task<Guid> AdminAccountIdAsync()
    {
        var me = await (await Admin.GetAsync("/api/me")).ReadJsonAsync();
        return me.GetProperty("id").GetGuid();
    }

    /// <summary>Create an enabled Service-role application account and mint an API key for it.</summary>
    protected async Task<(Guid AccountId, string ApiKey, string Name)> CreateServiceAccountAsync(string? email = null)
    {
        var name = $"svc-{Unique()}";
        var account = await (await Admin.PostJsonAsync("/api/admin/accounts", new
        {
            name,
            label = name,
            email,
            kind = "Application",
            role = "Service",
            enabled = true,
        })).ReadAsync<AccountDto>();

        var key = await (await Admin.PostJsonAsync($"/api/admin/accounts/{account.Id}/keys", new { })).ReadAsync<GeneratedApiKeyResponse>();
        return (account.Id, key.Plaintext, name);
    }

    /// <summary>
    /// Create an enabled Operator (back-office reader) account and mint an API key for it. When
    /// <paramref name="assignedServiceIds"/> is supplied and non-empty the operator is confined to
    /// those services; otherwise it is unrestricted (sees every service).
    /// </summary>
    protected async Task<(Guid AccountId, string ApiKey, string Name)> CreateOperatorAsync(IEnumerable<Guid>? assignedServiceIds = null)
    {
        var name = $"op-{Unique()}";
        var account = await (await Admin.PostJsonAsync("/api/admin/accounts", new
        {
            name,
            label = name,
            email = $"{name}@example.com",
            kind = "User",
            role = "Operator",
            enabled = true,
            assignedServiceIds = (assignedServiceIds ?? Array.Empty<Guid>()).ToArray(),
        })).ReadAsync<AccountDto>();

        var key = await (await Admin.PostJsonAsync($"/api/admin/accounts/{account.Id}/keys", new { })).ReadAsync<GeneratedApiKeyResponse>();
        return (account.Id, key.Plaintext, name);
    }

    /// <summary>Create a global, enabled schema with one numeric monthly value and optional RAG bands / approval.</summary>
    protected async Task<SchemaDto> CreateSchemaAsync(
        bool withBands = false,
        object? approval = null,
        string valueName = "count")
    {
        var name = $"schema_{Unique()}";
        var value = new Dictionary<string, object?>
        {
            ["name"] = valueName,
            ["label"] = valueName,
            ["type"] = "Integer",
            ["cadence"] = "Monthly",
            ["required"] = true,
            ["modifiable"] = true,
            ["enabled"] = true,
        };
        if (withBands)
        {
            value["greenMin"] = 80d;
            value["greenMax"] = 100d;
            value["amberMin"] = 50d;
            value["amberMax"] = 100d;
        }

        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["label"] = name,
            ["modifiable"] = true,
            ["enabled"] = true,
            ["isGlobal"] = true,
            ["values"] = new[] { value },
            ["version"] = 1,
        };
        if (approval is not null) body["approval"] = approval;

        return await (await Admin.PostJsonAsync("/api/admin/schemas", body)).ReadAsync<SchemaDto>();
    }

    /// <summary>Run an OData query as admin and return the <c>value</c> array element.</summary>
    protected async Task<System.Text.Json.JsonElement> ODataValuesAsync(string url)
    {
        var json = await (await Admin.GetAsync(url)).ReadJsonAsync();
        return json.GetProperty("value");
    }

    /// <summary>Count the projected sample rows currently visible (live) for a schema via the OData feed.</summary>
    protected async Task<int> CountSamplesAsync(string schemaName)
    {
        var values = await ODataValuesAsync($"/odata/samples?$filter=SchemaName eq '{schemaName}'");
        return values.GetArrayLength();
    }

    /// <summary>Submit one integer sample for <paramref name="schemaName"/> as the given service client. Returns the new submission id.</summary>
    protected static async Task<Guid> SubmitSampleAsync(
        HttpClient serviceClient, string schemaName, int value, bool draft = false, DateTime? timestamp = null)
    {
        var url = draft ? "/api/submissions?draft=true" : "/api/submissions";
        var body = new
        {
            samples = new[]
            {
                new
                {
                    schemaName,
                    valueName = "count",
                    value,
                    timestamp = timestamp ?? DateTime.UtcNow,
                    note = (string?)null,
                },
            },
        };
        var response = await serviceClient.PostJsonAsync(url, body);
        var written = await response.ReadAsync<SubmissionWriteResponse>();
        return written.Id;
    }
}
