using Ingest.Core.Entities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace Ingest.Infrastructure.Mongo;

/// <summary>
/// One-shot configuration helpers for the MongoDB driver. Both methods are idempotent and safe
/// to call on every host start.
/// </summary>
public static class MongoSetup
{
    private static int _registered;

    /// <summary>
    /// Register the global serialisation conventions used across the domain: camelCase element
    /// names, enums as strings, extra-element tolerance, and the standard <see cref="Guid"/>
    /// representation. Safe to call multiple times — only the first call has an effect.
    /// </summary>
    public static void RegisterClassMaps()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;

        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreExtraElementsConvention(true),
        };
        ConventionRegistry.Register("ingest", pack, _ => true);

        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

        // Warnings used to be stored as a plain array of strings and are now structured
        // (value name + message). The custom serializer reads both on-disk shapes so existing
        // submissions deserialize unchanged — no data migration needed. Must be registered
        // before the first Submission (de)serialization; RegisterClassMaps runs at host start.
        BsonSerializer.RegisterSerializer(new SubmissionWarningBsonSerializer());
    }

    /// <summary>
    /// Create the unique and lookup indexes used by the repositories. Mongo's
    /// <c>CreateIndexAsync</c> is idempotent, so calling this on every host start is cheap.
    /// </summary>
    /// <param name="ctx">Mongo context.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task EnsureIndexesAsync(MongoContext ctx, CancellationToken ct = default)
    {
        await ctx.Accounts.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<Account>(
                Builders<Account>.IndexKeys.Ascending(a => a.Name),
                new CreateIndexOptions { Unique = true, Name = "uniq_name" }),
            // Multikey lookup index backing GetByExternalLoginAsync. Not unique — uniqueness of
            // (provider, email) pairs across accounts is enforced in the account service so we
            // can return a friendly conflict rather than a raw duplicate-key error.
            new CreateIndexModel<Account>(
                Builders<Account>.IndexKeys
                    .Ascending("externalLogins.provider")
                    .Ascending("externalLogins.email"),
                new CreateIndexOptions { Name = "by_external_login", Sparse = true }),
        }, cancellationToken: ct);

        await ctx.ApiKeys.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<ApiKey>(
                Builders<ApiKey>.IndexKeys.Ascending(k => k.KeyId),
                new CreateIndexOptions { Unique = true, Name = "uniq_keyId" }),
            new CreateIndexModel<ApiKey>(
                Builders<ApiKey>.IndexKeys.Ascending(k => k.AccountId),
                new CreateIndexOptions { Name = "by_account" }),
        }, cancellationToken: ct);

        await ctx.Schemas.Indexes.CreateOneAsync(
            new CreateIndexModel<Schema>(
                Builders<Schema>.IndexKeys.Ascending(s => s.Name),
                new CreateIndexOptions { Unique = true, Name = "uniq_name" }),
            cancellationToken: ct);

        await ctx.SchemaVersionHistories.Indexes.CreateOneAsync(
            new CreateIndexModel<SchemaVersionHistory>(
                // The version-history page browses a single schema newest-change-first.
                Builders<SchemaVersionHistory>.IndexKeys
                    .Ascending(h => h.SchemaName)
                    .Descending(h => h.ChangeDate),
                new CreateIndexOptions { Name = "by_schema_change" }),
            cancellationToken: ct);

        await ctx.Submissions.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<Submission>(
                Builders<Submission>.IndexKeys.Ascending(s => s.ServiceAccountId).Descending(s => s.SubmittedAt),
                new CreateIndexOptions { Name = "by_service_time" }),
            // Backs the pending-approvals queue + count card.
            new CreateIndexModel<Submission>(
                Builders<Submission>.IndexKeys.Ascending(s => s.ApprovalStatus).Descending(s => s.SubmittedAt),
                new CreateIndexOptions { Name = "by_approval_status" }),
        }, cancellationToken: ct);

        await ctx.Reports.Indexes.CreateOneAsync(
            new CreateIndexModel<Report>(
                Builders<Report>.IndexKeys.Ascending(r => r.Name),
                new CreateIndexOptions { Unique = true, Name = "uniq_name" }),
            cancellationToken: ct);

        await ctx.AuditLogs.Indexes.CreateManyAsync(new[]
        {
            // Primary browse order for the audit page: newest change first.
            new CreateIndexModel<AuditLog>(
                Builders<AuditLog>.IndexKeys.Descending(a => a.Timestamp),
                new CreateIndexOptions { Name = "by_timestamp" }),
            // Backs the per-object history tab (and any "what happened to X" lookup).
            new CreateIndexModel<AuditLog>(
                Builders<AuditLog>.IndexKeys.Ascending(a => a.TargetId).Descending(a => a.Timestamp),
                new CreateIndexOptions { Name = "by_target_time" }),
            // Backs the change-type / target-type filters on the audit page.
            new CreateIndexModel<AuditLog>(
                Builders<AuditLog>.IndexKeys
                    .Ascending(a => a.TargetType)
                    .Ascending(a => a.Change)
                    .Descending(a => a.Timestamp),
                new CreateIndexOptions { Name = "by_type_change_time" }),
        }, cancellationToken: ct);

        await ctx.EmailOutbox.Indexes.CreateManyAsync(new[]
        {
            // The sender drains pending messages oldest-first.
            new CreateIndexModel<EmailMessage>(
                Builders<EmailMessage>.IndexKeys.Ascending(m => m.Status).Ascending(m => m.CreatedAt),
                new CreateIndexOptions { Name = "by_status_created" }),
            // The audit "Sent emails" tab browses newest-first.
            new CreateIndexModel<EmailMessage>(
                Builders<EmailMessage>.IndexKeys.Descending(m => m.CreatedAt),
                new CreateIndexOptions { Name = "by_created" }),
        }, cancellationToken: ct);

        await ctx.EmailTemplates.Indexes.CreateOneAsync(
            new CreateIndexModel<EmailTemplate>(
                Builders<EmailTemplate>.IndexKeys.Ascending(t => t.Key),
                new CreateIndexOptions { Unique = true, Name = "uniq_key" }),
            cancellationToken: ct);

        await ctx.NotificationLogs.Indexes.CreateOneAsync(
            new CreateIndexModel<NotificationLog>(
                Builders<NotificationLog>.IndexKeys.Ascending(n => n.Key),
                new CreateIndexOptions { Unique = true, Name = "uniq_key" }),
            cancellationToken: ct);

        await ctx.WebhookDeliveries.Indexes.CreateManyAsync(new[]
        {
            // At-most-once enqueue: one delivery per (event, endpoint). The publisher relies on the
            // duplicate-key error to dedupe, exactly like the notification log.
            new CreateIndexModel<WebhookDelivery>(
                Builders<WebhookDelivery>.IndexKeys.Ascending(d => d.EventId).Ascending(d => d.EndpointId),
                new CreateIndexOptions { Unique = true, Name = "uniq_event_endpoint" }),
            // The dispatcher drains pending deliveries oldest-first.
            new CreateIndexModel<WebhookDelivery>(
                Builders<WebhookDelivery>.IndexKeys.Ascending(d => d.Status).Ascending(d => d.CreatedAt),
                new CreateIndexOptions { Name = "by_status_created" }),
            // The admin "Deliveries" panel browses newest-first.
            new CreateIndexModel<WebhookDelivery>(
                Builders<WebhookDelivery>.IndexKeys.Descending(d => d.CreatedAt),
                new CreateIndexOptions { Name = "by_created" }),
        }, cancellationToken: ct);

        await ctx.IntegrationDeliveries.Indexes.CreateManyAsync(new[]
        {
            // At-most-once enqueue: one delivery per (event, integration). The run service relies on
            // the duplicate-key error to dedupe, exactly like the notification log and webhook outbox.
            new CreateIndexModel<IntegrationDelivery>(
                Builders<IntegrationDelivery>.IndexKeys.Ascending(d => d.EventId).Ascending(d => d.IntegrationId),
                new CreateIndexOptions { Unique = true, Name = "uniq_event_integration" }),
            // The dispatcher drains pending deliveries oldest-first.
            new CreateIndexModel<IntegrationDelivery>(
                Builders<IntegrationDelivery>.IndexKeys.Ascending(d => d.Status).Ascending(d => d.CreatedAt),
                new CreateIndexOptions { Name = "by_status_created" }),
        }, cancellationToken: ct);

        await ctx.Events.Indexes.CreateOneAsync(
            new CreateIndexModel<Event>(
                // The events page browses newest-first.
                Builders<Event>.IndexKeys.Descending(e => e.Timestamp),
                new CreateIndexOptions { Name = "by_timestamp" }),
            cancellationToken: ct);

        await ctx.Samples.Indexes.CreateManyAsync(new[]
        {
            new CreateIndexModel<SampleProjection>(
                Builders<SampleProjection>.IndexKeys
                    .Ascending(s => s.ServiceAccountId)
                    .Ascending(s => s.SchemaName)
                    .Ascending(s => s.ValueName)
                    .Descending(s => s.Timestamp),
                new CreateIndexOptions { Name = "by_service_schema_value_time" }),
            new CreateIndexModel<SampleProjection>(
                Builders<SampleProjection>.IndexKeys
                    .Ascending(s => s.ServiceAccountId)
                    .Ascending(s => s.SchemaName)
                    .Ascending(s => s.ValueName)
                    .Ascending(s => s.PeriodStart),
                new CreateIndexOptions { Name = "by_service_schema_value_period" }),
            new CreateIndexModel<SampleProjection>(
                Builders<SampleProjection>.IndexKeys.Ascending(s => s.SubmissionId),
                new CreateIndexOptions { Name = "by_submission" }),
        }, cancellationToken: ct);
    }
}
