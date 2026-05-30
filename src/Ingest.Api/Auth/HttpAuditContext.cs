using System.Security.Claims;
using Ingest.Core.Common;

namespace Ingest.Api.Auth;

/// <summary>
/// HTTP-aware <see cref="IAuditContext"/>. Pulls the calling account's name and id from the
/// current <see cref="HttpContext.User"/> via the custom <see cref="AuthConstants.AccountNameClaim"/>
/// and <see cref="AuthConstants.AccountIdClaim"/> claims and uses the registered
/// <see cref="TimeProvider"/> as the clock. Falls back to <see cref="ClaimTypes.Name"/> when the
/// custom claim is absent (e.g. tests that hand-roll a principal).
/// </summary>
public sealed class HttpAuditContext : IAuditContext
{
    private readonly IHttpContextAccessor _accessor;
    private readonly TimeProvider _time;

    /// <summary>Create a new <see cref="HttpAuditContext"/>.</summary>
    /// <param name="accessor">Ambient HTTP context accessor.</param>
    /// <param name="time">Clock used to compute <see cref="UtcNow"/>.</param>
    public HttpAuditContext(IHttpContextAccessor accessor, TimeProvider time)
    {
        _accessor = accessor;
        _time = time;
    }

    /// <inheritdoc />
    public string? UserName => _accessor.HttpContext?.User.FindFirst(AuthConstants.AccountNameClaim)?.Value
                               ?? _accessor.HttpContext?.User.FindFirst(ClaimTypes.Name)?.Value;

    /// <inheritdoc />
    public Guid? AccountId
    {
        get
        {
            var raw = _accessor.HttpContext?.User.FindFirst(AuthConstants.AccountIdClaim)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    /// <inheritdoc />
    public DateTime UtcNow => _time.GetUtcNow().UtcDateTime;
}
