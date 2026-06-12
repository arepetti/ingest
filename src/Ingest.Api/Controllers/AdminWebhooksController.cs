using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Controllers;

/// <summary>
/// Outbound webhook administration: register endpoints, manage their signing secret, send a test
/// ping, browse the delivery log, redeliver a failed delivery, and manually drain the outbox.
/// Every endpoint short-circuits with 404 when <c>Webhooks:Enabled</c> is off, mirroring the email
/// master switch so the feature is completely inert when disabled.
/// </summary>
[ApiController]
[Route("api/admin/webhooks")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public sealed class AdminWebhooksController : ControllerBase
{
    private readonly IWebhookEndpointService _endpoints;
    private readonly IWebhookDeliveryRepository _deliveries;
    private readonly IWebhookDispatchService _dispatch;
    private readonly bool _enabled;

    /// <summary>Create a new <see cref="AdminWebhooksController"/>.</summary>
    public AdminWebhooksController(
        IWebhookEndpointService endpoints,
        IWebhookDeliveryRepository deliveries,
        IWebhookDispatchService dispatch,
        IOptions<WebhookOptions> options)
    {
        _endpoints = endpoints;
        _deliveries = deliveries;
        _dispatch = dispatch;
        _enabled = options.Value.Enabled;
    }

    /// <summary>List the registered webhook endpoints.</summary>
    /// <response code="200">All endpoints, newest first.</response>
    /// <response code="404">Webhooks are disabled.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookEndpointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var list = await _endpoints.ListAsync(ct);
        return Ok(list.Select(WebhookEndpointDto.From).ToList());
    }

    /// <summary>Get one endpoint by id.</summary>
    /// <response code="200">The endpoint.</response>
    /// <response code="404">Webhooks are disabled, or no endpoint with that id.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WebhookEndpointDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        return Ok(WebhookEndpointDto.From(await _endpoints.GetAsync(id, ct)));
    }

    /// <summary>Create a webhook endpoint. When <c>generateSecret</c> is true the secret is returned exactly once.</summary>
    /// <response code="201">The created endpoint (and the secret if one was generated).</response>
    /// <response code="400">Validation failed (bad name/URL).</response>
    /// <response code="404">Webhooks are disabled.</response>
    [HttpPost]
    [ProducesResponseType(typeof(WebhookEndpointCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateWebhookEndpointRequest req, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var (endpoint, secret) = await _endpoints.CreateAsync(
            new WebhookEndpointInput(req.Name, req.Url, req.Enabled, req.Events ?? new(), req.ServiceAccountId, req.Description),
            req.GenerateSecret, ct);
        return Created($"/api/admin/webhooks/{endpoint.Id}",
            new WebhookEndpointCreatedResponse(WebhookEndpointDto.From(endpoint), secret));
    }

    /// <summary>Update an endpoint (the signing secret is left untouched — use rotate-secret to change it).</summary>
    /// <response code="200">The updated endpoint.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="404">Webhooks are disabled, or no endpoint with that id.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(WebhookEndpointDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWebhookEndpointRequest req, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var updated = await _endpoints.UpdateAsync(id,
            new WebhookEndpointInput(req.Name, req.Url, req.Enabled, req.Events ?? new(), req.ServiceAccountId, req.Description), ct);
        return Ok(WebhookEndpointDto.From(updated));
    }

    /// <summary>Delete an endpoint. Its past deliveries are retained for audit.</summary>
    /// <response code="204">Deleted (idempotent).</response>
    /// <response code="404">Webhooks are disabled.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        await _endpoints.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Mint a fresh signing secret and return it once.</summary>
    /// <response code="200">The endpoint and the new plaintext secret.</response>
    /// <response code="404">Webhooks are disabled, or no endpoint with that id.</response>
    [HttpPost("{id:guid}/rotate-secret")]
    [ProducesResponseType(typeof(WebhookSecretResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RotateSecret(Guid id, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var (endpoint, secret) = await _endpoints.RotateSecretAsync(id, ct);
        return Ok(new WebhookSecretResponse(WebhookEndpointDto.From(endpoint), secret));
    }

    /// <summary>Enqueue a <c>webhook.test</c> delivery to the endpoint so you can verify the wiring.</summary>
    /// <response code="202">Enqueued; returns the new delivery id.</response>
    /// <response code="404">Webhooks are disabled, or no endpoint with that id.</response>
    [HttpPost("{id:guid}/test")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendTest(Guid id, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var deliveryId = await _endpoints.SendTestAsync(id, ct);
        return Accepted(new { id = deliveryId });
    }

    /// <summary>Page through the delivery log newest-first, optionally filtered by status and a created-at window.</summary>
    /// <response code="200">A page of deliveries.</response>
    /// <response code="404">Webhooks are disabled.</response>
    [HttpGet("deliveries")]
    [ProducesResponseType(typeof(PagedResponse<WebhookDeliveryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListDeliveries(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] WebhookDeliveryStatus? status,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var result = await _deliveries.ListAsync(RequestHelpers.ToPageRequest(page, pageSize, null, false), status, from, to, ct);
        return Ok(result.Map(WebhookDeliveryDto.From));
    }

    /// <summary>Requeue a delivery (typically a failed one) for another attempt.</summary>
    /// <response code="200">Requeued.</response>
    /// <response code="404">Webhooks are disabled, or no delivery with that id.</response>
    [HttpPost("deliveries/{deliveryId:guid}/redeliver")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Redeliver(Guid deliveryId, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        return await _deliveries.RequeueAsync(deliveryId, ct) ? Ok() : NotFound();
    }

    /// <summary>Manually drain the delivery outbox now. Internal trigger for an external scheduler.</summary>
    /// <response code="200">The drain result (sent / failed counts).</response>
    /// <response code="404">Webhooks are disabled.</response>
    [HttpPost("drain")]
    [ProducesResponseType(typeof(WebhookDrainResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Drain([FromQuery] int? max, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        return Ok(await _dispatch.DrainAsync(max ?? 50, ct));
    }
}
