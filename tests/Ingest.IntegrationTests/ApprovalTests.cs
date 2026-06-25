using Ingest.Api.Models;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>The approval gate: a submission against an approval-required schema is held out of the
/// live read model until an approver signs it off.</summary>
public sealed class ApprovalTests : IntegrationTestBase
{
    public ApprovalTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Pending_submission_is_hidden_until_approved()
    {
        var adminId = await AdminAccountIdAsync();
        var approval = new
        {
            mode = "Required",
            appliesToSources = "Both",
            approvers = new[] { new { accountId = adminId, requirement = "Required", kind = "Account" } },
        };
        var schema = await CreateSchemaAsync(approval: approval);
        var (_, apiKey, _) = await CreateServiceAccountAsync();
        using var service = Fixture.CreateClient(apiKey);

        var submissionId = await SubmitSampleAsync(service, schema.Name, value: 90);

        // Held pending — nothing in the live projection yet.
        var pending = await (await service.GetAsync($"/api/submissions/{submissionId}")).ReadAsync<SubmissionDto>();
        Assert.Equal("Pending", pending.ApprovalStatus.ToString());
        Assert.Equal(0, await CountSamplesAsync(schema.Name));

        // The admin approves; the submission goes live and the projection appears.
        var approved = await (await Admin.PostJsonAsync($"/api/admin/submissions/{submissionId}/approve", new { note = "ok" }))
            .ReadAsync<SubmissionDto>();
        Assert.Equal("Approved", approved.ApprovalStatus.ToString());
        Assert.Equal(1, await CountSamplesAsync(schema.Name));
    }

    [Fact]
    public async Task Rejected_submission_stays_out_of_the_live_feed()
    {
        var adminId = await AdminAccountIdAsync();
        var approval = new
        {
            mode = "Required",
            appliesToSources = "Both",
            approvers = new[] { new { accountId = adminId, requirement = "Required", kind = "Account" } },
        };
        var schema = await CreateSchemaAsync(approval: approval);
        var (_, apiKey, _) = await CreateServiceAccountAsync();
        using var service = Fixture.CreateClient(apiKey);

        var submissionId = await SubmitSampleAsync(service, schema.Name, value: 90);

        var rejected = await (await Admin.PostJsonAsync($"/api/admin/submissions/{submissionId}/reject", new { note = "no" }))
            .ReadAsync<SubmissionDto>();
        Assert.Equal("Rejected", rejected.ApprovalStatus.ToString());
        Assert.Equal(0, await CountSamplesAsync(schema.Name));
    }
}
