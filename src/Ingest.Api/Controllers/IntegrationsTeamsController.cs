using System.Text.Json;
using Ingest.Api.Auth;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Integrations;
using Ingest.Infrastructure.Mongo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Ingest.Api.Controllers;

/// <summary>
/// Inbound Bot Framework messaging endpoint for the Microsoft Teams integration. Teams POSTs every
/// activity (the bot being added, a user opening the card, an <c>Action.Execute</c> submit) here.
/// The endpoint authenticates the connector's bearer token against the bot App ID, captures the
/// conversation reference needed for proactive sends, and drives the multi-step submission flow:
/// each submit re-evaluates the schema's <c>visibleIf</c>/<c>enabledIf</c> rules against the answers
/// gathered so far, asks for any newly-activated required values, and finally records the submission
/// (notes are never collected). Gated by <c>Integrations:Enabled</c>.
/// </summary>
[ApiController]
[Route("api/integrations/teams")]
[AllowAnonymous] // authenticated per-request via the Bot Framework connector token
public sealed class IntegrationsTeamsController : ControllerBase
{
    private const string SubmitVerb = TeamsCardBuilder.SubmitVerb;
    private const string AdaptiveCardContentType = "application/vnd.microsoft.card.adaptive";

    private readonly IIntegrationsService _integrations;
    private readonly ISchemaRepository _schemas;
    private readonly ISubmissionService _submissions;
    private readonly TeamsCardBuilder _cards;
    private readonly TeamsBotAuthenticator _auth;
    private readonly MongoContext _ctx;
    private readonly IAuditContext _audit;
    private readonly IAppConfigurationService _appConfig;
    private readonly bool _enabled;
    private readonly ILogger<IntegrationsTeamsController> _logger;

    /// <summary>Create a new <see cref="IntegrationsTeamsController"/>.</summary>
    public IntegrationsTeamsController(
        IIntegrationsService integrations,
        ISchemaRepository schemas,
        ISubmissionService submissions,
        TeamsCardBuilder cards,
        TeamsBotAuthenticator auth,
        MongoContext ctx,
        IAuditContext audit,
        IAppConfigurationService appConfig,
        IOptions<IntegrationOptions> options,
        ILogger<IntegrationsTeamsController> logger)
    {
        _integrations = integrations;
        _schemas = schemas;
        _submissions = submissions;
        _cards = cards;
        _auth = auth;
        _ctx = ctx;
        _audit = audit;
        _appConfig = appConfig;
        _enabled = options.Value.Enabled;
        _logger = logger;
    }

    /// <summary>Receive a Bot Framework activity from Microsoft Teams.</summary>
    /// <response code="200">Activity handled (with an inline card refresh for invoke activities).</response>
    /// <response code="401">The connector token was missing or invalid.</response>
    /// <response code="404">Integrations are disabled.</response>
    [HttpPost("messages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Messages([FromBody] JsonElement activity, CancellationToken ct)
    {
        if (!_enabled) return NotFound();

        var connection = await _integrations.GetConnectionAsync(ct);
        if (!connection.IsConfigured || string.IsNullOrWhiteSpace(connection.AppId))
            return Unauthorized();

        var authHeader = Request.Headers.Authorization.ToString();
        if (!await _auth.ValidateAsync(authHeader, connection.AppId!, ct))
            return Unauthorized();

        // Always capture the conversation reference so a later proactive prompt can reach this chat.
        await CaptureConversationAsync(activity, ct);

        var type = GetString(activity, "type");
        if (string.Equals(type, "invoke", StringComparison.OrdinalIgnoreCase))
            return await HandleInvokeAsync(activity, ct);

        return Ok();
    }

    private async Task<IActionResult> HandleInvokeAsync(JsonElement activity, CancellationToken ct)
    {
        // Action.Execute submits arrive as an invoke named "adaptiveCard/action"; the action's data
        // (control fields + prior answers) is merged with the card's input values by Teams.
        if (!activity.TryGetProperty("value", out var value)) return CardResponse(ErrorCard("No action payload."));

        var data = ExtractActionData(value);
        if (data is null || !data.Value.TryGetProperty("schema", out var schemaEl))
            return CardResponse(ErrorCard("This card is missing its context; please request a fresh prompt."));

        var schemaName = schemaEl.GetString() ?? "";
        var schema = await _schemas.GetByNameAsync(schemaName, false, ct);
        if (schema is null) return CardResponse(ErrorCard($"Schema '{schemaName}' is no longer available."));

        if (!TryGetGuid(data.Value, "integrationId", out var integrationId) ||
            !TryGetGuid(data.Value, "serviceId", out var serviceId))
            return CardResponse(ErrorCard("This card is missing its context; please request a fresh prompt."));

        Integration integration;
        try { integration = await _integrations.GetAsync(integrationId, ct); }
        catch (NotFoundException) { return CardResponse(ErrorCard("This integration no longer exists.")); }

        // Merge prior answers (data.answers) with this round's input fields (the remaining data keys).
        var raw = MergeAnswers(data.Value);
        var coerced = TeamsCardBuilder.CoerceAnswers(schema, raw);
        var answered = raw.Where(kv => kv.Value is string s && !string.IsNullOrWhiteSpace(s))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var outstanding = _cards.OutstandingRequired(schema, coerced, answered);
        if (outstanding.Count > 0)
        {
            var card = _cards.BuildPromptCard(integration, schema, serviceId,
                integration.Teams.DisplayName ?? "Service", "Current reporting period", coerced, outstanding);
            return CardResponse(card);
        }

        var samples = _cards.BuildSamples(schema, coerced, _audit.UtcNow);
        if (samples.Count == 0)
            return CardResponse(_cards.BuildResultCard("Nothing to submit", new[] { "No active values were provided." }, true));

        // Teams submissions go through AdminCreateAsync — the same path the admin UI uses for
        // remediation — so the kill switch can't be enforced there without also blocking admins.
        // Gate here instead, at this service-facing entry point.
        var ingestion = await _appConfig.GetIngestionStatusAsync(ct);
        if (ingestion.Closed)
        {
            var message = string.IsNullOrWhiteSpace(ingestion.Message) ? "Submissions are temporarily closed." : ingestion.Message!;
            return CardResponse(_cards.BuildResultCard("Submissions closed", new[] { message }, true));
        }

        try
        {
            var result = await _submissions.AdminCreateAsync(
                new AdminSubmissionInput(serviceId, samples), SubmissionSource.Manual, ct: ct);
            var messages = result.Warnings.Count > 0
                ? result.Warnings
                : new[] { "Your submission was recorded." };
            return CardResponse(_cards.BuildResultCard("Submitted", messages.ToList(), false));
        }
        catch (ValidationException ex)
        {
            return CardResponse(_cards.BuildResultCard("Submission rejected", ex.Errors.ToList(), true));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Teams submission for integration {Id} failed.", integrationId);
            return CardResponse(ErrorCard("Something went wrong recording your submission."));
        }
    }

    /// <summary>Build the invoke response Teams expects to refresh the card in place.</summary>
    private IActionResult CardResponse(object card) => Ok(new
    {
        statusCode = 200,
        type = AdaptiveCardContentType,
        value = card,
    });

    private object ErrorCard(string message) => _cards.BuildResultCard("Unavailable", new[] { message }, true);

    private async Task CaptureConversationAsync(JsonElement activity, CancellationToken ct)
    {
        var serviceUrl = GetString(activity, "serviceUrl");
        var conversationId = GetString(GetObject(activity, "conversation"), "id");
        if (string.IsNullOrWhiteSpace(serviceUrl) || string.IsNullOrWhiteSpace(conversationId)) return;

        var reference = new TeamsConversationReference
        {
            ServiceUrl = serviceUrl!,
            ConversationId = conversationId!,
            ChannelId = GetString(activity, "channelId"),
            BotId = GetString(GetObject(activity, "recipient"), "id"),
        };
        var json = JsonSerializer.Serialize(reference);

        // Identify candidate targets from the sender / conversation so we only attach the reference
        // to integrations that actually point at this user or channel.
        var from = GetObject(activity, "from");
        var userKeys = new[] { GetString(from, "aadObjectId"), GetString(from, "id"), GetString(from, "name") }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();

        var integrations = await _integrations.ListAsync(ct);
        foreach (var integration in integrations)
        {
            if (!integration.Enabled) continue;
            var t = integration.Teams;
            var matches = t.Kind switch
            {
                TeamsTargetKind.User => userKeys.Any(k => string.Equals(k, t.TargetId, StringComparison.OrdinalIgnoreCase)),
                TeamsTargetKind.Channel => string.Equals(conversationId, t.TargetId, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
            if (!matches) continue;
            if (string.Equals(t.ConversationReferenceJson, json, StringComparison.Ordinal)) continue;

            var update = Builders<Integration>.Update
                .Set(i => i.Teams.ConversationReferenceJson, json)
                .Set(i => i.ModifiedAt, _audit.UtcNow);
            await _ctx.Integrations.UpdateOneAsync(i => i.Id == integration.Id, update, cancellationToken: ct);
        }
    }

    /// <summary>Pull the action data object out of an invoke value (<c>value.action.data</c> or <c>value</c> for Action.Submit).</summary>
    private static JsonElement? ExtractActionData(JsonElement value)
    {
        if (value.TryGetProperty("action", out var action) &&
            action.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            return data;
        return value.ValueKind == JsonValueKind.Object ? value : null;
    }

    /// <summary>Combine the prior answers (nested <c>answers</c>) with this round's input fields.</summary>
    private static Dictionary<string, object?> MergeAnswers(JsonElement data)
    {
        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (data.TryGetProperty("answers", out var prior) && prior.ValueKind == JsonValueKind.Object)
            foreach (var p in prior.EnumerateObject())
                merged[p.Name] = ScalarToString(p.Value);

        foreach (var p in data.EnumerateObject())
        {
            if (p.Name is "integrationId" or "serviceId" or "schema" or "answers" or "verb") continue;
            merged[p.Name] = ScalarToString(p.Value);
        }
        return merged;
    }

    private static string? ScalarToString(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    private static string? GetString(JsonElement? obj, string name) =>
        obj is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static JsonElement? GetObject(JsonElement? obj, string name) =>
        obj is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? v : null;

    private static bool TryGetGuid(JsonElement obj, string name, out Guid id)
    {
        id = Guid.Empty;
        return obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out id);
    }
}
