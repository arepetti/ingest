// Push a weekly workforce snapshot to the Ingest service from MHR iTrent (minimal C# example).
//
// This is a .NET 10 "file-based app": run it directly with `dotnet run push_workforce.cs`
// - no .csproj, no build step. It GETs a weekly summary from iTrent's OData API,
// reads the few columns it needs, maps them to the `weekly_workforce` schema, and
// POSTs one submission. Only the BCL is used.
//
// The request JSON is built by hand (file-based apps disable reflection-based
// serialization by default), which also makes the payload shape explicit. The
// reflection-free JsonDocument is used to read both the iTrent and Ingest responses.
//
// Usage:
//   set INGEST_BASE_URL=https://ingest.example.org
//   set INGEST_API_KEY=abc12345.your-secret-here
//   dotnet run push_workforce.cs -- [--source-url URL] [--dry-run]

using System.Globalization;
using System.Text;
using System.Text.Json;

const string SchemaName = "weekly_workforce";
const string DefaultSourceUrl = "http://localhost:8000/sample_response.json";

bool dryRun = args.Contains("--dry-run");

string sourceUrl = DefaultSourceUrl;
int urlFlag = Array.IndexOf(args, "--source-url");
if (urlFlag >= 0 && urlFlag + 1 < args.Length)
    sourceUrl = args[urlFlag + 1];

// 1. Query iTrent for just the columns we need. In production the URL carries the
//    OData query, e.g.
//    .../odata/v1/WeeklyWorkforceSummary?$select=activeEmployees,absenceSickness,contingentWorkers,overtimeHours&$filter=organisationUnit eq 'Waste Services'
JsonElement row = await FetchWeek(sourceUrl);

// Weekly cadence: one sample per week bucket. Use the week-ending date (UTC).
string timestamp = $"{row.GetProperty("weekEnding").GetString()}T00:00:00Z";

// 2. Map the iTrent columns -> schema values.
var samples = new List<string>
{
    Sample("employees_active", Num(row.GetProperty("activeEmployees").GetInt32())),
    Sample("sick_leave", Num(row.GetProperty("absenceSickness").GetInt32())),
    Sample("contractors", Num(row.GetProperty("contingentWorkers").GetInt32())),
};

// overtime_hours is optional: only send when there was any overtime to report.
double overtime = row.TryGetProperty("overtimeHours", out var ot) ? ot.GetDouble() : 0;
if (overtime > 0)
    samples.Add(Sample("overtime_hours", Num(overtime)));

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

// GET the OData feed and return the first (and only) matching team-week row.
static async Task<JsonElement> FetchWeek(string url)
{
    using var http = new HttpClient();
    string payload = await http.GetStringAsync(url);
    using var doc = JsonDocument.Parse(payload);
    var rows = doc.RootElement.GetProperty("value");
    if (rows.GetArrayLength() == 0)
    {
        Console.Error.WriteLine("iTrent returned no rows for this week - nothing to submit.");
        Environment.Exit(1);
    }
    // Clone so the value survives the JsonDocument being disposed.
    return rows[0].Clone();
}

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
