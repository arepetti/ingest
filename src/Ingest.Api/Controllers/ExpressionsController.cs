using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Ingest.Api.Controllers;

/// <summary>
/// Public endpoints around the validation-rule language. Currently exposes one operation —
/// translating an expression to its JavaScript equivalent — used by the admin SPA to render
/// live "Enabled if" / "Visible if" / "Warning" feedback in the submission editor without
/// shipping a second parser to the browser.
/// </summary>
/// <remarks>
/// The endpoint is intentionally <see cref="AllowAnonymousAttribute">anonymous</see>: it is a
/// pure function of the request body (no data is read or written) and the SPA needs to call
/// it as part of rendering the schema editor, before the signed-in user is fully bootstrapped.
/// A hard cap on the input length keeps the surface area cheap to serve.
/// </remarks>
[ApiController]
[Route("api/expressions")]
[AllowAnonymous]
public sealed class ExpressionsController : ControllerBase
{
    /// <summary>Maximum number of characters accepted in a single translation request.</summary>
    /// <remarks>
    /// Schema rules are authored by admins through the UI, so they're typically far shorter
    /// than this. The cap exists to make it impractical to abuse the endpoint with very large
    /// payloads — NCalc parsing time grows with the expression length.
    /// </remarks>
    public const int MaxExpressionLength = 4096;

    /// <summary>Preferred IANA media type for JavaScript responses (RFC 9239).</summary>
    public const string JavaScriptMediaType = "text/javascript";

    /// <summary>Legacy media type for JavaScript responses still in wide use.</summary>
    public const string JavaScriptMediaTypeLegacy = "application/javascript";

    /// <summary>Media type for the human-readable (plain-English) explanation of a rule.</summary>
    public const string PlainTextMediaType = "text/plain";

    private readonly IExpressionTranslator _translator;
    private readonly ILogger<ExpressionsController> _logger;

    /// <summary>Create a new <see cref="ExpressionsController"/>.</summary>
    /// <param name="translator">Translator used to convert the source expression to JavaScript.</param>
    /// <param name="logger">Logger; only used to record a debug entry per request so spikes show up in the logs.</param>
    public ExpressionsController(IExpressionTranslator translator, ILogger<ExpressionsController> logger)
    {
        _translator = translator;
        _logger = logger;
    }

    /// <summary>Translate a validation expression into the response media type requested via <c>Accept</c>.</summary>
    /// <remarks>
    /// The target language is selected through standard HTTP content negotiation:
    /// <list type="bullet">
    ///   <item><description><c>text/javascript</c> or <c>application/javascript</c> — returns a JavaScript expression (no statements, no surrounding function) ready to be wrapped in <c>new Function("V", "H", "return (...)")</c> on the client.</description></item>
    ///   <item><description><c>text/plain</c> — returns a human-readable (plain-English) explanation of the rule.</description></item>
    ///   <item><description>Missing <c>Accept</c>, <c>*/*</c>, <c>application/*</c> or <c>text/*</c> — defaults to JavaScript (RFC 9110 semantics).</description></item>
    ///   <item><description>Any other media type (e.g. <c>application/json</c>) — <c>406 Not Acceptable</c>.</description></item>
    /// </list>
    /// Translation is deterministic, so clients can cache the response keyed by the source expression.
    /// </remarks>
    /// <param name="request">The translation request; <see cref="TranslateExpressionRequest.Expression"/> must be non-empty and at most <see cref="MaxExpressionLength"/> characters long.</param>
    /// <returns>The translated expression body, served in the negotiated media type.</returns>
    /// <response code="200">The translation succeeded; <c>Content-Type</c> reflects the chosen target language.</response>
    /// <response code="400">The expression was missing, too long, or failed to parse.</response>
    /// <response code="406">No supported target language matched the <c>Accept</c> header.</response>
    [HttpPost("translate")]
    [Consumes("application/json")]
    [Produces(JavaScriptMediaType, JavaScriptMediaTypeLegacy, PlainTextMediaType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status406NotAcceptable)]
    public IActionResult Translate([FromBody] TranslateExpressionRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Expression))
            return BadRequest(new ProblemDetails { Title = "Expression must not be empty.", Status = StatusCodes.Status400BadRequest });

        if (request.Expression.Length > MaxExpressionLength)
            return BadRequest(new ProblemDetails
            {
                Title = $"Expression exceeds the {MaxExpressionLength}-character limit.",
                Status = StatusCodes.Status400BadRequest,
            });

        // Content negotiation drives the target language. Today only JS is supported; the
        // shape of this method leaves room for future targets (e.g. text/plain for a
        // human-readable explanation) without changing the route or request body.
        var target = NegotiateTarget(Request.Headers[HeaderNames.Accept]);
        if (target is null)
        {
            return new ObjectResult(new ProblemDetails
            {
                Title = "Unsupported target language.",
                Detail = $"This endpoint produces {JavaScriptMediaType} (or {JavaScriptMediaTypeLegacy}) and {PlainTextMediaType}. Set the Accept header accordingly.",
                Status = StatusCodes.Status406NotAcceptable,
            })
            {
                StatusCode = StatusCodes.Status406NotAcceptable,
            };
        }

        try
        {
            if (string.Equals(target, PlainTextMediaType, StringComparison.OrdinalIgnoreCase))
                return Content(_translator.TranslateToEnglish(request.Expression), PlainTextMediaType);

            var translation = _translator.TranslateToJavaScript(request.Expression);
            return Content(translation.Js, target);
        }
        catch (Exception ex)
        {
            // Most failures will be NCalcParserException with a useful message — surface them
            // verbatim so the schema editor can show the user what's wrong.
            _logger.LogDebug(ex, "Expression translation failed");
            return BadRequest(new ProblemDetails
            {
                Title = "Expression failed to parse.",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    }

    /// <summary>Check that an expression is syntactically well-formed.</summary>
    /// <remarks>
    /// The endpoint exists so the schema editor can show "red squiggles" as the admin types
    /// without having to ship the parser to the browser. The check is intentionally limited to
    /// syntax: unknown identifiers and unknown function names are <em>not</em> flagged here —
    /// they are reported by the full schema validation that runs when the schema is saved.
    ///
    /// A failed syntax check is a normal outcome (not an HTTP error), so the endpoint always
    /// returns <c>200 OK</c> with a JSON body describing the result. Protocol errors (empty body,
    /// over-length input) still surface as <c>400</c>.
    /// </remarks>
    /// <param name="request">The validation request; <see cref="TranslateExpressionRequest.Expression"/> must be non-empty and at most <see cref="MaxExpressionLength"/> characters long.</param>
    /// <returns>The validation outcome.</returns>
    /// <response code="200">The expression was checked. The body's <c>ok</c> flag tells whether it parsed cleanly.</response>
    /// <response code="400">The expression was missing or exceeded the length cap.</response>
    [HttpPost("validate")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ValidateExpressionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult Validate([FromBody] TranslateExpressionRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Expression))
            return BadRequest(new ProblemDetails { Title = "Expression must not be empty.", Status = StatusCodes.Status400BadRequest });

        if (request.Expression.Length > MaxExpressionLength)
            return BadRequest(new ProblemDetails
            {
                Title = $"Expression exceeds the {MaxExpressionLength}-character limit.",
                Status = StatusCodes.Status400BadRequest,
            });

        var result = _translator.ValidateSyntax(request.Expression);
        return Ok(new ValidateExpressionResponse(result.Ok, result.Error, result.Position));
    }

    /// <summary>Maximum number of expressions accepted in one dependency-batch request.</summary>
    /// <remarks>Comfortably above any real schema's rule count (five rule fields per value, plus schema-level rules) while keeping worst-case parse time bounded.</remarks>
    public const int MaxDependencyBatchSize = 500;

    /// <summary>
    /// Parse a batch of expressions with the real NCalc parser and report the identifiers each one
    /// references — the same engine (and the same <see cref="JsExpressionTranslation.Identifiers"/>
    /// extraction) used by <see cref="Translate"/>, batched into one round trip.
    /// </summary>
    /// <remarks>
    /// Powers the schema editor's "Dependencies" diagram: rather than a rough client-side guess at
    /// what a rule references, the SPA sends every rule on the schema (including unsaved edits) here
    /// and gets back the authoritative reference list for each, in the same order. A per-expression
    /// parse failure is a normal outcome (not an HTTP error) — that expression's entry carries
    /// <see cref="ExpressionDependencyResult.Error"/> and an empty identifier list, mirroring
    /// <see cref="Validate"/>'s "always 200" contract, so one bad rule doesn't blank out the whole
    /// diagram while the admin is mid-edit. Blank/whitespace-only entries resolve the same way,
    /// with no error — there's simply nothing to reference.
    /// </remarks>
    /// <param name="request">The batch; <see cref="ExpressionDependencyBatchRequest.Expressions"/> must be non-null and at most <see cref="MaxDependencyBatchSize"/> entries.</param>
    /// <returns>One <see cref="ExpressionDependencyResult"/> per input expression, in the same order.</returns>
    /// <response code="200">The batch was processed (individual expressions may still have failed to parse — see each result's <c>error</c>).</response>
    /// <response code="400">The request body was missing, or had more than <see cref="MaxDependencyBatchSize"/> expressions.</response>
    [HttpPost("dependencies")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ExpressionDependencyBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult Dependencies([FromBody] ExpressionDependencyBatchRequest request)
    {
        if (request?.Expressions is null)
            return BadRequest(new ProblemDetails { Title = "Expressions must not be null.", Status = StatusCodes.Status400BadRequest });

        if (request.Expressions.Count > MaxDependencyBatchSize)
            return BadRequest(new ProblemDetails
            {
                Title = $"Batch exceeds the {MaxDependencyBatchSize}-expression limit.",
                Status = StatusCodes.Status400BadRequest,
            });

        var results = request.Expressions.Select(ParseOne).ToList();
        return Ok(new ExpressionDependencyBatchResponse(results));
    }

    private ExpressionDependencyResult ParseOne(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return new ExpressionDependencyResult(Array.Empty<string>(), null);

        // A single oversized entry shouldn't fail the whole batch — every other rule on the
        // schema is still worth graphing.
        if (expression.Length > MaxExpressionLength)
            return new ExpressionDependencyResult(Array.Empty<string>(), $"Expression exceeds the {MaxExpressionLength}-character limit.");

        try
        {
            var translation = _translator.TranslateToJavaScript(expression);
            return new ExpressionDependencyResult(translation.Identifiers, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Dependency-batch expression failed to parse");
            return new ExpressionDependencyResult(Array.Empty<string>(), ex.Message);
        }
    }

    /// <summary>
    /// Pick a supported response media type given the request's <c>Accept</c> header values.
    /// Returns <c>null</c> when no entry matches — the caller turns that into a 406. Wildcard
    /// and missing Accept default to <see cref="JavaScriptMediaType"/>, matching RFC 9110.
    /// </summary>
    private static string? NegotiateTarget(Microsoft.Extensions.Primitives.StringValues acceptValues)
    {
        // Missing header altogether → caller has no preference; we pick our default.
        if (acceptValues.Count == 0) return JavaScriptMediaType;

        var entries = acceptValues
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(StripParameters)
            .Where(v => !string.IsNullOrEmpty(v))
            .ToArray();

        // Header was present but empty (e.g. `Accept: `) — same effect as missing.
        if (entries.Length == 0) return JavaScriptMediaType;

        foreach (var entry in entries)
        {
            if (string.Equals(entry, JavaScriptMediaType, StringComparison.OrdinalIgnoreCase)) return JavaScriptMediaType;
            if (string.Equals(entry, JavaScriptMediaTypeLegacy, StringComparison.OrdinalIgnoreCase)) return JavaScriptMediaTypeLegacy;
            if (string.Equals(entry, PlainTextMediaType, StringComparison.OrdinalIgnoreCase)) return PlainTextMediaType;
            if (entry == "*/*") return JavaScriptMediaType;
            if (string.Equals(entry, "application/*", StringComparison.OrdinalIgnoreCase)) return JavaScriptMediaTypeLegacy;
            if (string.Equals(entry, "text/*", StringComparison.OrdinalIgnoreCase)) return JavaScriptMediaType;
        }
        return null;
    }

    private static string StripParameters(string mediaType)
    {
        // Trim any quality / charset parameter — we don't honour q-values today; first match wins.
        var semi = mediaType.IndexOf(';');
        return (semi < 0 ? mediaType : mediaType[..semi]).Trim();
    }
}
