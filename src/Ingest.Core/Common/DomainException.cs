namespace Ingest.Core.Common;

/// <summary>
/// Base class for exceptions that represent expected business outcomes (not bugs). These are
/// mapped to <c>ProblemDetails</c> responses by the API exception handler instead of bubbling up
/// as 500s. Subclasses pick the HTTP status — the handler routes by exception type.
/// </summary>
public class DomainException : Exception
{
    /// <summary>Create a coded expected-business exception.</summary>
    public DomainException(Diagnostic diagnostic)
        : base(diagnostic.Message)
    {
        Diagnostic = diagnostic;
    }

    /// <summary>Create a new <see cref="DomainException"/>.</summary>
    /// <param name="message">Human-readable message; surfaces in <c>ProblemDetails.Detail</c>.</param>
    public DomainException(string message)
        : this(new Diagnostic(DiagnosticCodes.Common.Validation, message))
    {
    }

    /// <summary>Create a new <see cref="DomainException"/> wrapping a lower-level cause.</summary>
    /// <param name="message">Human-readable message; surfaces in <c>ProblemDetails.Detail</c>.</param>
    /// <param name="inner">The wrapped exception.</param>
    public DomainException(string message, Exception inner)
        : base(message, inner)
    {
        Diagnostic = new Diagnostic(DiagnosticCodes.Common.Validation, message);
    }

    /// <summary>The stable machine-readable diagnostic carried by this exception.</summary>
    public Diagnostic Diagnostic { get; }

    /// <summary>Convenience projection of <see cref="Diagnostic"/>.</summary>
    public string Code => Diagnostic.Code;

    /// <summary>Convenience projection of <see cref="Diagnostic"/>.</summary>
    public IReadOnlyDictionary<string, object?> Params => Diagnostic.Params;
}

/// <summary>Requested resource doesn't exist. Mapped to HTTP 404.</summary>
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string what) : base(Diagnostics.Common.NotFound(what)) { }
    public NotFoundException(Diagnostic diagnostic) : base(diagnostic) { }
}

/// <summary>Operation would violate a uniqueness or business-rule constraint. Mapped to HTTP 409.</summary>
public sealed class ConflictException : DomainException
{
    public ConflictException(string message) : base(Diagnostics.Common.Conflict(message)) { }
    public ConflictException(Diagnostic diagnostic) : base(diagnostic) { }
}

/// <summary>
/// Caller is authenticated but not allowed to act on this resource (foreign owner, closed cadence
/// window, …). Mapped to HTTP 403. Use this rather than <see cref="UnauthorizedAccessException"/>
/// for application-level "you can't do that" cases — the latter implies "you aren't signed in".
/// </summary>
public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string message) : base(Diagnostics.Common.Forbidden(message)) { }
    public ForbiddenException(Diagnostic diagnostic) : base(diagnostic) { }
}

/// <summary>
/// The operation can't be performed right now because the server has deliberately taken itself
/// offline for it (the ingestion kill switch). Mapped to HTTP 503. Distinct from an infrastructure
/// outage: this is an intentional, operator-toggled state with a caller-facing explanation.
/// </summary>
public sealed class ServiceUnavailableException : DomainException
{
    public ServiceUnavailableException(string message) : base(Diagnostics.Common.ServiceUnavailable(message)) { }
    public ServiceUnavailableException(Diagnostic diagnostic) : base(diagnostic) { }
}

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
        : this(errors.Select(x => Diagnostics.Common.LegacyValidation(x)).ToList())
    {
    }

    /// <summary>Create structured validation errors sharing one stable domain code.</summary>
    public ValidationException(string code, IReadOnlyList<string> errors, string? domain = null)
        : this(errors.Select((message, index) => Diagnostic.Create(
            code,
            message,
            ("domain", domain),
            ("index", index))).ToList())
    {
    }

    /// <summary>Create a new <see cref="ValidationException"/> from structured rule errors.</summary>
    public ValidationException(IReadOnlyList<Diagnostic> errors)
        : base(BuildSummary(errors))
    {
        ErrorDetails = errors;
        Errors = errors.Select(x => x.Message).ToList();
    }

    /// <summary>The list of rule errors. Never null; may be empty in degenerate cases.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Structured counterparts to <see cref="Errors"/>, in the same order.</summary>
    public IReadOnlyList<Diagnostic> ErrorDetails { get; }

    private static Diagnostic BuildSummary(IReadOnlyList<Diagnostic> errors) =>
        Diagnostic.Create(
            DiagnosticCodes.Common.Validation,
            "Validation failed: " + string.Join("; ", errors.Select(x => x.Message)),
            ("count", errors.Count));
}
