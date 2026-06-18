// Push a weekly workforce snapshot to Ingest from MHR iTrent (minimal C# example).
//
// This is a .NET 10 "file-based app": run it directly with
// `dotnet run push_workforce_itrent.cs` - no .csproj, no build step. It authenticates
// to iTrent with OAuth2 client credentials, GETs a person-level personnel/absence
// extract for a team, AGGREGATES it locally into the KPIs of the `weekly_workforce`
// schema, and POSTs one weekly submission. Only the BCL is used.
//
// Privacy: the iTrent extract is person-level, but only the aggregated counts/totals
// ever leave this machine - no employee records are sent to Ingest. See docs/gdpr.md.
//
// For a self-contained run, the default source is a local static file served by
// `python -m http.server` (see README) and no OAuth is performed (leave ITRENT_* unset).
// In production set ITRENT_TOKEN_URL + ITRENT_CLIENT_ID + ITRENT_CLIENT_SECRET and point
// --source-url at the real iTrent endpoint.
//
// Usage:
//   set INGEST_BASE_URL=https://ingest.example.org
//   set INGEST_API_KEY=abc12345.your-secret-here
//   dotnet run push_workforce_itrent.cs -- [--source-url URL] [--token-url URL] [--dry-run]

using System.Globalization;
using System.Text;
using System.Text.Json;

const string SchemaName = "weekly_workforce";
const string DefaultSourceUrl = "http://localhost:8000/sample_response.json";

bool dryRun = args.Contains("--dry-run");

string sourceUrl = Environment.GetEnvironmentVariable("SOURCE_URL") ?? DefaultSourceUrl;
int sourceFlag = Array.IndexOf(args, "--source-url");
if (sourceFlag >= 0 && sourceFlag + 1 < args.Length)
    sourceUrl = args[sourceFlag + 1];

string? tokenUrl = Environment.GetEnvironmentVariable("ITRENT_TOKEN_URL");
int tokenFlag = Array.IndexOf(args, "--token-url");
if (tokenFlag >= 0 && tokenFlag + 1 < args.Length)
    tokenUrl = args[tokenFlag + 1];

// Layer 1 auth - to iTrent. Only when a token URL is configured; the local sample
// file is served without auth, so a self-contained dry-run needs none of this.
string? bearer = null;
if (!string.IsNullOrWhiteSpace(tokenUrl))
{
    string? clientId = Environment.GetEnvironmentVariable("ITRENT_CLIENT_ID");
    string? clientSecret = Environment.GetEnvironmentVariable("ITRENT_CLIENT_SECRET");
    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
    {
        Console.Error.WriteLine("ITRENT_TOKEN_URL is set but ITRENT_CLIENT_ID / ITRENT_CLIENT_SECRET are missing.");
        Environment.Exit(1);
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
    employees = new List<Employee>();
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
    Sample("employees_active", Num(employeesActive)),
    Sample("sick_leave", Num(sickLeave)),
    Sample("contractors", Num(contractors)),
};

// overtime_hours is optional: only send when there was overtime to report.
if (overtimeHours > 0)
    samples.Add(Sample("overtime_hours", Num(overtimeHours)));

string body = "{\"samples\":[" + string.Join(",", samples) + "]}";

if (dryRun)
{
    Console.WriteLine(Pretty(body));
    return;
}

string? baseUrl = Environment.GetEnvironmentVariable("INGEST_BASE_URL");
string? apiKey = Environment.GetEnvironmentVariable("INGEST_API_KEY");
if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set INGEST_BASE_URL and INGEST_API_KEY (or use --dry-run).");
    Environment.Exit(1);
}

await PostSubmission(baseUrl!.TrimEnd('/'), apiKey!, body);

// One sample object as a JSON string. `valueJson` is already JSON-encoded.
string Sample(string valueName, string valueJson) =>
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

static string Pretty(string json)
{
    using var doc = JsonDocument.Parse(json);
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        doc.RootElement.WriteTo(writer);
    return Encoding.UTF8.GetString(stream.ToArray());
}

// OAuth2 client-credentials grant: exchange the client id/secret for a short-lived
// bearer token. iTrent's integration APIs are OAuth2-protected; adjust the scope to
// match what your tenant issues.
static async Task<string> GetToken(string tokenUrl, string clientId, string clientSecret)
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
        Console.Error.WriteLine($"iTrent token request failed: HTTP {(int)response.StatusCode}");
        Console.Error.WriteLine($"  {payload}");
        Environment.Exit(1);
    }
    using var doc = JsonDocument.Parse(payload);
    return doc.RootElement.GetProperty("access_token").GetString()
        ?? throw new InvalidOperationException("Token response had no access_token.");
}

static async Task<string> FetchExtract(string url, string? bearer)
{
    using var http = new HttpClient();
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    if (!string.IsNullOrEmpty(bearer))
        request.Headers.Add("Authorization", $"Bearer {bearer}");

    using var response = await http.SendAsync(request);
    string payload = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"iTrent extract request failed: HTTP {(int)response.StatusCode}");
        Console.Error.WriteLine($"  {payload}");
        Environment.Exit(1);
    }
    return payload;
}

static async Task PostSubmission(string baseUrl, string apiKey, string body)
{
    using var http = new HttpClient();
    using var content = new StringContent(body, Encoding.UTF8, "application/json");
    content.Headers.Add("X-Api-Key", apiKey);

    using var response = await http.PostAsync($"{baseUrl}/api/submissions", content);
    string payload = await response.Content.ReadAsStringAsync();

    if (response.IsSuccessStatusCode)
    {
        using var doc = JsonDocument.Parse(payload);
        Console.WriteLine($"Created submission {doc.RootElement.GetProperty("id").GetString()}");
        if (doc.RootElement.TryGetProperty("warnings", out var warnings))
            foreach (var w in warnings.EnumerateArray())
                Console.WriteLine($"  warning: {w.GetString()}");
        return;
    }

    Console.Error.WriteLine($"Submission failed: HTTP {(int)response.StatusCode}");
    try
    {
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            foreach (var e in errors.EnumerateArray())
                Console.Error.WriteLine($"  error: {e.GetString()}");
        else if (doc.RootElement.TryGetProperty("detail", out var detail))
            Console.Error.WriteLine($"  {detail.GetString()}");
    }
    catch (JsonException)
    {
        Console.Error.WriteLine($"  {payload}");
    }
    Environment.Exit(1);
}

record Employee(string Status, string Engagement, bool SickThisWeek, double OvertimeHours);
