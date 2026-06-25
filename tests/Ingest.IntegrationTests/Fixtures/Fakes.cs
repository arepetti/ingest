using System.Collections.Concurrent;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;

namespace Ingest.IntegrationTests.Fixtures;

/// <summary>
/// Stand-in for the real SMTP sender. Records every message it is asked to deliver and never
/// touches the network, so the email pipeline (enqueue -> drain -> send) can be exercised
/// end-to-end without a mail server.
/// </summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    /// <summary>Every message handed to the sender, in delivery order.</summary>
    public IReadOnlyCollection<EmailMessage> Sent => _sent.ToArray();

    /// <summary>Forget everything captured so far (call between tests that share the app).</summary>
    public void Clear() => _sent.Clear();

    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, EmailSettings settings, CancellationToken ct = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Primary <see cref="HttpMessageHandler"/> swapped in for the webhook dispatcher's typed
/// <see cref="HttpClient"/>. Captures each outbound request body and always answers 200 OK, so the
/// real dispatcher (signing, outbox bookkeeping, retries) runs without any external endpoint.
/// </summary>
public sealed class RecordingHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<CapturedRequest> _requests = new();

    /// <summary>Every request the dispatcher attempted, in send order.</summary>
    public IReadOnlyCollection<CapturedRequest> Requests => _requests.ToArray();

    /// <summary>Forget everything captured so far.</summary>
    public void Clear() => _requests.Clear();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        _requests.Enqueue(new CapturedRequest(request.RequestUri?.ToString() ?? "", body, headers));
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
    }

    /// <summary>One captured outbound webhook POST.</summary>
    /// <param name="Url">Destination URL.</param>
    /// <param name="Body">Raw JSON body.</param>
    /// <param name="Headers">Request headers (joined values).</param>
    public sealed record CapturedRequest(string Url, string Body, IReadOnlyDictionary<string, string> Headers);
}
