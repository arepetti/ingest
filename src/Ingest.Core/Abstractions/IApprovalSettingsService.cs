using Ingest.Core.Entities;

namespace Ingest.Core.Abstractions;

/// <summary>
/// Read/write access to the server-wide global default approval policy that schemas can defer to
/// via <see cref="ApprovalMode.UseGlobalDefault"/>. Backed by a singleton document; an absent
/// document reads back as <see cref="ApprovalMode.None"/> (no default approval).
/// </summary>
public interface IApprovalSettingsService
{
    /// <summary>Fetch the global default approval policy (never <c>null</c>; defaults to <see cref="ApprovalMode.None"/>).</summary>
    Task<ApprovalPolicy> GetDefaultAsync(CancellationToken ct = default);

    /// <summary>Replace the global default approval policy. Validated (>= 1 required approver when Mode is Required).</summary>
    Task<ApprovalPolicy> UpdateDefaultAsync(ApprovalPolicy policy, CancellationToken ct = default);
}
