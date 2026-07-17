using Ingest.Infrastructure.Configuration;

namespace Ingest.Tests;

/// <summary>
/// Tests for the area-list normalization applied by <see cref="AppConfigurationService"/> before it
/// persists the configuration. The persistence itself is Mongo-backed and covered by integration
/// tests; here we pin the pure trim/dedupe/order semantics.
/// </summary>
public class AppConfigurationServiceTests
{
    [Fact]
    public void Normalize_trims_drops_blanks_and_preserves_order()
    {
        var result = AppConfigurationService.Normalize(new[] { "  North  ", "", "   ", "South" });
        Assert.Equal(new[] { "North", "South" }, result);
    }

    [Fact]
    public void Normalize_deduplicates_case_insensitively_keeping_the_first_spelling()
    {
        var result = AppConfigurationService.Normalize(new[] { "North", "north", "NORTH", "East" });
        Assert.Equal(new[] { "North", "East" }, result);
    }

    [Fact]
    public void Normalize_handles_a_null_input()
    {
        Assert.Empty(AppConfigurationService.Normalize(null));
    }
}
