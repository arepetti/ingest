using Ingest.Api.Models;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>The draft workflow: drafts are excluded from the live feed until they are published.</summary>
public sealed class DraftTests : IntegrationTestBase
{
    public DraftTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Draft_is_excluded_until_published()
    {
        var schema = await CreateSchemaAsync();
        var (_, apiKey, _) = await CreateServiceAccountAsync();
        using var service = Fixture.CreateClient(apiKey);

        var submissionId = await SubmitSampleAsync(service, schema.Name, value: 10, draft: true);

        var draft = await (await service.GetAsync($"/api/submissions/{submissionId}")).ReadAsync<SubmissionDto>();
        Assert.True(draft.IsDraft);
        Assert.Equal(0, await CountSamplesAsync(schema.Name));

        // Publish by replacing it with draft=false.
        var publishBody = new
        {
            samples = new[]
            {
                new { schemaName = schema.Name, valueName = "count", value = 10, timestamp = DateTime.UtcNow, note = (string?)null },
            },
        };
        var published = await (await service.PutJsonAsync($"/api/submissions/{submissionId}?draft=false", publishBody))
            .ReadAsync<SubmissionWriteResponse>();
        Assert.Equal(submissionId, published.Id);

        var live = await (await service.GetAsync($"/api/submissions/{submissionId}")).ReadAsync<SubmissionDto>();
        Assert.False(live.IsDraft);
        Assert.Equal(1, await CountSamplesAsync(schema.Name));
    }
}
