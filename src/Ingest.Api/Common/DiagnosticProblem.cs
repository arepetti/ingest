using Ingest.Core.Common;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Common;

/// <summary>Builds coded RFC 7807 responses for controller-level failures.</summary>
public static class DiagnosticProblem
{
    public static ProblemDetails Create(
        int status,
        string title,
        Diagnostic diagnostic,
        string? detail = null)
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

    public static ProblemDetails BadRequest(Diagnostic diagnostic, string? title = null, string? detail = null) =>
        Create(StatusCodes.Status400BadRequest, title ?? diagnostic.Message, diagnostic, detail);

    public static ProblemDetails NotFound(string resource, object? id = null) =>
        Create(
            StatusCodes.Status404NotFound,
            "Not found",
            Diagnostics.Common.NotFound(resource, id),
            $"{resource} not found.");

    public static ProblemDetails FeatureDisabled(string feature) =>
        Create(
            StatusCodes.Status404NotFound,
            "Not found",
            Diagnostic.Create(
                DiagnosticCodes.Common.FeatureDisabled,
                $"{feature} is disabled.",
                ("feature", feature)),
            $"{feature} is disabled.");

    public static ProblemDetails Unauthorized(string? reason = null) =>
        Create(
            StatusCodes.Status401Unauthorized,
            "Unauthorized",
            Diagnostic.Create(
                DiagnosticCodes.Common.Unauthorized,
                "Unauthorized",
                ("reason", reason)));
}
