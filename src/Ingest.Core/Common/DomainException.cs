namespace Ingest.Core.Common;

/// <summary>
/// Base class for exceptions that represent expected business outcomes (not bugs). These are
/// mapped to <c>ProblemDetails</c> responses by the API exception handler instead of bubbling up
/// as 500s. Subclasses pick the HTTP status — the handler routes by exception type.
/// </summary>
public class DomainException : Exception
{
    /// <summary>Create a new <see cref="DomainException"/>.</summary>
    /// <param name="message">Human-readable message; surfaces in <c>ProblemDetails.Detail</c>.</param>
    public DomainException(string message) : base(message) { }

    /// <summary>Create a new <see cref="DomainException"/> wrapping a lower-level cause.</summary>
    /// <param name="message">Human-readable message; surfaces in <c>ProblemDetails.Detail</c>.</param>
    /// <param name="inner">The wrapped exception.</param>
    public DomainException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Requested resource doesn't exist. Mapped to HTTP 404.</summary>
/// <param name="what">Short noun (e.g. "Account") used to compose the message.</param>
public sealed class NotFoundException(string what) : DomainException($"{what} not found.");

/// <summary>Operation would violate a uniqueness or business-rule constraint. Mapped to HTTP 409.</summary>
/// <param name="message">Caller-facing message.</param>
public sealed class ConflictException(string message) : DomainException(message);

/// <summary>
/// Caller is authenticated but not allowed to act on this resource (foreign owner, closed cadence
/// window, …). Mapped to HTTP 403. Use this rather than <see cref="UnauthorizedAccessException"/>
/// for application-level "you can't do that" cases — the latter implies "you aren't signed in".
/// </summary>
/// <param name="message">Caller-facing message.</param>
public sealed class ForbiddenException(string message) : DomainException(message);

/// <summary>
/// The operation can't be performed right now because the server has deliberately taken itself
/// offline for it (the ingestion kill switch). Mapped to HTTP 503. Distinct from an infrastructure
/// outage: this is an intentional, operator-toggled state with a caller-facing explanation.
/// </summary>
/// <param name="message">Caller-facing message; the configured kill-switch message when set.</param>
public sealed class ServiceUnavailableException(string message) : DomainException(message);

/// <summary>
/// One or more validation rules rejected the input. Mapped to HTTP 400 with an <c>errors</c>
/// extension on the problem-details body. The individual rule messages are carried in
/// <see cref="Errors"/>.
/// </summary>
public sealed class ValidationException : DomainException
{
    /// <summary>Create a new <see cref="ValidationException"/> from a collection of rule errors.</summary>
    /// <param name="errors">The list of rule errors; surfaces verbatim under <c>extensions.errors</c>.</param>
    public ValidationException(IReadOnlyList<string> errors)
        : base("Validation failed: " + string.Join("; ", errors))
    {
        Errors = errors;
    }

    /// <summary>The list of rule errors. Never null; may be empty in degenerate cases.</summary>
    public IReadOnlyList<string> Errors { get; }
}
