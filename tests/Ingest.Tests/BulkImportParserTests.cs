using System.Text.Json;
using Ingest.Core.Abstractions;
using Ingest.Infrastructure.Services;

namespace Ingest.Tests;

/// <summary>
/// Unit tests for <see cref="BulkImportParser"/> — the structural JSON/CSV parsing that underpins
/// the admin bulk import. Domain validation is out of scope here; these only check shape handling,
/// grouping, timestamp parsing, value typing, and error reporting.
/// </summary>
public class BulkImportParserTests
{
    // ── JSON ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Json_object_with_submissions_array_yields_one_group_each()
    {
        const string json = """
        {
          "submissions": [
            { "samples": [ { "schemaName": "s", "valueName": "a", "value": 1, "timestamp": "2024-01-01T00:00:00Z" } ] },
            { "samples": [ { "schemaName": "s", "valueName": "a", "value": 2, "timestamp": "2024-02-01T00:00:00Z" } ] }
          ]
        }
        """;

        var result = BulkImportParser.Parse(BulkImportFormat.Json, json);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Submissions.Count);
        Assert.Single(result.Submissions[0].Samples);
    }

    [Fact]
    public void Json_object_with_samples_is_one_submission()
    {
        const string json = """
        { "samples": [
            { "schemaName": "s", "valueName": "a", "value": 1, "timestamp": "2024-01-01T00:00:00Z" },
            { "schemaName": "s", "valueName": "b", "value": 2, "timestamp": "2024-01-01T00:00:00Z" }
        ] }
        """;

        var result = BulkImportParser.Parse(BulkImportFormat.Json, json);

        Assert.Empty(result.Errors);
        var sub = Assert.Single(result.Submissions);
        Assert.Equal(2, sub.Samples.Count);
    }

    [Fact]
    public void Json_top_level_array_is_an_array_of_submissions()
    {
        const string json = """
        [
          { "samples": [ { "schemaName": "s", "valueName": "a", "value": 1, "timestamp": "2024-01-01T00:00:00Z" } ] },
          { "samples": [ { "schemaName": "s", "valueName": "a", "value": 2, "timestamp": "2024-02-01T00:00:00Z" } ] }
        ]
        """;

        var result = BulkImportParser.Parse(BulkImportFormat.Json, json);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Submissions.Count);
    }

    [Fact]
    public void Json_invalid_syntax_reports_error_and_no_submissions()
    {
        var result = BulkImportParser.Parse(BulkImportFormat.Json, "{ not json ]");

        Assert.Empty(result.Submissions);
        Assert.Single(result.Errors);
        Assert.Contains("Invalid JSON", result.Errors[0]);
    }

    [Fact]
    public void Json_submission_missing_samples_is_an_error()
    {
        const string json = """{ "submissions": [ { "note": "oops" } ] }""";

        var result = BulkImportParser.Parse(BulkImportFormat.Json, json);

        Assert.Empty(result.Submissions);
        Assert.Contains(result.Errors, e => e.Contains("missing a 'samples'"));
    }

    [Fact]
    public void Json_sample_missing_schema_name_is_an_error()
    {
        const string json = """
        { "samples": [ { "valueName": "a", "value": 1, "timestamp": "2024-01-01T00:00:00Z" } ] }
        """;

        var result = BulkImportParser.Parse(BulkImportFormat.Json, json);

        Assert.Empty(result.Submissions);
        Assert.Contains(result.Errors, e => e.Contains("'schemaName' and 'valueName' are required"));
    }

    [Fact]
    public void Json_empty_content_is_an_error()
    {
        var result = BulkImportParser.Parse(BulkImportFormat.Json, "   ");
        Assert.Contains(result.Errors, e => e.Contains("empty"));
    }

    // ── CSV ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Csv_groups_rows_by_group_column_in_first_seen_order()
    {
        const string csv = """
        group,schemaName,valueName,value,timestamp,note
        jan,roads,length,10,2024-01-31T00:00:00Z,
        jan,roads,width,3,2024-01-31T00:00:00Z,
        feb,roads,length,12,2024-02-29T00:00:00Z,note here
        """;

        var result = BulkImportParser.Parse(BulkImportFormat.Csv, csv);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Submissions.Count);
        Assert.Equal("jan", result.Submissions[0].Group);
        Assert.Equal(2, result.Submissions[0].Samples.Count);
        Assert.Equal("feb", result.Submissions[1].Group);
        Assert.Equal("note here", result.Submissions[1].Samples[0].Note);
    }

    [Fact]
    public void Csv_without_group_column_is_a_single_submission()
    {
        const string csv = """
        schemaName,valueName,value,timestamp
        roads,length,10,2024-01-31T00:00:00Z
        roads,width,3,2024-01-31T00:00:00Z
        """;

        var result = BulkImportParser.Parse(BulkImportFormat.Csv, csv);

        Assert.Empty(result.Errors);
        var sub = Assert.Single(result.Submissions);
        Assert.Null(sub.Group);
        Assert.Equal(2, sub.Samples.Count);
    }

    [Fact]
    public void Csv_missing_required_column_is_an_error()
    {
        const string csv = """
        schemaName,valueName,timestamp
        roads,length,2024-01-31T00:00:00Z
        """;

        var result = BulkImportParser.Parse(BulkImportFormat.Csv, csv);

        Assert.Empty(result.Submissions);
        Assert.Contains(result.Errors, e => e.Contains("Missing required column 'value'"));
    }

    [Fact]
    public void Csv_bad_timestamp_reports_row_number()
    {
        const string csv = """
        schemaName,valueName,value,timestamp
        roads,length,10,not-a-date
        """;

        var result = BulkImportParser.Parse(BulkImportFormat.Csv, csv);

        Assert.Empty(result.Submissions);
        Assert.Contains(result.Errors, e => e.Contains("Row 2") && e.Contains("not a valid timestamp"));
    }

    [Fact]
    public void Csv_handles_quoted_fields_with_commas_and_embedded_newlines()
    {
        const string csv = "schemaName,valueName,value,timestamp,note\r\n"
            + "roads,name,\"Main, St\",2024-01-31T00:00:00Z,\"line one\nline two\"\r\n";

        var result = BulkImportParser.Parse(BulkImportFormat.Csv, csv);

        Assert.Empty(result.Errors);
        var sample = Assert.Single(Assert.Single(result.Submissions).Samples);
        Assert.Equal("Main, St", sample.Value!.Value.GetString());
        Assert.Equal("line one\nline two", sample.Note);
    }

    [Fact]
    public void Csv_booleans_become_json_bool_other_values_stay_strings()
    {
        const string csv = """
        schemaName,valueName,value,timestamp
        s,flag,true,2024-01-31T00:00:00Z
        s,count,42,2024-01-31T00:00:00Z
        """;

        var result = BulkImportParser.Parse(BulkImportFormat.Csv, csv);

        var samples = Assert.Single(result.Submissions).Samples;
        Assert.Equal(JsonValueKind.True, samples[0].Value!.Value.ValueKind);
        Assert.Equal(JsonValueKind.String, samples[1].Value!.Value.ValueKind);
        Assert.Equal("42", samples[1].Value!.Value.GetString());
    }

    [Fact]
    public void Csv_empty_value_cell_becomes_no_value()
    {
        const string csv = """
        schemaName,valueName,value,timestamp
        s,opt,,2024-01-31T00:00:00Z
        """;

        var result = BulkImportParser.Parse(BulkImportFormat.Csv, csv);

        var sample = Assert.Single(Assert.Single(result.Submissions).Samples);
        Assert.Null(sample.Value);
    }
}
