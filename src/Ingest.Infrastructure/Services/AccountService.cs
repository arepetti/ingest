using System.Net.Mail;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Core.Security;

namespace Ingest.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="IAccountService"/>. Owns the uniqueness rule on account
/// names (checked against soft-deleted rows too) and the "update only the mutable fields"
/// behaviour expected by callers. Pure repository forwarding for the rest.
/// </summary>
public sealed class AccountService : IAccountService
{
    private readonly IAccountRepository _accounts;
    private readonly ISampleRepository _samples;
    private readonly IAuditLogService _audit;

    /// <summary>Create a new <see cref="AccountService"/>.</summary>
    /// <param name="accounts">Account repository.</param>
    /// <param name="samples">Sample projection repository (used by the delete guard to detect accounts that already own historical data).</param>
    /// <param name="audit">Audit log used to record create/edit/delete changes.</param>
    public AccountService(IAccountRepository accounts, ISampleRepository samples, IAuditLogService audit)
    {
        _accounts = accounts;
        _samples = samples;
        _audit = audit;
    }

    /// <summary>An account row maps to a <see cref="AuditTargetType.User"/> or <see cref="AuditTargetType.Account"/> audit target depending on its kind.</summary>
    private static AuditTargetType TargetTypeFor(Account account) =>
        account.Kind == AccountKind.User ? AuditTargetType.User : AuditTargetType.Account;

    /// <inheritdoc />
    public Task<PagedResult<Account>> ListAsync(PageRequest request, AccountKind? kind, AccountRole? role, CancellationToken ct = default) =>
        _accounts.ListAsync(request, kind, role, ct);

    /// <inheritdoc />
    public Task<Account?> GetAsync(Guid id, bool includeDeleted, CancellationToken ct = default) =>
        _accounts.GetByIdAsync(id, includeDeleted, ct);

    /// <inheritdoc />
    public async Task<Account> CreateAsync(Account input, CancellationToken ct = default)
    {
        // Soft-deleted accounts still hold their name slot in the unique index. If the caller is
        // recreating an account that was previously deleted, hard-delete the old row first so the
        // fresh insert can take the slot. The samples that originated from the deleted account
        // remain in place but are filtered out of every read path by their own IsDeleted flag —
        // and that flag was set when the account was deleted (delete is now refused while live
        // samples still reference an account, so by the time we get here those samples have
        // been removed too).
        var collision = await _accounts.GetByNameAsync(input.Name, includeDeleted: true, ct);
        if (collision is not null)
        {
            if (!collision.IsDeleted)
                throw new ConflictException(Diagnostic.Create(
                    DiagnosticCodes.Accounts.AlreadyExists,
                    $"Account '{input.Name}' already exists.",
                    ("accountName", input.Name)));

            await _accounts.HardDeleteAsync(collision.Id, ct);
        }

        input.Email = NormalizeAndValidateEmail(input.Email);
        input.Area = NormalizeArea(input.Area);
        input.ExternalLogins = await NormalizeAndValidateLinksAsync(input.Id, input.Kind, input.ExternalLogins, preserveSubjectsFrom: null, ct);
        input.Capabilities = NormalizeAndValidateCapabilities(input.Role, input.Capabilities);
        input.AssignedServiceIds = await NormalizeAndValidateAssignedServicesAsync(input.Role, input.AssignedServiceIds, ct);

        await _accounts.AddAsync(input, ct);
        await _audit.RecordAsync(TargetTypeFor(input), AuditChangeType.Create, input.Id, input.Name, ct);
        return input;
    }

    /// <inheritdoc />
    public async Task<Account?> UpdateAsync(Guid id, AccountUpdate update, CancellationToken ct = default)
    {
        var existing = await _accounts.GetByIdAsync(id, ct: ct);
        if (existing is null) return null;

        existing.Label = update.Label;
        existing.Description = update.Description;
        existing.Email = NormalizeAndValidateEmail(update.Email);
        existing.Area = NormalizeArea(update.Area);
        existing.Role = update.Role;
        existing.Enabled = update.Enabled;

        // A null list means "leave links as they are"; a (possibly empty) list replaces them.
        // Surviving links keep any subject bound by a previous successful SSO login.
        if (update.ExternalLogins is not null)
            existing.ExternalLogins = await NormalizeAndValidateLinksAsync(existing.Id, existing.Kind, update.ExternalLogins, preserveSubjectsFrom: existing.ExternalLogins, ct);

        // Same null/empty contract as links: null leaves the stored overrides untouched, a
        // (possibly empty) list replaces them. An Admin always resolves to the full catalogue, so
        // overrides are normalised away for that role.
        if (update.Capabilities is not null)
            existing.Capabilities = NormalizeAndValidateCapabilities(existing.Role, update.Capabilities);
        else if (existing.Role == AccountRole.Admin)
            existing.Capabilities = new();

        // Same null/empty contract as capabilities: null leaves the stored allowlist untouched, a
        // (possibly empty) list replaces it. An Admin always sees every service, so the allowlist is
        // normalised away for that role.
        if (update.AssignedServiceIds is not null)
            existing.AssignedServiceIds = await NormalizeAndValidateAssignedServicesAsync(existing.Role, update.AssignedServiceIds, ct);
        else if (existing.Role == AccountRole.Admin)
            existing.AssignedServiceIds = new();

        await _accounts.UpdateAsync(existing, ct);
        await _audit.RecordAsync(TargetTypeFor(existing), AuditChangeType.Edit, existing.Id, existing.Name, ct);
        return existing;
    }

    /// <summary>
    /// Trim + lower-case the contact email and validate its shape. Blank/null is allowed (returns
    /// <c>null</c>) so legacy accounts and the bootstrap admin can have none; a non-empty value
    /// that isn't a valid address is rejected with a friendly <see cref="ValidationException"/>.
    /// </summary>
    private static string? NormalizeAndValidateEmail(string? email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        if (!MailAddress.TryCreate(trimmed, out _))
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Accounts.InvalidEmail,
                    $"'{trimmed}' is not a valid email address.",
                    ("email", trimmed)),
            });

        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Normalise the informative-only area tag: trim, and treat blank as unset. Deliberately does
    /// <em>not</em> validate against the configured areas list — the field is always optional and a
    /// later change to that list must never invalidate existing accounts.
    /// </summary>
    private static string? NormalizeArea(string? area)
    {
        var trimmed = area?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Validate and normalise an account's capability override set. Unknown capability strings are
    /// rejected (typo-safety). Admins implicitly hold every capability, so their overrides are
    /// meaningless and normalised to empty. Otherwise the input is de-duplicated and stored verbatim
    /// (an empty result simply means "follow the role default bundle").
    /// </summary>
    /// <param name="role">The account's role (drives the Admin special-case).</param>
    /// <param name="capabilities">The requested override set.</param>
    private static List<string> NormalizeAndValidateCapabilities(AccountRole role, IEnumerable<string>? capabilities)
    {
        if (role == AccountRole.Admin) return new();

        var input = (capabilities ?? Enumerable.Empty<string>())
            .Select(c => c?.Trim() ?? string.Empty)
            .Where(c => c.Length > 0)
            .ToList();
        if (input.Count == 0) return new();

        var unknown = input.Where(c => !Capabilities.IsKnown(c)).Distinct(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Accounts.UnknownCapabilities,
                    $"Unknown capabilities: {string.Join(", ", unknown)}.",
                    ("capabilities", unknown)),
            });

        return input.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Validate and normalise an account's assigned-service allowlist. Admins always see every
    /// service, so their allowlist is meaningless and normalised to empty. Otherwise the input is
    /// de-duplicated and every id is checked to be an existing (live) <see cref="AccountRole.Service"/>
    /// account — unknown or non-service ids are rejected so the scope can't silently drift. An empty
    /// result simply means "unrestricted" (sees every service).
    /// </summary>
    /// <param name="role">The account's role (drives the Admin special-case).</param>
    /// <param name="assignedServiceIds">The requested allowlist.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<List<Guid>> NormalizeAndValidateAssignedServicesAsync(AccountRole role, IEnumerable<Guid>? assignedServiceIds, CancellationToken ct)
    {
        if (role == AccountRole.Admin) return new();

        var input = (assignedServiceIds ?? Enumerable.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (input.Count == 0) return new();

        var unknown = new List<Guid>();
        foreach (var id in input)
        {
            var account = await _accounts.GetByIdAsync(id, ct: ct);
            if (account is null || account.Role != AccountRole.Service)
                unknown.Add(id);
        }

        if (unknown.Count > 0)
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Accounts.InvalidAssignedServices,
                    $"Assigned services must be existing service accounts. Unknown or non-service ids: {string.Join(", ", unknown)}.",
                    ("serviceIds", unknown)),
            });

        return input;
    }

    /// <summary>
    /// Normalise (trim provider, lower-case email), validate and de-duplicate a set of SSO links.
    /// Enforces the two business rules from the plan: only <see cref="AccountKind.User"/> accounts
    /// may hold links, and a (provider, email) pair must be unique across every account. When
    /// <paramref name="preserveSubjectsFrom"/> is supplied, subjects already bound to the same
    /// (provider, email) pair are carried over so an edit doesn't drop the binding.
    /// </summary>
    private async Task<List<ExternalLogin>> NormalizeAndValidateLinksAsync(
        Guid accountId,
        AccountKind kind,
        IEnumerable<ExternalLogin>? links,
        IReadOnlyList<ExternalLogin>? preserveSubjectsFrom,
        CancellationToken ct)
    {
        var input = (links ?? Enumerable.Empty<ExternalLogin>()).ToList();
        if (input.Count == 0) return new List<ExternalLogin>();

        if (kind != AccountKind.User)
            throw new ValidationException(new[]
            {
                Diagnostic.Create(
                    DiagnosticCodes.Accounts.SsoLinksUserOnly,
                    "Only User-kind accounts can have SSO sign-in links.",
                    ("accountKind", kind.ToString())),
            });

        var errors = new List<Diagnostic>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ExternalLogin>();

        foreach (var link in input)
        {
            var provider = (link.Provider ?? string.Empty).Trim();
            var email = (link.Email ?? string.Empty).Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(email))
            {
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Accounts.SsoLinkFieldsRequired,
                    "Each SSO link needs both a provider and an email.",
                    ("provider", provider),
                    ("email", email)));
                continue;
            }

            var dedupeKey = $"{provider}|{email}";
            if (!seen.Add(dedupeKey))
            {
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Accounts.DuplicateSsoLink,
                    $"Duplicate SSO link for {provider} / {email}.",
                    ("provider", provider),
                    ("email", email)));
                continue;
            }

            // Uniqueness across accounts: the pair may only be claimed by this account.
            var owner = await _accounts.GetByExternalLoginAsync(provider, email, ct);
            if (owner is not null && owner.Id != accountId)
                errors.Add(Diagnostic.Create(
                    DiagnosticCodes.Accounts.SsoLinkInUse,
                    $"The {provider} identity '{email}' is already linked to another account.",
                    ("provider", provider),
                    ("email", email)));

            var subject = link.Subject
                ?? preserveSubjectsFrom?.FirstOrDefault(p =>
                        string.Equals(p.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase))?.Subject;

            result.Add(new ExternalLogin { Provider = provider, Email = email, Subject = subject });
        }

        if (errors.Count > 0) throw new ValidationException(errors);
        return result;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // Same reasoning as `SchemaService.DeleteAsync`: an account that owns live submissions
        // can't be soft-deleted without orphaning its samples in the projection. The safer
        // workflow is to disable the account so it can no longer authenticate while history
        // (and the audit trail) remains intact.
        var existing = await _accounts.GetByIdAsync(id, ct: ct);
        if (existing is null) return; // idempotent: nothing to delete

        if (await _samples.IsAccountInUseAsync(id, ct))
        {
            var displayName = existing.Label ?? existing.Name;
            throw new ConflictException(Diagnostic.Create(
                DiagnosticCodes.Accounts.DeleteInUse,
                $"Account '{displayName}' has submitted data and cannot be deleted. " +
                "Disable it instead to revoke access while keeping the history intact.",
                ("accountId", id),
                ("accountName", existing.Name),
                ("displayName", displayName)));
        }

        await _accounts.SoftDeleteAsync(id, ct);
        await _audit.RecordAsync(TargetTypeFor(existing), AuditChangeType.Delete, existing.Id, existing.Name, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountBackupEntry>> ExportAsync(CancellationToken ct = default)
    {
        // One large page is plenty for the registry sizes this convenience tool targets; the
        // repository clamps PageSize to 500.
        var page = await _accounts.ListAsync(new PageRequest(1, 500), null, null, ct);
        return page.Items
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AccountBackupEntry(
                a.Name, a.Label, a.Description, a.Email, a.Area, a.Kind, a.Role, a.Enabled,
                a.Capabilities.ToList(),
                a.ExternalLogins.Select(l => new AccountBackupLogin(l.Provider, l.Email)).ToList(),
                a.AssignedServiceIds.ToList()))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<AccountsImportResult> ImportAsync(IReadOnlyList<AccountBackupEntry> accounts, CancellationToken ct = default)
    {
        var created = 0;
        var updated = 0;
        var errors = new List<string>();
        var errorDetails = new List<Diagnostic>();

        foreach (var entry in accounts)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                const string message = "Skipped an account with no name.";
                errors.Add(message);
                errorDetails.Add(Diagnostic.Create(
                    DiagnosticCodes.Imports.AccountEntryNameMissing,
                    message,
                    ("accountName", null)));
                continue;
            }

            try
            {
                var links = entry.ExternalLogins
                    .Select(l => new ExternalLogin { Provider = l.Provider, Email = l.Email })
                    .ToList();

                // Match on the unique name. A live account is updated in place; anything else
                // (missing, or only present as a soft-deleted row) is created — CreateAsync frees a
                // soft-deleted name slot automatically.
                var existing = await _accounts.GetByNameAsync(entry.Name, includeDeleted: false, ct);
                if (existing is not null)
                {
                    await UpdateAsync(existing.Id, new AccountUpdate(
                        entry.Label, entry.Description, entry.Email, entry.Role, entry.Enabled,
                        links, entry.Capabilities.ToList(), entry.AssignedServiceIds.ToList(), entry.Area), ct);
                    updated++;
                }
                else
                {
                    await CreateAsync(new Account
                    {
                        Name = entry.Name,
                        Label = entry.Label,
                        Description = entry.Description,
                        Email = entry.Email,
                        Area = entry.Area,
                        Kind = entry.Kind,
                        Role = entry.Role,
                        Enabled = entry.Enabled,
                        Capabilities = entry.Capabilities.ToList(),
                        ExternalLogins = links,
                        AssignedServiceIds = entry.AssignedServiceIds.ToList(),
                    }, ct);
                    created++;
                }
            }
            catch (Exception ex) when (ex is ValidationException or ConflictException)
            {
                var message = $"'{entry.Name}': {ex.Message}";
                errors.Add(message);
                var cause = ((DomainException)ex).Diagnostic;
                errorDetails.Add(Diagnostic.Create(
                    DiagnosticCodes.Imports.AccountEntry,
                    message,
                    ("accountName", entry.Name),
                    ("causeCode", cause.Code),
                    ("causeMessage", cause.Message),
                    ("causeParams", cause.Params)));
            }
        }

        return new AccountsImportResult(created, updated, errors)
        {
            ErrorDetails = errorDetails,
        };
    }
}
