using System.Globalization;
using System.Text;
using System.Text.Json;
using Ingest.Core.Abstractions;

namespace Ingest.Infrastructure.Services;

/// <summary>One submission group parsed out of a bulk import file, before any validation.</summary>
/// <param name="Group">Group key (CSV <c>group</c> column) when present; <c>null</c> otherwise.</param>
/// <param name="Samples">The samples that make up the submission.</param>
public sealed record ParsedSubmission(string? Group, IReadOnlyList<SampleInput> Samples);

/// <summary>Result of parsing a bulk import file: the submission groups plus any structural errors.</summary>
/// <param name="Submissions">Parsed groups in document order. Empty when parsing failed.</param>
/// <param name="Errors">Human-readable parse errors with row/group context. Empty on success.</param>
public sealed record BulkImportParseResult(IReadOnlyList<ParsedSubmission> Submissions, IReadOnlyList<string> Errors);

/// <summary>
/// Pure (no I/O) parser that turns a bulk import file into submission groups. Kept separate from
/// <see cref="BulkImportService"/> so the fiddly JSON/CSV shape handling can be unit-tested in
/// isolation. The parser only enforces structural rules (required columns/fields, parseable
/// timestamps); all domain validation happens later when each group is submitted.
/// </summary>
public static class BulkImportParser
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Parse <paramref name="content"/> according to <paramref name="format"/>.</summary>
    public static BulkImportParseResult Parse(BulkImportFormat format, string content) => format switch
    {
        BulkImportFormat.Json => ParseJson(content),
        BulkImportFormat.Csv => ParseCsv(content),
        _ => new BulkImportParseResult(Array.Empty<ParsedSubmission>(), new[] { $"Unsupported import format '{format}'." }),
    };

    // ── JSON ──────────────────────────────────────────────────────────────────────────────────

    private static BulkImportParseResult ParseJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Fail("The file is empty.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            return Fail($"Invalid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var errors = new List<string>();
            var submissions = new List<ParsedSubmission>();

            // Accepted shapes:
            //   1) { "submissions": [ { "samples": [...] }, ... ] }
            //   2) { "samples": [...] }                       (a single submission)
            //   3) [ { "samples": [...] }, ... ]              (array of submissions)
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("submissions", out var subs))
            {
                if (subs.ValueKind != JsonValueKind.Array)
                    return Fail("'submissions' must be an array.");
                var i = 0;
                foreach (var sub in subs.EnumerateArray())
                    AddSubmission(sub, i++, submissions, errors);
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("samples", out _))
            {
                AddSubmission(root, 0, submissions, errors);
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var sub in root.EnumerateArray())
                    AddSubmission(sub, i++, submissions, errors);
            }
            else
            {
                return Fail("Expected an array of submissions, or an object with a 'submissions' or 'samples' property.");
            }

            if (errors.Count > 0)
                return new BulkImportParseResult(Array.Empty<ParsedSubmission>(), errors);
            if (submissions.Count == 0)
                return Fail("No submissions found in the file.");

            return new BulkImportParseResult(submissions, Array.Empty<string>());
        }
    }

    private static void AddSubmission(JsonElement submission, int index, List<ParsedSubmission> into, List<string> errors)
    {
        var where = $"Submission #{index + 1}";
        if (submission.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{where}: expected an object with a 'samples' array.");
            return;
        }
        if (!submission.TryGetProperty("samples", out var samplesEl) || samplesEl.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{where}: missing a 'samples' array.");
            return;
        }

        var samples = new List<SampleInput>();
        var s = 0;
        foreach (var sampleEl in samplesEl.EnumerateArray())
        {
            s++;
            try
            {
                var sample = sampleEl.Deserialize<SampleInput>(JsonOptions);
                if (sample is null || string.IsNullOrWhiteSpace(sample.SchemaName) || string.IsNullOrWhiteSpace(sample.ValueName))
                {
                    errors.Add($"{where}, sample #{s}: 'schemaName' and 'valueName' are required.");
                    continue;
                }
                samples.Add(sample);
            }
            catch (JsonException ex)
            {
                errors.Add($"{where}, sample #{s}: {ex.Message}");
            }
        }

        if (samples.Count == 0)
        {
            errors.Add($"{where}: contains no valid samples.");
            return;
        }
        into.Add(new ParsedSubmission(null, samples));
    }

    // ── CSV ───────────────────────────────────────────────────────────────────────────────────

    private static BulkImportParseResult ParseCsv(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Fail("The file is empty.");

        var records = ReadCsvRecords(content);
        if (records.Count == 0)
            return Fail("The file is empty.");

        var header = records[0];
        var cols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim();
            if (name.Length > 0 && !cols.ContainsKey(name)) cols[name] = i;
        }

        foreach (var required in new[] { "schemaName", "valueName", "value", "timestamp" })
            if (!cols.ContainsKey(required))
                return Fail($"Missing required column '{required}'. Expected a header row with: group (optional), schemaName, valueName, value, timestamp, note (optional).");

        var hasGroup = cols.TryGetValue("group", out var groupCol);
        var hasNote = cols.TryGetValue("note", out var noteCol);
        var schemaCol = cols["schemaName"];
        var valueNameCol = cols["valueName"];
        var valueCol = cols["value"];
        var tsCol = cols["timestamp"];

        var errors = new List<string>();
        // Preserve first-appearance order of group keys; key "" buckets the ungrouped rows together.
        var order = new List<string>();
        var grouped = new Dictionary<string, List<SampleInput>>(StringComparer.Ordinal);

        for (var r = 1; r < records.Count; r++)
        {
            var row = records[r];
            // Skip wholly empty lines (trailing newline, blank separators).
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace)) continue;

            var line = r + 1; // 1-based, header included, for human-friendly messages.
            var schemaName = Field(row, schemaCol);
            var valueName = Field(row, valueNameCol);
            var rawTs = Field(row, tsCol);

            if (string.IsNullOrWhiteSpace(schemaName) || string.IsNullOrWhiteSpace(valueName))
            {
                errors.Add($"Row {line}: 'schemaName' and 'valueName' are required.");
                continue;
            }
            if (!TryParseTimestamp(rawTs, out var ts))
            {
                errors.Add($"Row {line}: '{rawTs}' is not a valid timestamp.");
                continue;
            }

            var sample = new SampleInput(
                schemaName.Trim(),
                valueName.Trim(),
                CellToJson(Field(row, valueCol)),
                ts,
                hasNote ? NullIfEmpty(Field(row, noteCol)) : null);

            var groupKey = hasGroup ? Field(row, groupCol).Trim() : string.Empty;
            if (!grouped.TryGetValue(groupKey, out var bucket))
            {
                bucket = new List<SampleInput>();
                grouped[groupKey] = bucket;
                order.Add(groupKey);
            }
            bucket.Add(sample);
        }

        if (errors.Count > 0)
            return new BulkImportParseResult(Array.Empty<ParsedSubmission>(), errors);

        var submissions = order
            .Select(k => new ParsedSubmission(k.Length == 0 ? null : k, grouped[k]))
            .ToList();

        if (submissions.Count == 0)
            return Fail("No data rows found in the file.");

        return new BulkImportParseResult(submissions, Array.Empty<string>());
    }

    private static string Field(IReadOnlyList<string> row, int index) => index < row.Count ? row[index] : string.Empty;

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool TryParseTimestamp(string raw, out DateTime ts)
    {
        ts = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (!DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return false;
        ts = parsed;
        return true;
    }

    /// <summary>
    /// Turn a raw CSV cell into a JSON value for <c>JsonValueMapper</c> to coerce. We only special-
    /// case booleans (the mapper can't coerce the string "true" to a bool); everything else stays a
    /// JSON string so the schema's declared type drives the final coercion and quirks like leading
    /// zeros survive. An empty cell becomes "no value" (null).
    /// </summary>
    private static JsonElement? CellToJson(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var trimmed = raw.Trim();
        if (bool.TryParse(trimmed, out var b))
            return JsonSerializer.SerializeToElement(b);
        return JsonSerializer.SerializeToElement(raw);
    }

    // ── CSV tokenizer (RFC 4180-ish: quotes, escaped quotes, embedded commas/newlines) ──────────

    private static List<List<string>> ReadCsvRecords(string content)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;

        void EndField()
        {
            record.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
        }

        void EndRecord()
        {
            EndField();
            records.Add(record);
            record = new List<string>();
        }

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    fieldStarted = true;
                    break;
                case ',':
                    EndField();
                    break;
                case '\r':
                    // Swallow; the following \n (if any) finalises the record.
                    if (i + 1 < content.Length && content[i + 1] == '\n') { i++; }
                    EndRecord();
                    break;
                case '\n':
                    EndRecord();
                    break;
                default:
                    field.Append(c);
                    fieldStarted = true;
                    break;
            }
        }

        // Flush a trailing record that wasn't terminated by a newline.
        if (fieldStarted || field.Length > 0 || record.Count > 0)
            EndRecord();

        return records;
    }

    private static BulkImportParseResult Fail(string error) =>
        new(Array.Empty<ParsedSubmission>(), new[] { error });
}
