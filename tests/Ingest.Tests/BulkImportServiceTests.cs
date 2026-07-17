using Ingest.Core.Abstractions;
using Ingest.Core.Common;
using Ingest.Core.Entities;
using Ingest.Infrastructure.Services;

namespace Ingest.Tests;

/// <summary>
/// Tests for the ingestion kill-switch gate at the top of <see cref="BulkImportService.ImportAsync"/>.
/// Everything past the gate (parsing, per-group replay through <see cref="ISubmissionService.AdminCreateAsync"/>)
/// is covered by <c>BulkImportParserTests</c> and the integration suite; here we only pin that a
/// closed switch rejects before any of that runs, and that an open switch does not interfere.
/// </summary>
public class BulkImportServiceTests
{
    private const string ValidJson = """{ "submissions": [ { "samples": [ { "schemaName": "s", "valueName": "v", "value": 1, "timestamp": "2026-01-01T00:00:00Z" } ] } ] }""";

    [Fact]
    public async Task ImportAsync_throws_ServiceUnavailable_when_closed_without_calling_AdminCreateAsync()
    {
        var submissions = new RecordingSubmissionService();
        var appConfig = new FakeAppConfigurationService { Status = new IngestionStatus(true, "Maintenance window") };
        var sut = new BulkImportService(submissions, appConfig);

        var ex = await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            sut.ImportAsync(Guid.NewGuid(), BulkImportFormat.Json, ValidJson));

        Assert.Equal("Maintenance window", ex.Message);
        Assert.False(submissions.AdminCreateCalled);
    }

    [Fact]
    public async Task ImportAsync_falls_back_to_a_default_message_when_none_is_configured()
    {
        var submissions = new RecordingSubmissionService();
        var appConfig = new FakeAppConfigurationService { Status = new IngestionStatus(true, null) };
        var sut = new BulkImportService(submissions, appConfig);

        var ex = await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            sut.ImportAsync(Guid.NewGuid(), BulkImportFormat.Json, ValidJson));

        Assert.Equal("Submissions are temporarily closed.", ex.Message);
    }

    [Fact]
    public async Task ImportAsync_proceeds_past_the_gate_when_open()
    {
        var submissions = new RecordingSubmissionService();
        var appConfig = new FakeAppConfigurationService(); // Status defaults to open.
        var sut = new BulkImportService(submissions, appConfig);

        var result = await sut.ImportAsync(Guid.NewGuid(), BulkImportFormat.Json, ValidJson);

        Assert.Equal(1, result.Succeeded);
        Assert.True(submissions.AdminCreateCalled);
    }

    /// <summary>Bare-bones <see cref="ISubmissionService"/> fake: only <see cref="AdminCreateAsync"/> does anything; every other member throws if exercised (unused by this test).</summary>
    private sealed class RecordingSubmissionService : ISubmissionService
    {
        public bool AdminCreateCalled { get; private set; }

        public Task<SubmissionWriteResult> AdminCreateAsync(AdminSubmissionInput input, SubmissionSource source = SubmissionSource.Manual, DateTime? submittedAt = null, bool draft = false, CancellationToken ct = default)
        {
            AdminCreateCalled = true;
            var submission = new Submission { Id = Guid.NewGuid(), ServiceAccountId = input.ServiceAccountId };
            return Task.FromResult(new SubmissionWriteResult(submission, Array.Empty<string>()));
        }

        public Task<SubmissionWriteResult> CreateMineAsync(Guid callerAccountId, SubmissionInput input, SubmissionSource source = SubmissionSource.Api, bool draft = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SubmissionWriteResult> ReplaceMineAsync(Guid callerAccountId, Guid submissionId, SubmissionInput input, SubmissionSource source = SubmissionSource.Api, bool draft = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Submission?> GetMineAsync(Guid callerAccountId, Guid submissionId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<Submission>> ListMineAsync(Guid callerAccountId, PageRequest request, DateTime? from, DateTime? to, string? schemaName, bool? draft = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SubmissionValidationOutcome> ValidateMineAsync(Guid callerAccountId, SubmissionInput input, SubmissionSource source = SubmissionSource.Api, bool draft = false, SubmissionValidationOptions? options = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SubmissionValidationOutcome> ValidateMineReplaceAsync(Guid callerAccountId, Guid submissionId, SubmissionInput input, SubmissionSource source = SubmissionSource.Api, bool draft = false, SubmissionValidationOptions? options = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<Submission>> ListAsync(PageRequest request, Guid? serviceId, DateTime? from, DateTime? to, string? schemaName, ApprovalStatus? approvalStatus = null, bool? draft = null, IReadOnlyCollection<Guid>? allowedServiceIds = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Submission?> GetAsync(Guid submissionId, bool includeDeleted, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SubmissionWriteResult> AdminReplaceAsync(Guid submissionId, AdminSubmissionInput input, SubmissionSource source = SubmissionSource.Manual, bool draft = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SubmissionValidationOutcome> AdminValidateAsync(AdminSubmissionInput input, SubmissionSource source = SubmissionSource.Manual, bool draft = false, SubmissionValidationOptions? options = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(Guid submissionId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Submission> ApproveAsync(Guid approverAccountId, Guid submissionId, string? note, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Submission> RejectAsync(Guid approverAccountId, Guid submissionId, string? note, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CountPendingAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }
}
