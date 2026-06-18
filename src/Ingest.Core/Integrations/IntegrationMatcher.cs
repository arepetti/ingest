using Ingest.Core.Entities;

namespace Ingest.Core.Integrations;

/// <summary>
/// Pure matching of an <see cref="Integration"/> against a concrete (service, schema) pair. Kept
/// free of I/O so the match logic is trivially unit-testable; mirrors <c>ApprovalRuleMatcher</c>.
/// </summary>
public static class IntegrationMatcher
{
    /// <summary>
    /// True when <paramref name="integration"/> applies to the given service and schema. The
    /// integration must be enabled; an empty <see cref="Integration.ServiceIds"/> matches every
    /// service and an empty <see cref="Integration.SchemaIds"/> matches every schema. A
    /// <paramref name="schemaId"/> of <c>null</c> still matches an "all schemas" integration.
    /// </summary>
    /// <param name="integration">The integration to test.</param>
    /// <param name="serviceId">The service account being inspected.</param>
    /// <param name="schemaId">The schema being inspected, or <c>null</c> when it can't be resolved.</param>
    public static bool Matches(Integration integration, Guid serviceId, Guid? schemaId)
    {
        if (!integration.Enabled) return false;

        var serviceMatches = integration.ServiceIds.Count == 0 || integration.ServiceIds.Contains(serviceId);
        if (!serviceMatches) return false;

        return integration.SchemaIds.Count == 0 || (schemaId is { } id && integration.SchemaIds.Contains(id));
    }
}
