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
            NotFoundException nf => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = nf.Message },
            ConflictException cx => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = cx.Message },
            ForbiddenException fx => new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Forbidden", Detail = fx.Message },
            ValidationException vx => BuildValidationProblem(vx),
            UnauthorizedAccessException => new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized" },
            _ => new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "Internal error" },
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception");

        httpContext.Response.StatusCode = problem.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    /// <summary>
    /// Validation failures are special: callers (especially the React UI) read individual errors
    /// from <c>extensions.errors</c>, so we surface the list alongside the standard problem-details
    /// shape instead of just stuffing them into <c>Detail</c>.
    /// </summary>
    private static ProblemDetails BuildValidationProblem(ValidationException vx)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Detail = vx.Message,
        };
        problem.Extensions["errors"] = vx.Errors;
        return problem;
    }
}
