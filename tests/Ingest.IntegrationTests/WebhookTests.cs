using Ingest.Core.Abstractions;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>The outbound webhook pipeline with a faked HTTP transport: a live submission enqueues a
/// delivery, and draining POSTs it (captured by the recording handler) without any real network.</summary>
public sealed class WebhookTests : IntegrationTestBase
{
    public WebhookTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Submission_accepted_event_is_delivered_to_a_subscribed_endpoint()
    {
        Fixture.Factory.WebhookHandler.Clear();

        await Admin.PostJsonAsync("/api/admin/webhooks", new
        {
            name = $"hook-{Unique()}",
            url = "http://webhook-sink.test/ingest",
            enabled = true,
            events = new[] { "SubmissionAccepted" },
            generateSecret = true,
        });

        var schema = await CreateSchemaAsync();
        var (_, key, _) = await CreateServiceAccountAsync();
        var submissionId = Guid.Empty;
        using (var s = Fixture.CreateClient(key))
            submissionId = await SubmitSampleAsync(s, schema.Name, 5);

        // The delivery is enqueued in the (real, Mongo-backed) outbox.
        var deliveries = await (await Admin.GetAsync("/api/admin/webhooks/deliveries?pageSize=200")).ReadJsonAsync();
        Assert.Contains(deliveries.GetProperty("items").EnumerateArray(),
            d => d.GetProperty("event").GetString() == "submission.accepted");

        // Draining POSTs it through the fake handler.
        var result = await (await Admin.PostJsonAsync("/api/admin/webhooks/drain", new { })).ReadAsync<WebhookDrainResult>();
        Assert.True(result.Sent >= 1);

        var captured = Fixture.Factory.WebhookHandler.Requests;
        Assert.Contains(captured, r => r.Body.Contains("submission.accepted"));
        // The signed endpoint should have produced a signature header.
        Assert.Contains(captured, r => r.Headers.ContainsKey("X-Ingest-Signature"));
    }
}
