// Weekly workforce snapshot pushed to Ingest from MHR iTrent, on a timer (Azure Functions, C# isolated worker).
//
// This is the cloud-scheduled sibling of ../hr-workforce-itrent-api-csharp: the same
// OAuth2 -> fetch person-level extract -> AGGREGATE locally -> POST one weekly submission
// logic, but driven by a [TimerTrigger] instead of a one-shot console run. Config that the
// console example reads from env vars / args is read here from Function App settings (which
// surface as environment variables), so the API key and iTrent secret live in app settings /
// Key Vault references rather than a local .cmd.
//
// Privacy: the iTrent extract is person-level, but only the aggregated counts/totals ever
// leave this process - no employee records are sent to Ingest. See docs/gdpr.md.
//
// Local run: serve sample_response.json with `python -m http.server 8000` (the default
// SOURCE_URL) and run `func start`; leave ITRENT_* empty so no OAuth is attempted. In
// production set ITRENT_TOKEN_URL + ITRENT_CLIENT_ID + ITRENT_CLIENT_SECRET and point
// SOURCE_URL at the real iTrent endpoint.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HrWorkforceItrentAzureFunction;

public class WeeklyWorkforceFunction(ILogger<WeeklyWorkforceFunction> logger)
{
    const string SchemaName = "weekly_workforce";
    const string DefaultSourceUrl = "http://localhost:8000/sample_response.json";

    // 06:00 every Monday, for the prior week. NCRONTAB is {second} {minute} {hour} ...
    [Function("WeeklyWorkforce")]
    public async Task Run([TimerTrigger("0 0 6 * * 1")] TimerInfo timer)
    {
        string sourceUrl = Environment.GetEnvironmentVariable("SOURCE_URL") ?? DefaultSourceUrl;
        string? tokenUrl = Environment.GetEnvironmentVariable("ITRENT_TOKEN_URL");

        // Layer 1 auth - to iTrent. Only when a token URL is configured; the local sample
        // file is served without auth, so a self-contained local run needs none of this.
        string? bearer = null;
        if (!string.IsNullOrWhiteSpace(tokenUrl))
        {
            string? clientId = Environment.GetEnvironmentVariable("ITRENT_CLIENT_ID");
            string? clientSecret = Environment.GetEnvironmentVariable("ITRENT_CLIENT_SECRET");
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                logger.LogError("ITRENT_TOKEN_URL is set but ITRENT_CLIENT_ID / ITRENT_CLIENT_SECRET are missing.");
                return;
            }
            bearer = await GetToken(tokenUrl!, clientId!, clientSecret!);
        }

        string extractJson = await FetchExtract(sourceUrl, bearer);

        string weekEnding;
        List<Employee> employees;
        using (var doc = JsonDocument.Parse(extractJson))
        {
            var root = doc.RootElement;
            weekEnding = root.GetProperty("weekEnding").GetString()
                ?? throw new InvalidOperationException("Extract is missing 'weekEnding'.");
            employees = [];
            foreach (var e in root.GetProperty("employees").EnumerateArray())
            {
                employees.Add(new Employee(
                    Status: (e.GetProperty("employmentStatus").GetString() ?? "").Trim().ToLowerInvariant(),
                    Engagement: (e.GetProperty("engagement").GetString() ?? "").Trim().ToLowerInvariant(),
                    SickThisWeek: e.TryGetProperty("sicknessThisWeek", out var s) && s.ValueKind == JsonValueKind.True,
                    OvertimeHours: e.TryGetProperty("overtimeHours", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetDouble() : 0));
            }
        }

        // Aggregate the person-level rows into the weekly KPIs. Everything below this point
        // is just counts and totals - no personal data travels any further.
        var active = employees.Where(e => e.Status == "active").ToList();
        var permanentActive = active.Where(e => e.Engagement == "permanent").ToList();

        int employeesActive = permanentActive.Count;
        int sickLeave = permanentActive.Count(e => e.SickThisWeek);
        int contractors = active.Count(e => e.Engagement == "contractor");
        double overtimeHours = Math.Round(active.Sum(e => e.OvertimeHours), 2);

        // Weekly cadence: one sample per week bucket, keyed on the week-ending date.
        string timestamp = $"{weekEnding}T00:00:00Z";

        var samples = new List<string>
        {
            Sample("employees_active", Num(employeesActive), timestamp),
            Sample("sick_leave", Num(sickLeave), timestamp),
            Sample("contractors", Num(contractors), timestamp),
        };

        // overtime_hours is optional: only send when there was overtime to report.
        if (overtimeHours > 0)
            samples.Add(Sample("overtime_hours", Num(overtimeHours), timestamp));

        string body = "{\"samples\":[" + string.Join(",", samples) + "]}";

        string? baseUrl = Environment.GetEnvironmentVariable("INGEST_BASE_URL");
        string? apiKey = Environment.GetEnvironmentVariable("INGEST_API_KEY");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("Set INGEST_BASE_URL and INGEST_API_KEY in the Function App settings.");
            return;
        }

        await PostSubmission(baseUrl!.TrimEnd('/'), apiKey!, body);
    }

    // One sample object as a JSON string. `valueJson` is already JSON-encoded.
    static string Sample(string valueName, string valueJson, string timestamp) =>
        $"{{\"schemaName\":{Str(SchemaName)},\"valueName\":{Str(valueName)},\"value\":{valueJson},\"timestamp\":{Str(timestamp)},\"note\":null}}";

    static string Num(double n) => n.ToString(CultureInfo.InvariantCulture);

    static string Str(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char ch in s)
            sb.Append(ch switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => ch.ToString(),
            });
        return sb.Append('"').ToString();
    }

    // OAuth2 client-credentials grant: exchange the client id/secret for a short-lived
    // bearer token. iTrent's integration APIs are OAuth2-protected; adjust the scope to
    // match what your tenant issues.
    async Task<string> GetToken(string tokenUrl, string clientId, string clientSecret)
    {
        using var http = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });
        using var response = await http.PostAsync(tokenUrl, form);
        string payload = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("iTrent token request failed: HTTP {Status}\n  {Payload}", (int)response.StatusCode, payload);
            throw new InvalidOperationException($"iTrent token request failed: HTTP {(int)response.StatusCode}");
        }
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Token response had no access_token.");
    }

    async Task<string> FetchExtract(string url, string? bearer)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(bearer))
            request.Headers.Add("Authorization", $"Bearer {bearer}");

        using var response = await http.SendAsync(request);
        string payload = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("iTrent extract request failed: HTTP {Status}\n  {Payload}", (int)response.StatusCode, payload);
            throw new InvalidOperationException($"iTrent extract request failed: HTTP {(int)response.StatusCode}");
        }
        return payload;
    }

    async Task PostSubmission(string baseUrl, string apiKey, string body)
    {
        using var http = new HttpClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Api-Key", apiKey);

        using var response = await http.PostAsync($"{baseUrl}/api/submissions", content);
        string payload = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(payload);
            logger.LogInformation("Created submission {Id}", doc.RootElement.GetProperty("id").GetString());
            if (doc.RootElement.TryGetProperty("warnings", out var warnings))
                foreach (var w in warnings.EnumerateArray())
                    logger.LogWarning("  warning: {Warning}", w.GetString());
            return;
        }

        logger.LogError("Submission failed: HTTP {Status}", (int)response.StatusCode);
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                foreach (var e in errors.EnumerateArray())
                    logger.LogError("  error: {Error}", e.GetString());
            else if (doc.RootElement.TryGetProperty("detail", out var detail))
                logger.LogError("  {Detail}", detail.GetString());
        }
        catch (JsonException)
        {
            logger.LogError("  {Payload}", payload);
        }
    }

    record Employee(string Status, string Engagement, bool SickThisWeek, double OvertimeHours);
}
