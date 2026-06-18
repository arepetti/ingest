using Ingest.Core.Approvals;
using Ingest.Core.Entities;

namespace Ingest.Tests;

/// <summary>
/// Unit tests for the pure cross-cutting rule matcher (<see cref="ApprovalRuleMatcher"/>). These
/// pin down the "does this rule apply to this (service, schema)?" decision, including the
/// empty-means-all semantics on either axis and the disabled-rule short-circuit.
/// </summary>
public class ApprovalRuleMatcherTests
{
    private static readonly Guid ServiceA = Guid.NewGuid();
    private static readonly Guid ServiceB = Guid.NewGuid();
    private static readonly Guid SchemaX = Guid.NewGuid();
    private static readonly Guid SchemaY = Guid.NewGuid();

    private static ApprovalRule Rule(IEnumerable<Guid>? services = null, IEnumerable<Guid>? schemas = null, bool enabled = true) => new()
    {
        Enabled = enabled,
        ServiceIds = services?.ToList() ?? new(),
        SchemaIds = schemas?.ToList() ?? new(),
        Policy = new ApprovalPolicy { Mode = ApprovalMode.Required },
    };

    [Fact]
    public void Specific_service_and_schema_match_only_that_pair()
    {
        var rule = Rule(new[] { ServiceA }, new[] { SchemaX });

        Assert.True(ApprovalRuleMatcher.Matches(rule, ServiceA, SchemaX));
        Assert.False(ApprovalRuleMatcher.Matches(rule, ServiceB, SchemaX));
        Assert.False(ApprovalRuleMatcher.Matches(rule, ServiceA, SchemaY));
    }

    [Fact]
    public void Empty_service_list_matches_all_services()
    {
        var rule = Rule(services: null, schemas: new[] { SchemaX });

        Assert.True(ApprovalRuleMatcher.Matches(rule, ServiceA, SchemaX));
        Assert.True(ApprovalRuleMatcher.Matches(rule, ServiceB, SchemaX));
        Assert.False(ApprovalRuleMatcher.Matches(rule, ServiceA, SchemaY));
    }

    [Fact]
    public void Empty_schema_list_matches_all_schemas()
    {
        var rule = Rule(new[] { ServiceA }, schemas: null);

        Assert.True(ApprovalRuleMatcher.Matches(rule, ServiceA, SchemaX));
        Assert.True(ApprovalRuleMatcher.Matches(rule, ServiceA, SchemaY));
        Assert.False(ApprovalRuleMatcher.Matches(rule, ServiceB, SchemaX));
    }

    [Fact]
    public void Both_lists_empty_matches_everything()
    {
        var rule = Rule();

        Assert.True(ApprovalRuleMatcher.Matches(rule, ServiceA, SchemaX));
        Assert.True(ApprovalRuleMatcher.Matches(rule, ServiceB, SchemaY));
    }

    [Fact]
    public void Null_schema_id_still_matches_an_all_schemas_rule()
    {
        // When the submission's schema can't be resolved, an "all schemas" rule must still apply.
        var allSchemas = Rule(new[] { ServiceA }, schemas: null);
        Assert.True(ApprovalRuleMatcher.Matches(allSchemas, ServiceA, schemaId: null));

        // A rule that names specific schemas can't match an unknown schema.
        var specific = Rule(new[] { ServiceA }, new[] { SchemaX });
        Assert.False(ApprovalRuleMatcher.Matches(specific, ServiceA, schemaId: null));
    }

    [Fact]
    public void Disabled_rule_never_matches()
    {
        var rule = Rule(enabled: false);
        Assert.False(ApprovalRuleMatcher.Matches(rule, ServiceA, SchemaX));
    }

    [Fact]
    public void Multiple_services_and_schemas_match_any_listed_pair()
    {
        var rule = Rule(new[] { ServiceA, ServiceB }, new[] { SchemaX, SchemaY });

        Assert.True(ApprovalRuleMatcher.Matches(rule, ServiceA, SchemaY));
        Assert.True(ApprovalRuleMatcher.Matches(rule, ServiceB, SchemaX));
        Assert.False(ApprovalRuleMatcher.Matches(rule, Guid.NewGuid(), SchemaX));
    }
}
