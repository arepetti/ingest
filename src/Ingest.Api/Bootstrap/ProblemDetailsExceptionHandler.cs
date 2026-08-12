using Ingest.Core.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Bootstrap;

/// <summary>
/// Central exception filter that turns domain exceptions (<see cref="DomainException"/> family)
/// into <c>ProblemDetails</c> responses with the right HTTP status. Anything we don't recognise
/// is logged and surfaces as a 500; the framework's default exception page is therefore never
/// shown to clients.
/// </summary>
public sealed class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

    /// <summary>Create a new <see cref="ProblemDetailsExceptionHandler"/>.</summary>
    /// <param name="logger">Logger; receives unhandled exceptions before the 500 is emitted.</param>
    public ProblemDetailsExceptionHandler(ILogger<ProblemDetailsExceptionHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem = exception switch
        {
            NotFoundException nf => BuildDomainProblem(StatusCodes.Status404NotFound, "Not found", nf),
            ConflictException cx => BuildDomainProblem(StatusCodes.Status409Conflict, "Conflict", cx),
            ForbiddenException fx => BuildDomainProblem(StatusCodes.Status403Forbidden, "Forbidden", fx),
            ServiceUnavailableException su => BuildDomainProblem(StatusCodes.Status503ServiceUnavailable, "Submissions closed", su),
            ValidationException vx => BuildValidationProblem(vx),
            UnauthorizedAccessException => BuildProblem(
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                detail: null,
                new Diagnostic(DiagnosticCodes.Common.Unauthorized, "Unauthorized")),
            _ => BuildProblem(
                StatusCodes.Status500InternalServerError,
                "Internal error",
                detail: null,
                new Diagnostic(DiagnosticCodes.Common.Internal, "Internal error")),
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception");

        httpContext.Response.StatusCode = problem.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static ProblemDetails BuildDomainProblem(int status, string title, DomainException exception) =>
        BuildProblem(status, title, exception.Message, exception.Diagnostic);

    private static ProblemDetails BuildProblem(int status, string title, string? detail, Diagnostic diagnostic)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
        };
        problem.Extensions["code"] = diagnostic.Code;
        problem.Extensions["params"] = diagnostic.Params;
        return problem;
    }

    /// <summary>
    /// Validation failures are special: callers (especially the React UI) read individual errors
    /// from <c>extensions.errors</c>, so we surface the list alongside the standard problem-details
    /// shape instead of just stuffing them into <c>Detail</c>.
    /// </summary>
    private static ProblemDetails BuildValidationProblem(ValidationException vx)
    {
        var problem = BuildProblem(
            StatusCodes.Status400BadRequest,
            "Validation failed",
            vx.Message,
            vx.Diagnostic);
        problem.Extensions["errors"] = vx.Errors;
        problem.Extensions["errorDetails"] = vx.ErrorDetails;
        return problem;
    }
}
