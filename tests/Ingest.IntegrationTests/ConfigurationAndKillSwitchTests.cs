using Ingest.Api.Models;
using Ingest.Core.Entities;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>
/// The configurable cadence-anchor settings and the global ingestion kill switch, both served off
/// <c>AdminConfigurationController</c> and the shared <c>AppConfiguration</c> singleton.
/// </summary>
/// <remarks>
/// All tests in this collection share one throwaway MongoDB (see <see cref="IngestAppFixture"/>) and
/// run sequentially (xUnit never parallelises test classes within the same collection), but every
/// test here mutates *global* configuration. Each test restores the default configuration in a
/// <c>finally</c> block so a failure never leaks a closed kill switch or a shifted cadence anchor
/// into an unrelated test.
/// </remarks>
public sealed class ConfigurationAndKillSwitchTests : IntegrationTestBase
{
    public ConfigurationAndKillSwitchTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Submission_window_round_trips_and_preserves_the_other_config_fields()
    {
        // Areas is the pre-existing field on the same singleton document; setting it first and
        // re-reading it after the submission-window update proves the update is a merge, not an
        // overwrite of the whole document.
        var areas = new[] { $"area-{Unique()}" };
        await Admin.PutJsonAsync("/api/admin/configuration/areas", new { areas });

        try
        {
            var defaults = await (await Admin.GetAsync("/api/admin/configuration/submission-window")).ReadAsync<SubmissionWindowDto>();
            Assert.Equal(1, defaults.FiscalYearStartMonth);
            Assert.Equal(DayOfWeek.Monday, defaults.WeekStartDay);
            Assert.Equal(1, defaults.MonthStartDay);

            var requested = new SubmissionWindowDto(4, DayOfWeek.Sunday, 31, new DateTime(2020, 6, 6, 0, 0, 0, DateTimeKind.Utc));
            var updated = await (await Admin.PutJsonAsync("/api/admin/configuration/submission-window", requested)).ReadAsync<SubmissionWindowDto>();

            Assert.Equal(4, updated.FiscalYearStartMonth);
            Assert.Equal(DayOfWeek.Sunday, updated.WeekStartDay);
            Assert.Equal(28, updated.MonthStartDay); // 31 is clamped to 28.
            Assert.Equal(new DateTime(2020, 6, 6, 0, 0, 0, DateTimeKind.Utc), updated.FortnightAnchor);

            var refetched = await (await Admin.GetAsync("/api/admin/configuration/submission-window")).ReadAsync<SubmissionWindowDto>();
            Assert.Equal(updated, refetched);

            var areasAfter = await (await Admin.GetAsync("/api/admin/configuration/areas")).ReadAsync<AreasConfigurationDto>();
            Assert.Equal(areas, areasAfter.Areas);
        }
        finally
        {
            await Admin.PutJsonAsync("/api/admin/configuration/submission-window",
                new SubmissionWindowDto(1, DayOfWeek.Monday, 1, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            await Admin.PutJsonAsync("/api/admin/configuration/areas", new { areas = Array.Empty<string>() });
        }
    }

    [Fact]
    public async Task Ingestion_status_round_trips_and_preserves_the_submission_window()
    {
        var window = new SubmissionWindowDto(7, DayOfWeek.Wednesday, 10, new DateTime(2019, 3, 4, 0, 0, 0, DateTimeKind.Utc));
        await Admin.PutJsonAsync("/api/admin/configuration/submission-window", window);

        try
        {
            var defaultStatus = await (await Admin.GetAsync("/api/admin/configuration/ingestion")).ReadAsync<IngestionStatusDto>();
            Assert.False(defaultStatus.Closed);
            Assert.Null(defaultStatus.Message);

            var closed = await (await Admin.PutJsonAsync("/api/admin/configuration/ingestion", new IngestionStatusDto(true, "  Freeze in effect  ")))
                .ReadAsync<IngestionStatusDto>();
            Assert.True(closed.Closed);
            Assert.Equal("Freeze in effect", closed.Message); // Trimmed server-side.

            var windowAfter = await (await Admin.GetAsync("/api/admin/configuration/submission-window")).ReadAsync<SubmissionWindowDto>();
            Assert.Equal(window, windowAfter);
        }
        finally
        {
            await Admin.PutJsonAsync("/api/admin/configuration/ingestion", new IngestionStatusDto(false, null));
            await Admin.PutJsonAsync("/api/admin/configuration/submission-window",
                new SubmissionWindowDto(1, DayOfWeek.Monday, 1, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        }
    }

    [Fact]
    public async Task Closing_ingestion_blocks_service_writes_but_leaves_reads_and_admin_remediation_open()
    {
        var schema = await CreateSchemaAsync();
        var (serviceAccountId, apiKey, _) = await CreateServiceAccountAsync();
        using var service = Fixture.CreateClient(apiKey);

        // Written while open, so there is something to read/replace once closed.
        var submissionId = await SubmitSampleAsync(service, schema.Name, value: 1);

        try
        {
            var closed = await (await Admin.PutJsonAsync("/api/admin/configuration/ingestion", new IngestionStatusDto(true, "Frozen for maintenance")))
                .ReadAsync<IngestionStatusDto>();
            Assert.True(closed.Closed);

            // Service-facing create is rejected with the configured message.
            var createBody = new
            {
                samples = new[]
                {
                    new { schemaName = schema.Name, valueName = "count", value = 2, timestamp = DateTime.UtcNow, note = (string?)null },
                },
            };
            var createResponse = await service.PostJsonAsync("/api/submissions", createBody);
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, createResponse.StatusCode);
            var createProblem = await createResponse.ReadJsonBodyAsync();
            Assert.Contains("Frozen for maintenance", createProblem.GetProperty("detail").GetString());

            // Service-facing replace of the pre-existing submission is rejected the same way.
            var replaceResponse = await service.PutJsonAsync($"/api/submissions/{submissionId}", createBody);
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, replaceResponse.StatusCode);

            // Reads for the service are unaffected.
            var mine = await (await service.GetAsync($"/api/submissions/{submissionId}")).ReadAsync<SubmissionDto>();
            Assert.Equal(submissionId, mine.Id);
            var schemasVisible = await (await service.GetAsync("/api/schemas")).ReadJsonAsync();
            Assert.Contains(schemasVisible.EnumerateArray(), s => s.GetProperty("name").GetString() == schema.Name);

            // Admin remediation (create on behalf of the service) is NOT gated. Backdated a month so
            // it lands in a different Monthly bucket than the one seeded above (same-period reuse
            // would otherwise trip the unrelated "already submitted for this period" duplicate check).
            var adminCreated = await (await Admin.PostJsonAsync("/api/admin/submissions", new
            {
                serviceAccountId,
                samples = new[]
                {
                    new { schemaName = schema.Name, valueName = "count", value = 3, timestamp = DateTime.UtcNow.AddMonths(-1), note = (string?)null },
                },
            })).ReadAsync<SubmissionWriteResponse>();
            Assert.NotEqual(Guid.Empty, adminCreated.Id);

            // The kill switch is visible to the service on /me, driving the site-wide banner.
            var me = await (await service.GetAsync("/api/me")).ReadJsonAsync();
            Assert.True(me.GetProperty("submissionsClosed").GetBoolean());
            Assert.Equal("Frozen for maintenance", me.GetProperty("submissionsClosedMessage").GetString());
        }
        finally
        {
            await Admin.PutJsonAsync("/api/admin/configuration/ingestion", new IngestionStatusDto(false, null));
        }
    }

    [Fact]
    public async Task Cadence_windows_round_trip_and_clamp_out_of_range_values()
    {
        try
        {
            var defaults = await (await Admin.GetAsync("/api/admin/configuration/cadence-windows")).ReadAsync<CadenceWindowsDto>();
            Assert.Equal(new CadenceWindowDto(0, 0), defaults.Weekly);
            Assert.Equal(new CadenceWindowDto(0, 0), defaults.Yearly);

            var requested = defaults with
            {
                Weekly = new CadenceWindowDto(24, 48),
                Monthly = new CadenceWindowDto(-5, 1_000_000), // negative floored, huge value capped
            };
            var updated = await (await Admin.PutJsonAsync("/api/admin/configuration/cadence-windows", requested)).ReadAsync<CadenceWindowsDto>();

            Assert.Equal(new CadenceWindowDto(24, 48), updated.Weekly);
            Assert.Equal(0, updated.Monthly.OpenOffsetHours);
            Assert.True(updated.Monthly.GraceHours < 1_000_000);
            // Every other cadence was left at its (zero) default by the request.
            Assert.Equal(new CadenceWindowDto(0, 0), updated.Daily);

            var refetched = await (await Admin.GetAsync("/api/admin/configuration/cadence-windows")).ReadAsync<CadenceWindowsDto>();
            Assert.Equal(updated, refetched);
        }
        finally
        {
            await Admin.PutJsonAsync("/api/admin/configuration/cadence-windows", new CadenceWindowsDto(
                new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)));
        }
    }

    [Fact]
    public async Task Cadence_preview_returns_every_cadence_with_the_window_matching_the_configured_grace()
    {
        try
        {
            await Admin.PutJsonAsync("/api/admin/configuration/cadence-windows", new CadenceWindowsDto(
                Daily: new(0, 0), Weekly: new(0, 0), Fortnightly: new(0, 0),
                Monthly: new(0, 72), Quarterly: new(0, 0), SemiAnnually: new(0, 0), Yearly: new(0, 0)));

            var preview = await (await Admin.GetAsync("/api/admin/configuration/cadence-preview")).ReadAsync<List<CadencePreviewEntryDto>>();

            Assert.Equal(7, preview.Count);
            var monthly = preview.Single(p => p.Cadence == Cadence.Monthly);
            Assert.Equal(monthly.PeriodStart, monthly.WindowStart); // zero open offset
            Assert.Equal(monthly.PeriodEnd.AddHours(72), monthly.WindowEnd); // 72h grace
            var weekly = preview.Single(p => p.Cadence == Cadence.Weekly);
            Assert.Equal(weekly.PeriodStart, weekly.WindowStart);
            Assert.Equal(weekly.PeriodEnd, weekly.WindowEnd); // untouched cadence: window == period
        }
        finally
        {
            await Admin.PutJsonAsync("/api/admin/configuration/cadence-windows", new CadenceWindowsDto(
                new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)));
        }
    }

    [Fact]
    public async Task Create_is_rejected_outside_the_window_and_allowed_once_a_grace_is_configured()
    {
        var schema = await CreateSchemaAsync(); // Monthly cadence value "count"
        var (_, apiKey, _) = await CreateServiceAccountAsync();
        using var service = Fixture.CreateClient(apiKey);

        try
        {
            // A year-old timestamp is in a Monthly bucket that closed long ago (default: zero grace).
            var stale = DateTime.UtcNow.AddYears(-1);
            var body = new
            {
                samples = new[] { new { schemaName = schema.Name, valueName = "count", value = 1, timestamp = stale, note = (string?)null } },
            };
            var rejected = await service.PostJsonAsync("/api/submissions", body);
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, rejected.StatusCode);

            // Drafts are exempt from the window gate.
            var draftResponse = await service.PostJsonAsync("/api/submissions?draft=true", body);
            Assert.Equal(System.Net.HttpStatusCode.Created, draftResponse.StatusCode);

            // A grace long enough to cover a year-old sample lets the same (non-draft) request through.
            await Admin.PutJsonAsync("/api/admin/configuration/cadence-windows", new CadenceWindowsDto(
                Daily: new(0, 0), Weekly: new(0, 0), Fortnightly: new(0, 0),
                Monthly: new(0, 24 * 400), Quarterly: new(0, 0), SemiAnnually: new(0, 0), Yearly: new(0, 0)));

            var accepted = await service.PostJsonAsync("/api/submissions", body);
            Assert.Equal(System.Net.HttpStatusCode.Created, accepted.StatusCode);
        }
        finally
        {
            await Admin.PutJsonAsync("/api/admin/configuration/cadence-windows", new CadenceWindowsDto(
                new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0), new(0, 0)));
        }
    }

    [Fact]
    public async Task Closing_ingestion_blocks_bulk_import()
    {
        var schema = await CreateSchemaAsync();
        var (serviceId, _, _) = await CreateServiceAccountAsync();

        var closed = await (await Admin.PutJsonAsync("/api/admin/configuration/ingestion", new IngestionStatusDto(true, "Frozen")))
            .ReadAsync<IngestionStatusDto>();
        Assert.True(closed.Closed);

        try
        {
            var content = $$"""{ "submissions": [ { "samples": [ { "schemaName": "{{schema.Name}}", "valueName": "count", "value": 5, "timestamp": "2026-01-01T00:00:00Z" } ] } ] }""";
            var response = await Admin.PostJsonAsync("/api/admin/submissions/import", new
            {
                serviceAccountId = serviceId,
                format = "Json",
                content,
            });
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            await Admin.PutJsonAsync("/api/admin/configuration/ingestion", new IngestionStatusDto(false, null));
        }
    }
}
