using Ingest.Core.Entities;
using Ingest.Infrastructure.Services;

namespace Ingest.Tests;

/// <summary>
/// Tests for <see cref="SampleProjectionBuilder.Build"/>: the pure mapper that flattens a
/// <see cref="Submission"/> into denormalised <see cref="SampleProjection"/> rows. The focus here
/// is the <c>SubmittedAt</c> snapshot (when the submission was reported) versus the per-sample
/// <c>Timestamp</c> (when the measurement was taken).
/// </summary>
public class SampleProjectionBuilderTests
{
    private static Schema NumberSchema() => new()
    {
        Name = "monthly_kpis", Label = "Monthly KPIs", Enabled = true,
        Values = new List<SchemaValue>
        {
            new() { Name = "tonnes", Type = SchemaValueType.Number, Cadence = Cadence.Monthly },
        },
    };

    private static Submission SubmissionWith(DateTime submittedAt, params DateTime[] sampleTimestamps)
    {
        var sub = new Submission
        {
            ServiceAccountId = Guid.NewGuid(),
            ServiceName = "roads-team",
            SubmittedAt = submittedAt,
        };
        foreach (var ts in sampleTimestamps)
        {
            sub.Samples.Add(new Sample
            {
                SchemaName = "monthly_kpis",
                ValueName = "tonnes",
                Value = 42d,
                Timestamp = ts,
            });
        }
        return sub;
    }

    [Fact]
    public void Build_populates_SubmittedAt_from_the_submission()
    {
        var submittedAt = new DateTime(2026, 5, 20, 9, 30, 0, DateTimeKind.Utc);
        // Measured a month before it was reported — back-filled history.
        var measuredAt = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);
        var schemas = new Dictionary<string, Schema> { ["monthly_kpis"] = NumberSchema() };

        var rows = SampleProjectionBuilder.Build(SubmissionWith(submittedAt, measuredAt), schemas).ToList();

        var row = Assert.Single(rows);
        Assert.Equal(submittedAt, row.SubmittedAt);
        // It must be distinct from the measurement timestamp (the whole point of the field).
        Assert.NotEqual(row.Timestamp, row.SubmittedAt);
    }

    [Fact]
    public void Build_stamps_SubmittedAt_as_utc()
    {
        // An unspecified-kind value (as Mongo may hand back) must be normalised to UTC, matching
        // how Timestamp is handled.
        var submittedAt = new DateTime(2026, 5, 20, 9, 30, 0, DateTimeKind.Unspecified);
        var schemas = new Dictionary<string, Schema> { ["monthly_kpis"] = NumberSchema() };

        var rows = SampleProjectionBuilder.Build(
            SubmissionWith(submittedAt, new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)), schemas).ToList();

        var row = Assert.Single(rows);
        Assert.Equal(DateTimeKind.Utc, row.SubmittedAt.Kind);
    }

    [Fact]
    public void Build_copies_the_same_SubmittedAt_onto_every_row()
    {
        var submittedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var schemas = new Dictionary<string, Schema> { ["monthly_kpis"] = NumberSchema() };

        var rows = SampleProjectionBuilder.Build(
            SubmissionWith(
                submittedAt,
                new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc)),
            schemas).ToList();

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(submittedAt, r.SubmittedAt));
    }

    [Fact]
    public void Build_leaves_legacy_default_SubmittedAt_as_utc_min_value()
    {
        // Submissions that predate the SubmittedAt field carry default(DateTime). The projection
        // should not invent a value — it stays 0001-01-01 — but it's still stamped UTC so it never
        // surprises consumers with an Unspecified kind (matches the legacy note in samples.md).
        var schemas = new Dictionary<string, Schema> { ["monthly_kpis"] = NumberSchema() };

        var rows = SampleProjectionBuilder.Build(
            SubmissionWith(default, new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc)), schemas).ToList();

        var row = Assert.Single(rows);
        Assert.Equal(default(DateTime), row.SubmittedAt);
        Assert.Equal(DateTimeKind.Utc, row.SubmittedAt.Kind);
    }
}
