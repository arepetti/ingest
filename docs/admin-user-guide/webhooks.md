# Webhooks

**Webhooks** push events *out* of Ingest the moment they happen, so you can wire submissions and missed-window alerts into Teams, Power Automate, Slack, or your own service **without polling** the OData feed. Each registered endpoint receives a signed HTTP `POST` for the events it subscribes to.

It's a **Settings → Webhooks** section gated by the `webhooks:read` capability (managing endpoints needs `webhooks:manage`) — in the Admin default bundle, but grantable to any non-admin — and it only appears when the feature is switched on server-side (`Webhooks:Enabled`, **off by default** — see [setup/configuration.md → Webhooks](../setup/configuration.md#webhooks)). When the switch is off the section is hidden and every `/api/admin/webhooks/*` endpoint returns 404, mirroring the email master-switch pattern.

## Events you can subscribe to

| Event (wire name)        | Fires when… | Source |
|--------------------------|-------------|--------|
| `submission.accepted`    | A service submits data and it's accepted. | Immediate, on write. |
| `submission.warnings`    | An accepted submission carried non-blocking validation warnings. (`submission.accepted` also fires for the same write.) | Immediate, on write. |
| `window.upcoming`        | A required value's cadence window is about to close and nothing has been submitted yet. | The notification scheduler (timer). |
| `window.missed`          | A required value's *previous* window closed without a submission. | The notification scheduler (timer). |
| `submission.pending_approval` | A submission was accepted but is held awaiting approval before it goes live. | Immediate, on write. |
| `submission.approved`    | A pending submission was approved and is now live. (`submission.accepted` also fires for the same transition.) | Immediate, on the approve decision. |
| `submission.rejected`    | A pending submission was rejected and will not go live. The payload carries the reviewer's `note`. | Immediate, on the reject decision. |

> The two `window.*` events are discovered by the same background job that drives the **upcoming / missed email reminders** ([settings.md → Notifications](settings.md#notifications)). They fire whether or not the matching *email* trigger is enabled — subscribing a webhook is enough to make the job look for them. The cadence of the timer is `Notifications:Scheduler:PollMinutes`.
>
> The three `submission.*_approval` / `submission.approved` / `submission.rejected` events only ever fire while the [approval workflow](approval-process.md) is enabled (`Approval:Enabled`) — a submission has to be held pending for them to make sense. Like the accepted/warnings events they are emitted synchronously by the write/decision and are independent of the email triggers.
>
> A submission [saved as a draft](submissions.md#saving-a-draft) emits **no** webhook — it isn't a live business event yet, so neither `submission.accepted` nor `submission.pending_approval` fires. Those events fire only when the draft is **published** (and then exactly as a first-time submission would). The draft save instead sends the separate [draft-saved email nudge](settings.md#notifications), which is not a webhook.

## Registering an endpoint

On **Settings → Webhooks**, click **Add endpoint** and fill in:

- **Name** — a friendly label shown only in this list.
- **Destination URL** — where the signed `POST` is sent. Must be an absolute `http(s)` URL. (If the server is configured with a host allow-list, the URL's host must match it — otherwise delivery fails with a clear reason.)
- **Enabled** — uncheck to keep the registration but stop sending.
- **Events** — tick one or more of the events above.
- **Only for service** — optional. Limit deliveries to a single service account; leave blank to fire for *every* service. (`window.*` and `submission.*` events all carry a service, so this filters all event kinds.)
- **Description** — optional note.
- **Generate a signing secret** — see below. Ticked by default for new endpoints.

Click **Create endpoint**. If you asked for a secret, it's shown **once** in a dialog — copy it now (see [Verifying signatures](#verifying-signatures)).

Selecting an existing row opens the same editor to change its details. The signing secret is **not** editable here — use **Rotate secret** instead.

## The signing secret

Each endpoint can have an **HMAC-SHA256 signing secret** so the receiver can prove a request really came from Ingest (and wasn't tampered with or replayed).

- The plaintext is shown **exactly once**, at creation or rotation — it's stored encrypted at rest (keyed off `ApiKey:Pepper`) and can never be retrieved again.
- **Rotate secret** (row **⋮** menu) mints a new one and invalidates the old one immediately.
- An endpoint with no secret is delivered **unsigned** and flagged `unsigned` in the list. That's fine for an internal URL you trust, but set a secret for anything reachable off-box.

## What a delivery looks like

The body is JSON with a small envelope wrapping the event payload:

```json
{
  "event": "submission.accepted",
  "eventId": "accepted:7b1f…:2026-06-12T09:15:04.1234567Z",
  "occurredAt": "2026-06-12T09:15:04.1234567Z",
  "data": {
    "submissionId": "7b1f…",
    "serviceAccountId": "0c4a…",
    "serviceName": "acme-meters",
    "isReplacement": false,
    "submittedAt": "2026-06-12T09:15:04.0000000Z",
    "sampleCount": 12,
    "schemas": ["daily-readings"],
    "warnings": []
  }
}
```

Every request carries these headers:

| Header | Value |
|--------|-------|
| `Content-Type`        | `application/json` |
| `X-Ingest-Event`      | The dotted event name (`submission.accepted`, `window.missed`, …). |
| `X-Ingest-Event-Id`   | The deterministic event id — the idempotency key (see below). |
| `X-Ingest-Delivery`   | A unique id for *this delivery attempt's* row. |
| `X-Ingest-Timestamp`  | Unix-seconds time the request was signed. |
| `X-Ingest-Signature`  | `sha256=<hex>` — present only when the endpoint has a secret. |

### Verifying signatures

The signature is `HMAC-SHA256(secret, "{X-Ingest-Timestamp}.{rawBody}")`, lower-case hex, prefixed with `sha256=`. To verify, recompute it over the **exact raw body bytes** and the timestamp header, and compare. Reject requests whose `X-Ingest-Timestamp` is not recent (e.g. older than five minutes) to defend against replays.

```python
import hashlib, hmac, time

def verify(secret: str, headers, raw_body: bytes) -> bool:
    ts = headers["X-Ingest-Timestamp"]
    if abs(time.time() - int(ts)) > 300:          # reject stale timestamps
        return False
    expected = "sha256=" + hmac.new(
        secret.encode(), f"{ts}.".encode() + raw_body, hashlib.sha256
    ).hexdigest()
    return hmac.compare_digest(expected, headers.get("X-Ingest-Signature", ""))
```

### Idempotency

Retries (and the occasional duplicate the at-least-once design allows) reuse the same `X-Ingest-Event-Id`. Treat it as a **dedupe key**: process each id once and acknowledge repeats with a `2xx`.

## Delivery, retries and the log

Deliveries go through a durable **outbox** (the same pattern as outgoing email), so a webhook is never lost if the receiver is briefly down:

- Return any **2xx** to acknowledge. Anything else (or a timeout / connection error) is a failure.
- Failures **retry automatically with exponential backoff** up to `Webhooks:Worker:MaxAttempts`, after which the delivery is marked `Failed`.
- The delivery history lives on the **Audit** page under the **Webhook deliveries** tab (next to **Sent emails**) — not on the Webhooks settings section. It lists every attempt newest-first, filterable by **status** (`Pending` / `Sending` / `Sent` / `Failed`) and a **date range**, with the same **Export CSV** action as the other audit tabs. Hovering a failed row reveals the HTTP status and error. (The tab only appears when webhooks are enabled.)
- **Redeliver** (row **⋮** menu in the deliveries tab) requeues a delivery for another attempt — handy after you've fixed the receiver.
- **Send pending now** (the **⋯** actions menu on the Audit page) sends everything pending immediately rather than waiting for the next worker tick (useful when the in-process worker is disabled and an external scheduler calls `POST /api/admin/webhooks/drain`).

## Testing an endpoint

Use **Send test** (row **⋮** menu) to enqueue a `webhook.test` delivery to that endpoint. It's signed and shaped exactly like a real event, so you can confirm the URL, signature verification, and your receiver's parsing end-to-end. Watch it land in **Audit → Webhook deliveries**.

## Operational notes

- **Best-effort, never blocking.** Publishing a webhook can't fail a submission: the submission is persisted first, then events are enqueued. A webhook problem is logged, not surfaced to the submitting service.
- **At-least-once.** Combined with retries, a receiver may occasionally see the same event twice — rely on `X-Ingest-Event-Id` for idempotency.
- **Deleting an endpoint** stops future deliveries but keeps its past delivery rows for audit.

## Where to go next

- [setup/configuration.md → Webhooks](../setup/configuration.md#webhooks) — the server-side switches (master flag, worker cadence, retry limits, host allow-list).
- [settings.md → Notifications](settings.md#notifications) — the email side of the same events, and the scheduler that drives `window.*`.
