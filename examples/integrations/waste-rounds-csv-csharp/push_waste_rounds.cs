// Push a day's waste-collection rounds to the Ingest service (minimal C# example).
//
// This is a .NET 10 "file-based app": run it directly with `dotnet run push_waste_rounds.cs`
// - no .csproj, no build step. It reads a round-level CSV export of the kind a
// waste-management / in-cab system produces, aggregates it into the daily KPIs of
// the `garbage_collection` schema, and POSTs one submission. Only the BCL is used.
//
// The request JSON is built by hand (file-based apps disable reflection-based
// serialization by default), which also makes the payload shape explicit. The
// reflection-free JsonDocument is used to read the response.
//
// Usage:
//   set INGEST_BASE_URL=https://ingest.example.org
//   set INGEST_API_KEY=abc12345.your-secret-here
//   dotnet run push_waste_rounds.cs -- [--csv rounds_export_2026-06-15.csv] [--dry-run]

using System.Globalization;
using System.Text;
using System.Text.Json;

const string SchemaName = "garbage_collection";

bool dryRun = args.Contains("--dry-run");

string csvPath = Path.Combine(Directory.GetCurrentDirectory(), "rounds_export_2026-06-15.csv");
int csvFlag = Array.IndexOf(args, "--csv");
if (csvFlag >= 0 && csvFlag + 1 < args.Length)
    csvPath = args[csvFlag + 1];

List<Round> rounds = ReadRounds(csvPath);
if (rounds.Count == 0)
{
    Console.Error.WriteLine("No rounds found in the export - nothing to submit.");
    Environment.Exit(1);
}

// Daily cadence: one sample per day bucket, end-of-shift UTC timestamp.
string timestamp = $"{rounds[0].CollectionDate}T17:00:00Z";

var completed = rounds.Where(r => r.Status == "completed").ToList();
var missed = rounds.Where(r => r.Status == "missed").ToList();
var breakdowns = rounds.Where(r => r.VehicleBreakdown).ToList();

double generalTonnes = rounds.Sum(r => r.GeneralWasteTonnes);
double recyclingTonnes = rounds.Sum(r => r.RecyclingTonnes);

// Tonnage-weighted average contamination across rounds that carried recycling.
var recyclingRounds = rounds.Where(r => r.RecyclingTonnes > 0).ToList();
double contamination = recyclingRounds.Count > 0
    ? Math.Round(recyclingRounds.Sum(r => r.RecyclingTonnes * r.ContaminationPct) / recyclingTonnes, 2)
    : 0;

// tonnes_collected is the gate total (recycling included) so recycling <= total holds.
var samples = new List<string>
{
    Sample("tonnes_collected", Num(Math.Round(generalTonnes + recyclingTonnes, 2))),
    Sample("routes_completed", Num(completed.Count)),
    Sample("routes_missed", Num(missed.Count)),
    Sample("vehicle_breakdowns", Num(breakdowns.Count)),
    Sample("recycling_tonnes_collected", Num(Math.Round(recyclingTonnes, 2))),
};

// Conditional fields mirror the schema's visibleIf rules: only send when relevant.
if (missed.Count > 0)
    samples.Add(Sample("routes_missed_reason", Str(
        string.Join("; ", missed.Where(r => r.MissReason.Length > 0).Select(r => $"{r.RoundName}: {r.MissReason}")))));

if (breakdowns.Count > 0)
    samples.Add(Sample("breakdown_description", Str(
        string.Join("; ", breakdowns.Where(r => r.BreakdownNotes.Length > 0).Select(r => $"{r.VehicleReg} ({r.RoundName}): {r.BreakdownNotes}")))));

if (recyclingTonnes > 0)
    samples.Add(Sample("contamination_pct", Num(contamination)));

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

static List<Round> ReadRounds(string path)
{
    var rows = File.ReadAllLines(path);
    var result = new List<Round>();
    // Minimal parser: assumes no embedded commas in the sample export.
    for (int i = 1; i < rows.Length; i++)
    {
        if (string.IsNullOrWhiteSpace(rows[i])) continue;
        var c = rows[i].Split(',');
        result.Add(new Round(
            RoundName: c[1],
            CollectionDate: c[3],
            Status: c[4].Trim().ToLowerInvariant(),
            MissReason: c[5].Trim(),
            GeneralWasteTonnes: ParseDouble(c[6]),
            RecyclingTonnes: ParseDouble(c[7]),
            ContaminationPct: ParseDouble(c[8]),
            VehicleReg: c[9],
            VehicleBreakdown: c[10].Trim().ToUpperInvariant() == "Y",
            BreakdownNotes: c[11].Trim()));
    }
    return result;
}

static double ParseDouble(string s) =>
    double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

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

record Round(
    string RoundName,
    string CollectionDate,
    string Status,
    string MissReason,
    double GeneralWasteTonnes,
    double RecyclingTonnes,
    double ContaminationPct,
    string VehicleReg,
    bool VehicleBreakdown,
    string BreakdownNotes);
