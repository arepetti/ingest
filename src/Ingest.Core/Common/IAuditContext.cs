namespace Ingest.Core.Common;

/// <summary>
/// Ambient context describing "who is acting right now" and "what time is it". Implemented by
/// the HTTP layer (see <c>HttpAuditContext</c>) and consumed by repositories and services so
/// they can stamp <see cref="AuditedEntity"/> fields without taking a dependency on
/// ASP.NET Core.
/// </summary>
public interface IAuditContext
{
    /// <summary>Name of the calling account, or <c>null</c> when running outside an authenticated HTTP request (e.g. background tasks).</summary>
    string? UserName { get; }

    /// <summary>Id of the calling account, or <c>null</c> as above.</summary>
    Guid? AccountId { get; }

    /// <summary>Current time in UTC. Centralised so tests can swap in a fixed clock.</summary>
    DateTime UtcNow { get; }
}
