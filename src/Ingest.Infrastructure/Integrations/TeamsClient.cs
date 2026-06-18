using System.Net.Http.Json;
using System.Text.Json;
using Ingest.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ingest.Infrastructure.Integrations;

/// <summary>
/// HTTP implementation of <see cref="ITeamsClient"/> over the Bot Framework connector REST API. It
/// acquires a bot token via the Microsoft Entra client-credentials flow and POSTs a proactive
/// message (carrying the Adaptive Card attachment) to a stored conversation. No Bot Builder SDK
/// dependency — credentials live in a DB singleton and are passed in explicitly.
/// </summary>
public sealed class TeamsClient : ITeamsClient
{
    /// <summary>Name of the typed <see cref="HttpClient"/> registered for Teams calls.</summary>
    public const string HttpClientName = "integrations-teams";

    private const string BotScope = "https://api.botframework.com/.default";
    private const string AdaptiveCardContentType = "application/vnd.microsoft.card.adaptive";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<TeamsClient> _logger;

    /// <summary>Create a new <see cref="TeamsClient"/>.</summary>
    public TeamsClient(IHttpClientFactory httpFactory, ILogger<TeamsClient> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TeamsConnectionTestResult> TestConnectionAsync(TeamsCredentials credentials, CancellationToken ct = default)
    {
        try
        {
            var token = await AcquireTokenAsync(credentials, ct);
            return string.IsNullOrEmpty(token)
                ? new TeamsConnectionTestResult(false, "No token returned by Microsoft Entra.")
                : new TeamsConnectionTestResult(true);
        }
        catch (Exception ex)
        {
            return new TeamsConnectionTestResult(false, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task SendAdaptiveCardAsync(
        TeamsCredentials credentials,
        string conversationReferenceJson,
        object adaptiveCard,
        CancellationToken ct = default)
    {
        var reference = JsonSerializer.Deserialize<TeamsConversationReference>(conversationReferenceJson)
            ?? throw new InvalidOperationException("The stored conversation reference could not be parsed.");
        if (!reference.IsUsable)
            throw new InvalidOperationException("The stored conversation reference is incomplete; the bot has not been added to the chat/channel yet.");

        var token = await AcquireTokenAsync(credentials, ct)
            ?? throw new InvalidOperationException("Could not acquire a bot token.");

        var activity = new Dictionary<string, object?>
        {
            ["type"] = "message",
            ["attachments"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["contentType"] = AdaptiveCardContentType,
                    ["content"] = adaptiveCard,
                },
            },
        };

        var baseUrl = reference.ServiceUrl.EndsWith('/') ? reference.ServiceUrl : reference.ServiceUrl + "/";
        var url = $"{baseUrl}v3/conversations/{Uri.EscapeDataString(reference.ConversationId)}/activities";

        var client = _httpFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(activity) };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Teams send failed ({(int)response.StatusCode}): {Trim(body)}");
        }
    }

    private async Task<string?> AcquireTokenAsync(TeamsCredentials credentials, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(credentials.AppId) || string.IsNullOrWhiteSpace(credentials.Password))
            throw new InvalidOperationException("Teams bot credentials are not configured.");

        // Multi-tenant bots authenticate against the shared botframework.com tenant; single-tenant
        // bots against their own tenant. The scope is always the Bot Connector resource.
        var tenant = credentials.SingleTenant && !string.IsNullOrWhiteSpace(credentials.TenantId)
            ? credentials.TenantId
            : "botframework.com";
        var tokenUrl = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token";

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = credentials.AppId,
            ["client_secret"] = credentials.Password,
            ["scope"] = BotScope,
        });

        var client = _httpFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsync(tokenUrl, form, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token request failed ({(int)response.StatusCode}): {Trim(json)}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
    }

    private static string Trim(string s) => s.Length > 400 ? s[..400] + "…" : s;
}
