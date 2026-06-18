using Ingest.Core.Entities;
using Ingest.Core.Integrations;

namespace Ingest.Tests;

/// <summary>
/// Unit tests for the pure integration matcher (<see cref="IntegrationMatcher"/>). These pin down
/// the "does this integration apply to this (service, schema)?" decision, including the
/// empty-means-all semantics on either axis and the disabled-integration short-circuit. Mirrors
/// <see cref="ApprovalRuleMatcherTests"/>.
/// </summary>
public class IntegrationMatcherTests
{
    private static readonly Guid ServiceA = Guid.NewGuid();
    private static readonly Guid ServiceB = Guid.NewGuid();
    private static readonly Guid SchemaX = Guid.NewGuid();
    private static readonly Guid SchemaY = Guid.NewGuid();

    private static Integration Integration(
        IEnumerable<Guid>? services = null,
        IEnumerable<Guid>? schemas = null,
        bool enabled = true) => new()
    {
        Enabled = enabled,
        ServiceIds = services?.ToList() ?? new(),
        SchemaIds = schemas?.ToList() ?? new(),
        Teams = new TeamsTarget { Kind = TeamsTargetKind.User, TargetId = "u" },
    };

    [Fact]
    public void Specific_service_and_schema_match_only_that_pair()
    {
        var i = Integration(new[] { ServiceA }, new[] { SchemaX });

        Assert.True(IntegrationMatcher.Matches(i, ServiceA, SchemaX));
        Assert.False(IntegrationMatcher.Matches(i, ServiceB, SchemaX));
        Assert.False(IntegrationMatcher.Matches(i, ServiceA, SchemaY));
    }

    [Fact]
    public void Empty_service_list_matches_all_services()
    {
        var i = Integration(services: null, schemas: new[] { SchemaX });

        Assert.True(IntegrationMatcher.Matches(i, ServiceA, SchemaX));
        Assert.True(IntegrationMatcher.Matches(i, ServiceB, SchemaX));
        Assert.False(IntegrationMatcher.Matches(i, ServiceA, SchemaY));
    }

    [Fact]
    public void Empty_schema_list_matches_all_schemas()
    {
        var i = Integration(new[] { ServiceA }, schemas: null);

        Assert.True(IntegrationMatcher.Matches(i, ServiceA, SchemaX));
        Assert.True(IntegrationMatcher.Matches(i, ServiceA, SchemaY));
        Assert.False(IntegrationMatcher.Matches(i, ServiceB, SchemaX));
    }

    [Fact]
    public void Both_lists_empty_matches_everything()
    {
        var i = Integration();

        Assert.True(IntegrationMatcher.Matches(i, ServiceA, SchemaX));
        Assert.True(IntegrationMatcher.Matches(i, ServiceB, SchemaY));
    }

    [Fact]
    public void Null_schema_id_still_matches_an_all_schemas_integration()
    {
        var allSchemas = Integration(new[] { ServiceA }, schemas: null);
        Assert.True(IntegrationMatcher.Matches(allSchemas, ServiceA, schemaId: null));

        var specific = Integration(new[] { ServiceA }, new[] { SchemaX });
        Assert.False(IntegrationMatcher.Matches(specific, ServiceA, schemaId: null));
    }

    [Fact]
    public void Disabled_integration_never_matches()
    {
        var i = Integration(enabled: false);
        Assert.False(IntegrationMatcher.Matches(i, ServiceA, SchemaX));
    }

    [Fact]
    public void Multiple_services_and_schemas_match_any_listed_pair()
    {
        var i = Integration(new[] { ServiceA, ServiceB }, new[] { SchemaX, SchemaY });

        Assert.True(IntegrationMatcher.Matches(i, ServiceA, SchemaY));
        Assert.True(IntegrationMatcher.Matches(i, ServiceB, SchemaX));
        Assert.False(IntegrationMatcher.Matches(i, Guid.NewGuid(), SchemaX));
    }
}
