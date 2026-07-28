using System.Net;
using Ingest.Api.Models;
using Ingest.Core.Entities;
using Ingest.IntegrationTests.Fixtures;

namespace Ingest.IntegrationTests;

/// <summary>
/// Schema comment threads: capability gating (read/create/manage), the create/reply/edit/delete
/// lifecycle, the "can't add to a resolved thread" rule, ownership-based edit rights, the
/// open-thread count aggregation, and that every write lands in the audit log.
/// </summary>
public sealed class CommentsTests : IntegrationTestBase
{
    public CommentsTests(IngestAppFixture fixture) : base(fixture) { }

    /// <summary>
    /// Create an enabled Operator account with an explicit capability override — stored verbatim
    /// (not the role's default bundle) — and mint an API key for it. No comments:* capability is
    /// granted to any role by default, so this is the only way to test the finer-grained rules.
    /// </summary>
    private async Task<(Guid AccountId, string ApiKey, string Name)> CreateAccountWithCapabilitiesAsync(params string[] capabilities)
    {
        var name = $"cmt-{Unique()}";
        var account = await (await Admin.PostJsonAsync("/api/admin/accounts", new
        {
            name,
            label = name,
            email = $"{name}@example.com",
            kind = "User",
            role = "Operator",
            enabled = true,
            capabilities,
        })).ReadAsync<AccountDto>();

        var key = await (await Admin.PostJsonAsync($"/api/admin/accounts/{account.Id}/keys", new { })).ReadAsync<GeneratedApiKeyResponse>();
        return (account.Id, key.Plaintext, name);
    }

    private Task<List<CommentThreadDto>> ListThreadsAsync(HttpClient client, Guid schemaId) =>
        client.GetAsync($"/api/admin/comments/threads?targetType=Schema&targetId={schemaId}")
            .ContinueWith(t => t.Result.ReadAsync<List<CommentThreadDto>>()).Unwrap();

    [Fact]
    public async Task Without_comments_read_every_endpoint_is_forbidden()
    {
        var schema = await CreateSchemaAsync();
        var (_, apiKey, _) = await CreateAccountWithCapabilitiesAsync();
        using var client = Fixture.CreateClient(apiKey);

        var response = await client.GetAsync($"/api/admin/comments/threads?targetType=Schema&targetId={schema.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Comments_read_alone_can_list_but_not_create()
    {
        var schema = await CreateSchemaAsync();
        var (_, apiKey, _) = await CreateAccountWithCapabilitiesAsync("comments:read");
        using var client = Fixture.CreateClient(apiKey);

        var list = await client.GetAsync($"/api/admin/comments/threads?targetType=Schema&targetId={schema.Id}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var create = await client.PostJsonAsync("/api/admin/comments/threads", new { targetType = "Schema", targetId = schema.Id, text = "hi" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_a_schema_level_thread_and_a_value_scoped_thread()
    {
        var schema = await CreateSchemaAsync();

        var general = await (await Admin.PostJsonAsync("/api/admin/comments/threads", new
        {
            targetType = "Schema", targetId = schema.Id, text = "General comment",
        })).ReadAsync<CommentThreadDto>();
        Assert.Null(general.ValueName);
        Assert.Single(general.Comments);
        Assert.Equal("General comment", general.Comments[0].Text);
        Assert.False(general.Comments[0].Edited);

        var scoped = await (await Admin.PostJsonAsync("/api/admin/comments/threads", new
        {
            targetType = "Schema", targetId = schema.Id, valueName = "count", text = "Value comment",
        })).ReadAsync<CommentThreadDto>();
        Assert.Equal("count", scoped.ValueName);

        var threads = await ListThreadsAsync(Admin, schema.Id);
        Assert.Equal(2, threads.Count);
    }

    [Fact]
    public async Task Create_thread_rejects_an_unknown_value_name()
    {
        var schema = await CreateSchemaAsync();
        var response = await Admin.PostJsonAsync("/api/admin/comments/threads", new
        {
            targetType = "Schema", targetId = schema.Id, valueName = "does_not_exist", text = "hi",
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_thread_rejects_a_nonexistent_schema()
    {
        var response = await Admin.PostJsonAsync("/api/admin/comments/threads", new
        {
            targetType = "Schema", targetId = Guid.NewGuid(), text = "hi",
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_thread_rejects_blank_text()
    {
        var schema = await CreateSchemaAsync();
        var response = await Admin.PostJsonAsync("/api/admin/comments/threads", new
        {
            targetType = "Schema", targetId = schema.Id, text = "   ",
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Resolved_thread_rejects_new_comments_until_reopened()
    {
        var schema = await CreateSchemaAsync();
        var thread = await (await Admin.PostJsonAsync("/api/admin/comments/threads", new
        {
            targetType = "Schema", targetId = schema.Id, text = "First",
        })).ReadAsync<CommentThreadDto>();

        var resolved = await (await Admin.PutJsonAsync($"/api/admin/comments/threads/{thread.Id}/resolved", new { resolved = true }))
            .ReadAsync<CommentThreadDto>();
        Assert.True(resolved.Resolved);

        var blocked = await Admin.PostJsonAsync($"/api/admin/comments/threads/{thread.Id}/comments", new { text = "Should fail" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        var reopened = await (await Admin.PutJsonAsync($"/api/admin/comments/threads/{thread.Id}/resolved", new { resolved = false }))
            .ReadAsync<CommentThreadDto>();
        Assert.False(reopened.Resolved);

        var reply = await Admin.PostJsonAsync($"/api/admin/comments/threads/{thread.Id}/comments", new { text = "Now it works" });
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
    }

    [Fact]
    public async Task Author_can_edit_their_own_comment_but_not_someone_elses_and_cannot_delete_it()
    {
        var schema = await CreateSchemaAsync();
        var (authorId, authorKey, _) = await CreateAccountWithCapabilitiesAsync("comments:read", "comments:create");
        using var author = Fixture.CreateClient(authorKey);
        var (_, otherKey, _) = await CreateAccountWithCapabilitiesAsync("comments:read", "comments:create");
        using var other = Fixture.CreateClient(otherKey);

        var thread = await (await author.PostJsonAsync("/api/admin/comments/threads", new
        {
            targetType = "Schema", targetId = schema.Id, text = "Original",
        })).ReadAsync<CommentThreadDto>();
        var commentId = thread.Comments[0].Id;
        Assert.Equal(authorId, thread.Comments[0].CreatedByAccountId);

        var ownEdit = await author.PutJsonAsync($"/api/admin/comments/{commentId}", new { text = "Edited by author" });
        Assert.Equal(HttpStatusCode.OK, ownEdit.StatusCode);
        var editedThread = await ownEdit.ReadAsync<CommentThreadDto>();
        Assert.True(editedThread.Comments[0].Edited);
        Assert.Equal("Edited by author", editedThread.Comments[0].Text);

        var othersEdit = await other.PutJsonAsync($"/api/admin/comments/{commentId}", new { text = "Hijacked" });
        Assert.Equal(HttpStatusCode.Forbidden, othersEdit.StatusCode);

        // Delete is comments:manage-only, even for the comment's own author.
        var authorDelete = await author.DeleteAsync($"/api/admin/comments/{commentId}");
        Assert.Equal(HttpStatusCode.Forbidden, authorDelete.StatusCode);
    }

    [Fact]
    public async Task Manage_can_edit_delete_and_resolve_anyone_elses_comment_or_thread()
    {
        var schema = await CreateSchemaAsync();
        var (_, authorKey, _) = await CreateAccountWithCapabilitiesAsync("comments:read", "comments:create");
        using var author = Fixture.CreateClient(authorKey);
        var (_, managerKey, _) = await CreateAccountWithCapabilitiesAsync("comments:read", "comments:manage");
        using var manager = Fixture.CreateClient(managerKey);

        var thread = await (await author.PostJsonAsync("/api/admin/comments/threads", new
        {
            targetType = "Schema", targetId = schema.Id, text = "Author's comment",
        })).ReadAsync<CommentThreadDto>();
        var commentId = thread.Comments[0].Id;

        var managerEdit = await manager.PutJsonAsync($"/api/admin/comments/{commentId}", new { text = "Edited by manager" });
        Assert.Equal(HttpStatusCode.OK, managerEdit.StatusCode);

        var managerResolve = await manager.PutJsonAsync($"/api/admin/comments/threads/{thread.Id}/resolved", new { resolved = true });
        Assert.Equal(HttpStatusCode.OK, managerResolve.StatusCode);

        var managerDelete = await manager.DeleteAsync($"/api/admin/comments/{commentId}");
        Assert.Equal(HttpStatusCode.NoContent, managerDelete.StatusCode);

        var afterDelete = await ListThreadsAsync(Admin, schema.Id);
        Assert.Empty(afterDelete.Single(t => t.Id == thread.Id).Comments);

        var threadDelete = await manager.DeleteAsync($"/api/admin/comments/threads/{thread.Id}");
        Assert.Equal(HttpStatusCode.NoContent, threadDelete.StatusCode);

        var afterThreadDelete = await ListThreadsAsync(Admin, schema.Id);
        Assert.DoesNotContain(afterThreadDelete, t => t.Id == thread.Id);
    }

    [Fact]
    public async Task Open_counts_aggregate_across_multiple_schemas_and_omit_zero_entries()
    {
        var schemaWithOpen = await CreateSchemaAsync();
        var schemaResolvedOnly = await CreateSchemaAsync();
        var schemaWithNoThreads = await CreateSchemaAsync();

        await Admin.PostJsonAsync("/api/admin/comments/threads", new { targetType = "Schema", targetId = schemaWithOpen.Id, text = "Open 1" });
        await Admin.PostJsonAsync("/api/admin/comments/threads", new { targetType = "Schema", targetId = schemaWithOpen.Id, text = "Open 2" });

        var resolvedThread = await (await Admin.PostJsonAsync("/api/admin/comments/threads", new
        {
            targetType = "Schema", targetId = schemaResolvedOnly.Id, text = "Will be resolved",
        })).ReadAsync<CommentThreadDto>();
        await Admin.PutJsonAsync($"/api/admin/comments/threads/{resolvedThread.Id}/resolved", new { resolved = true });

        var url = "/api/admin/comments/open-counts?targetType=Schema"
            + $"&targetIds={schemaWithOpen.Id}&targetIds={schemaResolvedOnly.Id}&targetIds={schemaWithNoThreads.Id}";
        var counts = (await (await Admin.GetAsync(url)).ReadJsonAsync()).GetProperty("counts");

        Assert.Equal(2, counts.GetProperty(schemaWithOpen.Id.ToString()).GetInt32());
        Assert.False(counts.TryGetProperty(schemaResolvedOnly.Id.ToString(), out _));
        Assert.False(counts.TryGetProperty(schemaWithNoThreads.Id.ToString(), out _));
    }

    [Fact]
    public async Task Every_comment_operation_is_audited()
    {
        var schema = await CreateSchemaAsync();
        var thread = await (await Admin.PostJsonAsync("/api/admin/comments/threads", new
        {
            targetType = "Schema", targetId = schema.Id, text = "First comment",
        })).ReadAsync<CommentThreadDto>();

        await Admin.PutJsonAsync($"/api/admin/comments/threads/{thread.Id}/resolved", new { resolved = true });
        await Admin.PutJsonAsync($"/api/admin/comments/threads/{thread.Id}/resolved", new { resolved = false });

        var reply = await (await Admin.PostJsonAsync($"/api/admin/comments/threads/{thread.Id}/comments", new { text = "A reply" }))
            .ReadAsync<CommentThreadDto>();
        var replyId = reply.Comments.Single(c => c.Text == "A reply").Id;
        await Admin.PutJsonAsync($"/api/admin/comments/{replyId}", new { text = "A reply (edited)" });
        await Admin.DeleteAsync($"/api/admin/comments/{replyId}");
        await Admin.DeleteAsync($"/api/admin/comments/threads/{thread.Id}");

        var threadLog = await (await Admin.GetAsync("/api/admin/audit?targetType=CommentThread&pageSize=200"))
            .ReadAsync<PagedResponse<AuditLogDto>>();
        var threadEntries = threadLog.Items.Where(e => e.TargetId == thread.Id).ToList();
        Assert.Contains(threadEntries, e => e.Change == AuditChangeType.Create);
        Assert.Equal(2, threadEntries.Count(e => e.Change == AuditChangeType.Edit)); // resolve + reopen
        Assert.Contains(threadEntries, e => e.Change == AuditChangeType.Delete);

        var commentLog = await (await Admin.GetAsync("/api/admin/audit?targetType=Comment&pageSize=200"))
            .ReadAsync<PagedResponse<AuditLogDto>>();
        var commentEntries = commentLog.Items.Where(e => e.TargetId == replyId).ToList();
        Assert.Contains(commentEntries, e => e.Change == AuditChangeType.Create);
        Assert.Contains(commentEntries, e => e.Change == AuditChangeType.Edit);
        Assert.Contains(commentEntries, e => e.Change == AuditChangeType.Delete);
    }
}
