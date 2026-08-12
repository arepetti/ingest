using System.Text.Json;
using Ingest.Api.Bootstrap;
using Ingest.Api.Models;
using Ingest.Core.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ingest.Tests;

public sealed class DiagnosticContractTests
{
    [Fact]
    public async Task Single_domain_failure_adds_code_and_params_without_changing_fallback_fields()
    {
        var diagnostic = Diagnostic.Create(
            DiagnosticCodes.Accounts.AlreadyExists,
            "Account 'service-a' already exists.",
            ("accountName", "service-a"));
        var exception = new ConflictException(diagnostic);

        using var body = await HandleAsync(exception);
        var root = body.RootElement;

        Assert.Equal(StatusCodes.Status409Conflict, root.GetProperty("status").GetInt32());
        Assert.Equal("Conflict", root.GetProperty("title").GetString());
        Assert.Equal(diagnostic.Message, root.GetProperty("detail").GetString());
        Assert.Equal(diagnostic.Code, root.GetProperty("code").GetString());
        Assert.Equal("service-a", root.GetProperty("params").GetProperty("accountName").GetString());
    }

    [Fact]
    public async Task Validation_failure_preserves_errors_and_adds_parallel_error_details()
    {
        var details = new[]
        {
            Diagnostic.Create(
                DiagnosticCodes.Submissions.ValueRequired,
                "Value 'Weekly / Headcount' requires a value.",
                ("schemaName", "weekly"),
                ("valueName", "headcount")),
            Diagnostic.Create(
                DiagnosticCodes.Submissions.DuplicatePeriod,
                "Value 'Weekly / Headcount' already submitted for this weekly period.",
                ("schemaName", "weekly"),
                ("valueName", "headcount"),
                ("cadence", "weekly")),
        };

        using var body = await HandleAsync(new ValidationException(details));
        var root = body.RootElement;

        Assert.Equal("Validation failed", root.GetProperty("title").GetString());
        Assert.Equal(
            "Validation failed: " + string.Join("; ", details.Select(x => x.Message)),
            root.GetProperty("detail").GetString());
        Assert.Equal(details.Select(x => x.Message), root.GetProperty("errors").EnumerateArray().Select(x => x.GetString()));

        var structured = root.GetProperty("errorDetails").EnumerateArray().ToArray();
        Assert.Equal(2, structured.Length);
        Assert.Equal(DiagnosticCodes.Submissions.ValueRequired, structured[0].GetProperty("code").GetString());
        Assert.Equal("headcount", structured[0].GetProperty("params").GetProperty("valueName").GetString());
        Assert.Equal(DiagnosticCodes.Submissions.DuplicatePeriod, structured[1].GetProperty("code").GetString());
        Assert.Equal("weekly", structured[1].GetProperty("params").GetProperty("cadence").GetString());
    }

    [Fact]
    public void Success_dto_keeps_warning_strings_and_adds_warning_details()
    {
        var warning = Diagnostic.Create(
            DiagnosticCodes.Submissions.WarningRuleTriggered,
            "Sample 'Weekly / Headcount': warning rule triggered.",
            ("schemaName", "weekly"),
            ("valueName", "headcount"));
        var dto = new SubmissionWriteResponse(Guid.NewGuid(), new[] { warning.Message })
        {
            WarningDetails = new[] { warning },
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;

        Assert.Equal(warning.Message, root.GetProperty("warnings")[0].GetString());
        Assert.Equal(warning.Code, root.GetProperty("warningDetails")[0].GetProperty("code").GetString());
        Assert.Equal("headcount", root.GetProperty("warningDetails")[0].GetProperty("params").GetProperty("valueName").GetString());
    }

    [Fact]
    public void Expression_success_contract_keeps_error_and_adds_error_detail()
    {
        const string message = "Unexpected token at position 4.";
        var diagnostic = Diagnostic.Create(
            DiagnosticCodes.Expressions.ParseFailed,
            message,
            ("position", 4));
        var dto = new ValidateExpressionResponse(false, message, 4)
        {
            ErrorDetail = diagnostic,
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;

        Assert.Equal(message, root.GetProperty("error").GetString());
        Assert.Equal(4, root.GetProperty("position").GetInt32());
        Assert.Equal(diagnostic.Code, root.GetProperty("errorDetail").GetProperty("code").GetString());
        Assert.Equal(4, root.GetProperty("errorDetail").GetProperty("params").GetProperty("position").GetInt32());
    }

    private static async Task<JsonDocument> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var handler = new ProblemDetailsExceptionHandler(NullLogger<ProblemDetailsExceptionHandler>.Instance);

        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }
}
