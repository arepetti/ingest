using System.Net;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>Authentication happy path and the two obvious failure modes.</summary>
public sealed class AuthTests : IntegrationTestBase
{
    public AuthTests(IngestAppFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Me_with_bootstrap_admin_key_returns_admin_identity()
    {
        var me = await (await Admin.GetAsync("/api/me")).ReadJsonAsync();

        Assert.Equal("Admin", me.GetProperty("role").GetString());
        var capabilities = me.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()).ToList();
        // Admin implicitly holds the whole catalogue, so a couple of representative caps must be present.
        Assert.Contains("schemas:manage", capabilities);
        Assert.Contains("query:read", capabilities);
    }

    [Fact]
    public async Task Me_without_a_key_is_unauthorized()
    {
        using var anon = Fixture.CreateClient(apiKey: null);
        var response = await anon.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_a_bogus_key_is_unauthorized()
    {
        using var bad = Fixture.CreateClient("nope.not-a-real-key");
        var response = await bad.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_is_anonymous_and_returns_only_normalized_default_locale()
    {
        using var anon = Fixture.CreateClient(apiKey: null);
        var bootstrap = await (await anon.GetAsync("/api/bootstrap")).ReadJsonAsync();

        Assert.Equal("en-US", bootstrap.GetProperty("defaultLocale").GetString());
        Assert.Single(bootstrap.EnumerateObject());
    }
}
