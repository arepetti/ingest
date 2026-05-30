using System.Security.Claims;
using System.Text.Encodings.Web;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Auth;

/// <summary>
/// Options for the API-key scheme. Empty today; defined so the framework can register the
/// scheme generically (an <see cref="AuthenticationSchemeOptions"/> derivative is required even
/// when no options are exposed).
/// </summary>
public sealed class ApiKeyAuthSchemeOptions : AuthenticationSchemeOptions { }

/// <summary>
/// ASP.NET Core authentication handler that validates an API key carried in a configurable
/// header (default <c>X-Api-Key</c>). Successful authentication produces a
/// <see cref="ClaimsPrincipal"/> with the account id, name, optional label, kind, and role
/// claims used by the rest of the pipeline.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthSchemeOptions>
{
    private readonly IApiKeyHasher _hasher;
    private readonly IApiKeyRepository _keys;
    private readonly IAccountRepository _accounts;
    private readonly ApiKeyOptions _apiKeyOptions;
    private readonly TimeProvider _time;

    /// <summary>Create a new <see cref="ApiKeyAuthenticationHandler"/>.</summary>
    /// <param name="options">Scheme options monitor (boilerplate).</param>
    /// <param name="logger">Logger factory.</param>
    /// <param name="encoder">URL encoder (boilerplate).</param>
    /// <param name="hasher">API-key hasher used to verify the secret.</param>
    /// <param name="keys">Repository to look up the key by its public id.</param>
    /// <param name="accounts">Repository to load the owning account.</param>
    /// <param name="apiKeyOptions">Bound API-key options (header name, pepper).</param>
    /// <param name="time">Clock used to check key expiry.</param>
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyHasher hasher,
        IApiKeyRepository keys,
        IAccountRepository accounts,
        IOptions<ApiKeyOptions> apiKeyOptions,
        TimeProvider time)
        : base(options, logger, encoder)
    {
        _hasher = hasher;
        _keys = keys;
        _accounts = accounts;
        _apiKeyOptions = apiKeyOptions.Value;
        _time = time;
    }

    /// <summary>
    /// Resolve the principal from the request's API-key header. Returns
    /// <see cref="AuthenticateResult.NoResult"/> when the header is absent (so the rest of the
    /// pipeline can decide whether to challenge), <see cref="AuthenticateResult.Fail(string)"/>
    /// when the key is malformed/invalid, and a populated principal otherwise.
    /// </summary>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(_apiKeyOptions.HeaderName, out var values))
            return AuthenticateResult.NoResult();

        var presented = values.ToString();
        if (string.IsNullOrWhiteSpace(presented))
            return AuthenticateResult.Fail("Missing API key.");

        if (!_hasher.TrySplit(presented, out var keyId, out var secret))
            return AuthenticateResult.Fail("Malformed API key.");

        var stored = await _keys.GetByKeyIdAsync(keyId, Context.RequestAborted);
        if (stored is null || !stored.IsActive(_time.GetUtcNow().UtcDateTime))
            return AuthenticateResult.Fail("Invalid API key.");

        if (!_hasher.Verify(secret, stored.Salt, stored.Hash))
            return AuthenticateResult.Fail("Invalid API key.");

        var account = await _accounts.GetByIdAsync(stored.AccountId, ct: Context.RequestAborted);
        if (account is null || !account.Enabled || account.IsDeleted)
            return AuthenticateResult.Fail("Account is not active.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.Name),
            new(AuthConstants.AccountIdClaim, account.Id.ToString()),
            new(AuthConstants.AccountNameClaim, account.Name),
            new(AuthConstants.KindClaim, account.Kind.ToString()),
            new(ClaimTypes.Role, account.Role.ToString()),
        };
        if (!string.IsNullOrEmpty(account.Label))
            claims.Add(new Claim(AuthConstants.AccountLabelClaim, account.Label));

        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    /// <summary>
    /// Emit a 401 with a <c>WWW-Authenticate</c> header advertising the scheme and the expected
    /// header name. Called when an authenticated endpoint is hit without (or with an invalid)
    /// API key.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers["WWW-Authenticate"] = $"ApiKey realm=\"ingest\", header=\"{_apiKeyOptions.HeaderName}\"";
        return Task.CompletedTask;
    }
}
