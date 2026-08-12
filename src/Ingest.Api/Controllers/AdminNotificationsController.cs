using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Ingest.Infrastructure.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Controllers;

/// <summary>
/// Notification configuration and the manual run trigger. Built on top of the email
/// infrastructure, so it shares the <c>Email:Enabled</c> master switch — every endpoint returns
/// 404 when email is disabled.
/// </summary>
[ApiController]
[Route("api/admin/notifications")]
[Authorize(Policy = Capabilities.NotificationsRead)]
public sealed class AdminNotificationsController : ControllerBase
{
    private readonly INotificationSettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly IAuditLogService _audit;
    private readonly bool _enabled;

    /// <summary>Create a new <see cref="AdminNotificationsController"/>.</summary>
    public AdminNotificationsController(
        INotificationSettingsService settings,
        INotificationService notifications,
        IAuditLogService audit,
        IOptions<EmailOptions> options)
    {
        _settings = settings;
        _notifications = notifications;
        _audit = audit;
        _enabled = options.Value.Enabled;
    }

    /// <summary>Get the current notification configuration.</summary>
    /// <response code="200">The settings.</response>
    /// <response code="404">Email is disabled.</response>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(NotificationSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        if (!_enabled) return NotFound(DiagnosticProblem.FeatureDisabled("notifications"));
        return Ok(NotificationSettingsDto.From(await _settings.GetAsync(ct)));
    }

    /// <summary>Update the notification configuration.</summary>
    /// <response code="200">The updated settings.</response>
    /// <response code="404">Email is disabled.</response>
    [HttpPut("settings")]
    [Authorize(Policy = Capabilities.NotificationsManage)]
    [ProducesResponseType(typeof(NotificationSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateNotificationSettingsRequest req, CancellationToken ct)
    {
        if (!_enabled) return NotFound(DiagnosticProblem.FeatureDisabled("notifications"));
        var updated = await _settings.UpdateAsync(new NotificationSettingsUpdate(
            req.Upcoming.ToUpdate(), req.Missed.ToUpdate(), req.Warnings.ToUpdate(),
            req.PendingApproval.ToUpdate(), req.Approved.ToUpdate(), req.Rejected.ToUpdate(),
            req.DraftSaved.ToUpdate(),
            req.UpcomingLeadHours, req.AdminRecipientAccountIds ?? new()), ct);
        await _audit.RecordAsync(AuditTargetType.Settings, AuditChangeType.Edit, AuditTargets.NotificationSettings, "Notification settings", ct);
        return Ok(NotificationSettingsDto.From(updated));
    }

    /// <summary>Run the notification job now. Internal trigger for an external scheduler.</summary>
    /// <response code="200">The run result (per-trigger email counts).</response>
    /// <response code="404">Email is disabled.</response>
    [HttpPost("run")]
    [Authorize(Policy = Capabilities.NotificationsManage)]
    [ProducesResponseType(typeof(NotificationRunResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        if (!_enabled) return NotFound(DiagnosticProblem.FeatureDisabled("notifications"));
        return Ok(await _notifications.RunAsync(ct));
    }
}
