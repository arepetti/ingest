using Fluid;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Email;

/// <summary>Database-backed editable email templates with built-in seeding.</summary>
public sealed class EmailTemplateService : IEmailTemplateService
{
    private static readonly FluidParser _parser = new();
    private readonly MongoContext _ctx;
    private readonly IAuditContext _audit;

    /// <summary>Create a new <see cref="EmailTemplateService"/>.</summary>
    public EmailTemplateService(MongoContext ctx, IAuditContext audit)
    {
        _ctx = ctx;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task SeedDefaultsAsync(CancellationToken ct = default)
    {
        foreach (var template in DefaultEmailTemplates.All)
        {
            var exists = await _ctx.EmailTemplates
                .Find(Builders<EmailTemplate>.Filter.Eq(t => t.Key, template.Key))
                .AnyAsync(ct);
            if (exists) continue;

            var now = _audit.UtcNow;
            var copy = new EmailTemplate
            {
                Key = template.Key,
                Name = template.Name,
                Description = template.Description,
                Subject = template.Subject,
                HtmlBody = template.HtmlBody,
                TextBody = template.TextBody,
                CreatedAt = now,
                ModifiedAt = now,
            };
            // Insert is racy across replicas; ignore the duplicate-key if two hosts seed at once.
            try { await _ctx.EmailTemplates.InsertOneAsync(copy, cancellationToken: ct); }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey) { }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailTemplate>> ListAsync(CancellationToken ct = default) =>
        await _ctx.EmailTemplates.Find(FilterDefinition<EmailTemplate>.Empty).SortBy(t => t.Key).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<EmailTemplate> GetAsync(string key, CancellationToken ct = default)
    {
        var template = await _ctx.EmailTemplates
            .Find(Builders<EmailTemplate>.Filter.Eq(t => t.Key, key))
            .FirstOrDefaultAsync(ct);
        return template ?? throw new NotFoundException("Email template");
    }

    /// <inheritdoc />
    public async Task<EmailTemplate> UpdateAsync(string key, EmailTemplateUpdate update, CancellationToken ct = default)
    {
        var template = await GetAsync(key, ct);

        var errors = new List<string>();
        ValidateLiquid("Subject", update.Subject, errors);
        ValidateLiquid("Text body", update.TextBody, errors);
        if (!string.IsNullOrWhiteSpace(update.HtmlBody)) ValidateLiquid("HTML body", update.HtmlBody, errors);
        if (string.IsNullOrWhiteSpace(update.Subject)) errors.Add("Subject is required.");
        if (string.IsNullOrWhiteSpace(update.TextBody)) errors.Add("Text body is required.");
        if (errors.Count > 0) throw new ValidationException(errors);

        template.Name = update.Name?.Trim() ?? template.Name;
        template.Description = update.Description;
        template.Subject = update.Subject;
        template.HtmlBody = string.IsNullOrWhiteSpace(update.HtmlBody) ? null : update.HtmlBody;
        template.TextBody = update.TextBody;
        template.ModifiedAt = _audit.UtcNow;
        template.ModifiedBy = _audit.UserName;

        await _ctx.EmailTemplates.ReplaceOneAsync(
            Builders<EmailTemplate>.Filter.Eq(t => t.Id, template.Id), template, cancellationToken: ct);
        return template;
    }

    private static void ValidateLiquid(string field, string? template, List<string> errors)
    {
        if (string.IsNullOrEmpty(template)) return;
        if (!_parser.TryParse(template, out _, out var error))
            errors.Add($"{field}: Liquid failed to parse: {error}");
    }
}
