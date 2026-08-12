using System.Net.Mail;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Email;

/// <summary>
/// Database-backed SMTP settings. There is at most one document; the first read seeds it from the
/// <c>Email:Smtp</c> configuration when present so a fresh deployment can come up pre-configured,
/// after which the database wins and configuration is ignored.
/// </summary>
public sealed class EmailSettingsService : IEmailSettingsService
{
    private readonly MongoContext _ctx;
    private readonly IEmailSecretProtector _protector;
    private readonly IAuditContext _audit;
    private readonly EmailOptions _options;

    /// <summary>Create a new <see cref="EmailSettingsService"/>.</summary>
    public EmailSettingsService(
        MongoContext ctx,
        IEmailSecretProtector protector,
        IAuditContext audit,
        IOptions<EmailOptions> options)
    {
        _ctx = ctx;
        _protector = protector;
        _audit = audit;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<EmailSettings> GetAsync(CancellationToken ct = default)
    {
        var existing = await _ctx.EmailSettings.Find(FilterDefinition<EmailSettings>.Empty).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;

        // No document yet — seed from configuration if an operator supplied SMTP values, otherwise
        // hand back an unpersisted blank so the dispatcher can report "not configured" cleanly.
        var seed = _options.Smtp;
        var now = _audit.UtcNow;
        var settings = new EmailSettings
        {
            Port = seed.Port,
            UseStartTls = seed.UseStartTls,
            CreatedAt = now,
            ModifiedAt = now,
            CreatedBy = _audit.UserName,
            ModifiedBy = _audit.UserName,
        };

        if (seed.HasSeed)
        {
            settings.Host = seed.Host!.Trim();
            settings.Username = string.IsNullOrWhiteSpace(seed.Username) ? null : seed.Username.Trim();
            settings.PasswordCipher = _protector.Protect(seed.Password);
            settings.FromAddress = seed.FromAddress?.Trim() ?? "";
            settings.FromName = string.IsNullOrWhiteSpace(seed.FromName) ? null : seed.FromName.Trim();
            await _ctx.EmailSettings.InsertOneAsync(settings, cancellationToken: ct);
        }

        return settings;
    }

    /// <inheritdoc />
    public async Task<EmailSettings> UpdateAsync(EmailSettingsUpdate update, CancellationToken ct = default)
    {
        var errors = new List<Diagnostic>();
        if (string.IsNullOrWhiteSpace(update.Host))
            errors.Add(new Diagnostic(DiagnosticCodes.Email.SmtpHostRequired, "SMTP host is required."));
        if (update.Port is < 1 or > 65535)
            errors.Add(Diagnostic.Create(
                DiagnosticCodes.Email.SmtpPortInvalid,
                "SMTP port must be between 1 and 65535.",
                ("port", update.Port),
                ("minimum", 1),
                ("maximum", 65535)));
        if (string.IsNullOrWhiteSpace(update.FromAddress))
            errors.Add(new Diagnostic(DiagnosticCodes.Email.FromAddressRequired, "From address is required."));
        else if (!MailAddress.TryCreate(update.FromAddress.Trim(), out _))
            errors.Add(Diagnostic.Create(
                DiagnosticCodes.Email.AddressInvalid,
                $"'{update.FromAddress}' is not a valid email address.",
                ("address", update.FromAddress)));
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var existing = await _ctx.EmailSettings.Find(FilterDefinition<EmailSettings>.Empty).FirstOrDefaultAsync(ct);
        var now = _audit.UtcNow;
        var settings = existing ?? new EmailSettings { CreatedAt = now, CreatedBy = _audit.UserName };

        settings.Host = update.Host.Trim();
        settings.Port = update.Port;
        settings.UseStartTls = update.UseStartTls;
        settings.Username = string.IsNullOrWhiteSpace(update.Username) ? null : update.Username.Trim();
        settings.FromAddress = update.FromAddress.Trim();
        settings.FromName = string.IsNullOrWhiteSpace(update.FromName) ? null : update.FromName.Trim();

        // Write-only password: keep the stored cipher untouched unless the caller explicitly opts
        // to change it (a blank new value clears it).
        if (update.UpdatePassword)
            settings.PasswordCipher = _protector.Protect(update.Password);

        settings.ModifiedAt = now;
        settings.ModifiedBy = _audit.UserName;

        await _ctx.EmailSettings.ReplaceOneAsync(
            Builders<EmailSettings>.Filter.Eq(s => s.Id, settings.Id),
            settings,
            new ReplaceOptions { IsUpsert = true },
            ct);

        return settings;
    }
}
