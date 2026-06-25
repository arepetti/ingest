using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>The email pipeline end-to-end with a faked SMTP transport: enqueue -> outbox -> drain
/// -> sender. No mail ever leaves the process.</summary>
public sealed class NotificationTests : IntegrationTestBase
{
    public NotificationTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Adhoc_email_is_queued_and_delivered_through_the_fake_sender()
    {
        // SMTP must be "configured" for the dispatcher to attempt delivery (the fake sender stands
        // in for the actual transport).
        await Admin.PutJsonAsync("/api/admin/email/settings", new
        {
            host = "localhost",
            port = 25,
            useStartTls = false,
            username = (string?)null,
            fromAddress = "ingest@test.local",
            fromName = "Ingest",
            updatePassword = false,
        });

        var email = $"svc-{Unique()}@test.local";
        var account = await (await Admin.PostJsonAsync("/api/admin/accounts", new
        {
            name = $"svc-{Unique()}",
            email,
            kind = "Application",
            role = "Service",
            enabled = true,
        })).ReadAsync<AccountDto>();

        var subject = $"Integration ping {Unique()}";
        var send = await Admin.PostJsonAsync("/api/admin/email/send", new { accountId = account.Id, subject, body = "hello" });
        send.EnsureSuccessStatusCode();

        // It lands in the outbox first.
        var outbox = await (await Admin.GetAsync("/api/admin/email/outbox?pageSize=200"))
            .ReadAsync<PagedResponse<EmailMessageDto>>();
        Assert.Contains(outbox.Items, m => m.Subject == subject);

        // Draining delivers it via the fake sender.
        var result = await (await Admin.PostJsonAsync("/api/admin/email/drain", new { })).ReadAsync<EmailDrainResult>();
        Assert.True(result.Sent >= 1);

        Assert.Contains(Fixture.Factory.EmailSender.Sent, m => m.Subject == subject && m.ToAddress == email);
    }
}
