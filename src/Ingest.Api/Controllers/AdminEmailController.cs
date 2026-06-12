using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Controllers;

/// <summary>
/// Email infrastructure administration: SMTP settings, editable templates, the outbox (for the
/// audit "Sent emails" tab), the manual drain trigger, and the ad-hoc "send an email to an
/// account" action. Every endpoint short-circuits with 404 when <c>Email:Enabled</c> is off, so
/// the feature is completely inert when disabled (mirroring the SSO master switch).
/// </summary>
[ApiController]
[Route("api/admin/email")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public sealed class AdminEmailController : ControllerBase
{
    private readonly IEmailSettingsService _settings;
    private readonly IEmailTemplateService _templates;
    private readonly IEmailQueue _queue;
    private readonly IEmailDispatchService _dispatch;
    private readonly IAccountService _accounts;
    private readonly bool _enabled;

    /// <summary>Create a new <see cref="AdminEmailController"/>.</summary>
    public AdminEmailController(
        IEmailSettingsService settings,
        IEmailTemplateService templates,
        IEmailQueue queue,
        IEmailDispatchService dispatch,
        IAccountService accounts,
        IOptions<EmailOptions> options)
    {
        _settings = settings;
        _templates = templates;
        _queue = queue;
        _dispatch = dispatch;
        _accounts = accounts;
        _enabled = options.Value.Enabled;
    }

    /// <summary>Get the current SMTP settings (the password is never returned).</summary>
    /// <response code="200">The settings.</response>
    /// <response code="404">Email is disabled.</response>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(EmailSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        return Ok(EmailSettingsDto.From(await _settings.GetAsync(ct)));
    }

    /// <summary>Update the SMTP settings. The password is write-only (see <see cref="UpdateEmailSettingsRequest"/>).</summary>
    /// <response code="200">The updated settings.</response>
    /// <response code="400">Validation failed (bad host/port/from-address).</response>
    /// <response code="404">Email is disabled.</response>
    [HttpPut("settings")]
    [ProducesResponseType(typeof(EmailSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateEmailSettingsRequest req, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var updated = await _settings.UpdateAsync(
            new EmailSettingsUpdate(req.Host, req.Port, req.UseStartTls, req.Username, req.FromAddress, req.FromName, req.UpdatePassword, req.Password), ct);
        return Ok(EmailSettingsDto.From(updated));
    }

    /// <summary>List the editable email templates.</summary>
    /// <response code="200">All templates, ordered by key.</response>
    /// <response code="404">Email is disabled.</response>
    [HttpGet("templates")]
    [ProducesResponseType(typeof(IReadOnlyList<EmailTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListTemplates(CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var list = await _templates.ListAsync(ct);
        return Ok(list.Select(EmailTemplateDto.From).ToList());
    }

    /// <summary>Get one template by key.</summary>
    /// <response code="200">The template.</response>
    /// <response code="404">Email is disabled, or no template with that key.</response>
    [HttpGet("templates/{key}")]
    [ProducesResponseType(typeof(EmailTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTemplate(string key, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        return Ok(EmailTemplateDto.From(await _templates.GetAsync(key, ct)));
    }

    /// <summary>Update a template's content (the key is immutable; the Liquid is validated).</summary>
    /// <response code="200">The updated template.</response>
    /// <response code="400">The Liquid failed to parse.</response>
    /// <response code="404">Email is disabled, or no template with that key.</response>
    [HttpPut("templates/{key}")]
    [ProducesResponseType(typeof(EmailTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTemplate(string key, [FromBody] UpdateEmailTemplateRequest req, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var updated = await _templates.UpdateAsync(key,
            new EmailTemplateUpdate(req.Name, req.Description, req.Subject, req.HtmlBody, req.TextBody), ct);
        return Ok(EmailTemplateDto.From(updated));
    }

    /// <summary>Page through the outbox newest-first (the audit "Sent emails" tab), optionally filtered by status.</summary>
    /// <response code="200">A page of messages.</response>
    /// <response code="404">Email is disabled.</response>
    [HttpGet("outbox")]
    [ProducesResponseType(typeof(PagedResponse<EmailMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListOutbox(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] EmailStatus? status,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        var result = await _queue.ListAsync(RequestHelpers.ToPageRequest(page, pageSize, null, false), status, from, to, ct);
        return Ok(result.Map(EmailMessageDto.From));
    }

    /// <summary>Manually drain the outbox now. Internal trigger for an external scheduler/sender.</summary>
    /// <response code="200">The drain result (sent / failed counts).</response>
    /// <response code="404">Email is disabled.</response>
    [HttpPost("drain")]
    [ProducesResponseType(typeof(EmailDrainResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Drain([FromQuery] int? max, CancellationToken ct)
    {
        if (!_enabled) return NotFound();
        return Ok(await _dispatch.DrainAsync(max ?? 50, ct));
    }

    /// <summary>
    /// Send an ad-hoc plain-text email to a single account. Available to operators and admins.
    /// The message is enqueued into the outbox and delivered by the sender like any other.
    /// </summary>
    /// <response code="202">Enqueued; returns the new message id.</response>
    /// <response code="400">Subject/body missing, or the account has no contact email.</response>
    /// <response code="404">Email is disabled, or no account with that id.</response>
    [HttpPost("send")]
    [Authorize(Policy = AuthConstants.OperatorPolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Send([FromBody] SendAdhocEmailRequest req, CancellationToken ct)
    {
        if (!_enabled) return NotFound();

        var account = await _accounts.GetAsync(req.AccountId, false, ct);
        if (account is null) return NotFound();
        if (string.IsNullOrWhiteSpace(account.Email))
            throw new ValidationException(new[] { $"Account '{account.Name}' has no contact email set." });
        if (string.IsNullOrWhiteSpace(req.Subject)) throw new ValidationException(new[] { "Subject is required." });

        var id = await _queue.EnqueueAsync(new EmailRequest(
            account.Email, account.Label ?? account.Name, req.Subject, req.Body ?? "",
            Category: "adhoc", RelatedAccountId: account.Id), ct);
        return Accepted(new { id });
    }
}
