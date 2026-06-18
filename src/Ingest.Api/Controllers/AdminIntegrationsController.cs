using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Ingest.Infrastructure.Integrations;
using Ingest.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Controllers;

/// <summary>
/// Integration administration: register integrations (Microsoft Teams today), edit the bot
/// connection, verify it, run a pass on demand, send a test prompt, and drain the delivery outbox.
/// Every endpoint short-circuits with 404 when <c>Integrations:Enabled</c> is off, mirroring the
/// webhook + email master switches so the feature is completely inert when disabled.
/// </summary>
[ApiController]
[Route("api/admin/integrations")]
[Authorize(Policy = Capabilities.IntegrationsRead)]
public sealed class AdminIntegrationsController : ControllerBase
{
    private readonly IIntegrationsService _integrations;
    private readonly IIntegrationRunService _run;
    private readonly IIntegrationDispatchService _dispatch;
    private readonly ITeamsClient _teams;
    private readonly ISecretProtector _protector;
    private readonly IAuditLogService _audit;
    private readonly bool _enabled;

    /// <summary>Create a new <see cref="AdminIntegrationsController"/>.</summary>
    public AdminIntegrationsController(
        IIntegrationsService integrations,
        IIntegrationRunService run,
        IIntegrationDispatchService dispatch,
        ITeamsClient teams,
        ISecretProtector protector,
        IAuditLogService audit,
        IOptions<IntegrationOptions> options)
    {
        _integrations = integrations;
        _run = run;
        _dispatch = dispatch;
        _teams = teams;
        _protector = protector;
        _audit = audit;
        _enabled = options.Value.Enabled;
    }

    /// <summary>List the configured integrations.</summary>
    /// <response code="200">All integrations, newest first.</response>
    /// <response code="404">Integrations are disabled.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<IntegrationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var list = await _integrations.ListAsync(ct);
        return Ok(list.Select(IntegrationDto.From).ToList());
    }

    /// <summary>Get one integration by id.</summary>
    /// <response code="200">The integration.</response>
    /// <response code="404">Integrations are disabled, or no integration with that id.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(IntegrationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        return Ok(IntegrationDto.From(await _integrations.GetAsync(id, ct)));
    }

    /// <summary>Create an integration.</summary>
    /// <response code="201">The created integration.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="404">Integrations are disabled.</response>
    [HttpPost]
    [Authorize(Policy = Capabilities.IntegrationsManage)]
    [ProducesResponseType(typeof(IntegrationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] IntegrationRequest req, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var created = await _integrations.CreateAsync(req.ToEntity(), ct);
        return Created($"/api/admin/integrations/{created.Id}", IntegrationDto.From(created));
    }

    /// <summary>Update an integration.</summary>
    /// <response code="200">The updated integration.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="404">Integrations are disabled, or no integration with that id.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Capabilities.IntegrationsManage)]
    [ProducesResponseType(typeof(IntegrationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] IntegrationRequest req, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var updated = await _integrations.UpdateAsync(id, req.ToEntity(), ct);
        return Ok(IntegrationDto.From(updated));
    }

    /// <summary>Delete an integration (soft-delete; its past deliveries are retained for audit).</summary>
    /// <response code="204">Deleted (idempotent).</response>
    /// <response code="404">Integrations are disabled.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Capabilities.IntegrationsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        await _integrations.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Get the Microsoft Teams bot connection settings (without the secret).</summary>
    /// <response code="200">The connection settings.</response>
    /// <response code="404">Integrations are disabled.</response>
    [HttpGet("connection")]
    [ProducesResponseType(typeof(TeamsConnectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConnection(CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        return Ok(TeamsConnectionDto.From(await _integrations.GetConnectionAsync(ct)));
    }

    /// <summary>Update the Microsoft Teams bot connection settings. The bot secret is write-once.</summary>
    /// <response code="200">The updated connection settings.</response>
    /// <response code="404">Integrations are disabled.</response>
    [HttpPut("connection")]
    [Authorize(Policy = Capabilities.IntegrationsManage)]
    [ProducesResponseType(typeof(TeamsConnectionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateConnection([FromBody] UpdateTeamsConnectionRequest req, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var updated = await _integrations.UpdateConnectionAsync(
            new TeamsConnectionUpdate(req.AppId, req.TenantId, req.SingleTenant, req.UpdatePassword, req.Password), ct);
        await _audit.RecordAsync(AuditTargetType.Settings, AuditChangeType.Edit, AuditTargets.TeamsConnection, "Microsoft Teams connection", ct);
        return Ok(TeamsConnectionDto.From(updated));
    }

    /// <summary>Verify the saved bot credentials by acquiring a token from Microsoft Entra.</summary>
    /// <response code="200">The test outcome (ok + optional error).</response>
    /// <response code="404">Integrations are disabled.</response>
    [HttpPost("connection/test")]
    [Authorize(Policy = Capabilities.IntegrationsManage)]
    [ProducesResponseType(typeof(TeamsConnectionTestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestConnection(CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var connection = await _integrations.GetConnectionAsync(ct);
        var password = _protector.Unprotect(connection.AppPasswordCipher);
        if (!connection.IsConfigured || string.IsNullOrEmpty(password))
            return Ok(new TeamsConnectionTestResult(false, "Teams connection is not configured."));

        var result = await _teams.TestConnectionAsync(
            new TeamsCredentials(connection.AppId!, password!, connection.TenantId, connection.SingleTenant), ct);
        return Ok(result);
    }

    /// <summary>Run every due integration now (on-demand), enqueuing prompts.</summary>
    /// <response code="200">The run result.</response>
    /// <response code="404">Integrations are disabled.</response>
    [HttpPost("run")]
    [Authorize(Policy = Capabilities.IntegrationsManage)]
    [ProducesResponseType(typeof(IntegrationRunResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RunAll(CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        return Ok(await _run.RunAllAsync(ct));
    }

    /// <summary>Run a single integration now, regardless of its schedule.</summary>
    /// <response code="200">The run result.</response>
    /// <response code="404">Integrations are disabled, or no integration with that id.</response>
    [HttpPost("{id:guid}/run")]
    [Authorize(Policy = Capabilities.IntegrationsManage)]
    [ProducesResponseType(typeof(IntegrationRunResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RunOne(Guid id, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        return Ok(await _run.RunOneAsync(id, ct));
    }

    /// <summary>Enqueue a diagnostic test prompt to the integration's target.</summary>
    /// <response code="202">Enqueued.</response>
    /// <response code="404">Integrations are disabled, or no integration with that id.</response>
    [HttpPost("{id:guid}/test")]
    [Authorize(Policy = Capabilities.IntegrationsManage)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendTest(Guid id, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        await _run.SendTestAsync(id, ct);
        return Accepted();
    }

    /// <summary>Manually drain the delivery outbox now (internal trigger for an external scheduler).</summary>
    /// <response code="200">The drain result (sent / failed counts).</response>
    /// <response code="404">Integrations are disabled.</response>
    [HttpPost("drain")]
    [Authorize(Policy = Capabilities.IntegrationsManage)]
    [ProducesResponseType(typeof(IntegrationDrainResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Drain([FromQuery] int? max, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        return Ok(await _dispatch.DrainAsync(max ?? 50, ct));
    }
}
