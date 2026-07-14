using System.Net;
using Ingest.Api.Models;
using Ingest.Core.Entities;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>The admin events timeline: CRUD, required-field validation, the "affects services"
/// scope, and soft-delete visibility.</summary>
public sealed class EventsTests : IntegrationTestBase
{
    public EventsTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Admin_can_create_list_update_and_delete_an_event()
    {
        var label = $"deploy-{Unique()}";
        var created = await (await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = DateTime.UtcNow,
            label,
            description = "Rolled out v1.2.3",
            serviceIds = Array.Empty<Guid>(),
        })).ReadAsync<EventDto>();

        Assert.Equal(label, created.Label);
        Assert.Empty(created.ServiceIds);
        Assert.Equal(EventKind.PointInTime, created.Kind);
        Assert.Null(created.DurationMinutes);

        var page = await (await Admin.GetAsync("/api/admin/events?pageSize=200")).ReadAsync<PagedResponse<EventDto>>();
        Assert.Contains(page.Items, e => e.Id == created.Id);

        var updated = await (await Admin.PutJsonAsync($"/api/admin/events/{created.Id}", new
        {
            timestamp = created.Timestamp,
            label = created.Label,
            description = "Rolled out v1.2.4",
            serviceIds = Array.Empty<Guid>(),
        })).ReadAsync<EventDto>();
        Assert.Equal("Rolled out v1.2.4", updated.Description);

        var deleteResponse = await Admin.DeleteAsync($"/api/admin/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var pageAfterDelete = await (await Admin.GetAsync("/api/admin/events?pageSize=200")).ReadAsync<PagedResponse<EventDto>>();
        Assert.DoesNotContain(pageAfterDelete.Items, e => e.Id == created.Id);
    }

    [Fact]
    public async Task Create_requires_a_label()
    {
        var response = await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = DateTime.UtcNow,
            label = "   ",
            serviceIds = Array.Empty<Guid>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_requires_a_non_default_timestamp()
    {
        var response = await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = default(DateTime),
            label = $"evt-{Unique()}",
            serviceIds = Array.Empty<Guid>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_a_service_id_that_is_not_a_real_service_account()
    {
        var response = await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = DateTime.UtcNow,
            label = $"evt-{Unique()}",
            serviceIds = new[] { Guid.NewGuid() },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_accepts_a_real_service_account_id_as_the_affected_scope()
    {
        var (serviceId, _, _) = await CreateServiceAccountAsync();

        var created = await (await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = DateTime.UtcNow,
            label = $"evt-{Unique()}",
            serviceIds = new[] { serviceId },
        })).ReadAsync<EventDto>();

        Assert.Equal(new[] { serviceId }, created.ServiceIds);
    }

    [Fact]
    public async Task Interval_event_requires_a_positive_duration()
    {
        var response = await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = DateTime.UtcNow,
            label = $"evt-{Unique()}",
            kind = "Interval",
            serviceIds = Array.Empty<Guid>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Interval_event_with_a_duration_round_trips_it_in_minutes()
    {
        var created = await (await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = DateTime.UtcNow,
            label = $"evt-{Unique()}",
            kind = "Interval",
            durationMinutes = 90,
            serviceIds = Array.Empty<Guid>(),
        })).ReadAsync<EventDto>();

        Assert.Equal(EventKind.Interval, created.Kind);
        Assert.Equal(90, created.DurationMinutes);
    }

    [Fact]
    public async Task FromNowOn_event_ignores_any_duration_it_is_sent()
    {
        var created = await (await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = DateTime.UtcNow,
            label = $"evt-{Unique()}",
            kind = "FromNowOn",
            durationMinutes = 45,
            serviceIds = Array.Empty<Guid>(),
        })).ReadAsync<EventDto>();

        Assert.Equal(EventKind.FromNowOn, created.Kind);
        Assert.Null(created.DurationMinutes);
    }

    [Fact]
    public async Task List_from_to_overlap_includes_an_interval_that_starts_before_the_window()
    {
        var label = $"evt-{Unique()}";
        var start = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = start,
            label,
            kind = "Interval",
            durationMinutes = 240, // ends 04:00 the same day
            serviceIds = Array.Empty<Guid>(),
        });

        // Window starts an hour after the interval started but well before it ends -> overlaps.
        var overlapping = await (await Admin.GetAsync(
            "/api/admin/events?pageSize=200&from=2026-04-01T01:00:00Z&to=2026-04-01T02:00:00Z"))
            .ReadAsync<PagedResponse<EventDto>>();
        Assert.Contains(overlapping.Items, e => e.Label == label);

        // Window entirely after the interval ended -> no overlap.
        var after = await (await Admin.GetAsync(
            "/api/admin/events?pageSize=200&from=2026-04-01T05:00:00Z&to=2026-04-01T06:00:00Z"))
            .ReadAsync<PagedResponse<EventDto>>();
        Assert.DoesNotContain(after.Items, e => e.Label == label);
    }

    [Fact]
    public async Task List_from_to_overlap_always_includes_a_FromNowOn_event_that_already_started()
    {
        var label = $"evt-{Unique()}";
        await Admin.PostJsonAsync("/api/admin/events", new
        {
            timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            label,
            kind = "FromNowOn",
            serviceIds = Array.Empty<Guid>(),
        });

        // Any window far in the future still overlaps an open-ended event that already started.
        var page = await (await Admin.GetAsync(
            "/api/admin/events?pageSize=200&from=2030-01-01T00:00:00Z&to=2030-02-01T00:00:00Z"))
            .ReadAsync<PagedResponse<EventDto>>();
        Assert.Contains(page.Items, e => e.Label == label);
    }
}
