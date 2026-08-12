using System.Globalization;
using Ingest.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingest.Api.Controllers;

/// <summary>Anonymous configuration needed by the SPA before authentication.</summary>
[ApiController]
[Route("api/bootstrap")]
[AllowAnonymous]
public sealed class BootstrapController(IOptions<IngestOptions> options) : ControllerBase
{
    private const string FallbackLocale = "en-US";

    /// <summary>Return the server's normalized default locale and no privileged configuration.</summary>
    /// <response code="200">The client bootstrap configuration.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new
    {
        defaultLocale = NormalizeLocale(options.Value.DefaultLocale),
    });

    internal static string NormalizeLocale(string? locale)
    {
        var candidate = locale?.Trim();
        if (string.IsNullOrEmpty(candidate)) return FallbackLocale;

        try
        {
            return CultureInfo.GetCultureInfo(candidate).Name;
        }
        catch (CultureNotFoundException)
        {
            return FallbackLocale;
        }
    }
}
