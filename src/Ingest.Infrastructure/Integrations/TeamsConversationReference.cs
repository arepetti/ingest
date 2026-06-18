using System.Text.Json.Serialization;

namespace Ingest.Infrastructure.Integrations;

/// <summary>
/// Minimal conversation reference captured from an inbound Bot Framework activity and replayed to
/// send a proactive message. Only the fields needed to address a reply are kept; serialised as JSON
/// and stored on <c>TeamsTarget.ConversationReferenceJson</c>.
/// </summary>
public sealed class TeamsConversationReference
{
    /// <summary>Connector base URL the reply is POSTed to (e.g. <c>https://smba.trafficmanager.net/emea/</c>).</summary>
    [JsonPropertyName("serviceUrl")]
    public string ServiceUrl { get; set; } = "";

    /// <summary>Conversation id the reply targets.</summary>
    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = "";

    /// <summary>Channel id (always <c>msteams</c> here); informational.</summary>
    [JsonPropertyName("channelId")]
    public string? ChannelId { get; set; }

    /// <summary>Bot's own account id within the conversation; informational.</summary>
    [JsonPropertyName("botId")]
    public string? BotId { get; set; }

    /// <summary>True when the addressed conversation has the minimum it needs to receive a proactive message.</summary>
    [JsonIgnore]
    public bool IsUsable => !string.IsNullOrWhiteSpace(ServiceUrl) && !string.IsNullOrWhiteSpace(ConversationId);
}
