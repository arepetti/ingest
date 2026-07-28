using Ingest.Api.Auth;
using Ingest.Api.Common;
using Ingest.Api.Models;
using Ingest.Core.Abstractions;
using Ingest.Core.Entities;
using Ingest.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingest.Api.Controllers;

/// <summary>
/// Admin management of comment threads attached to schemas (and, later, other target types).
/// <c>comments:read</c> gates the whole controller (you need it to see anything);
/// <c>comments:create</c> gates starting threads/replying; <c>comments:manage</c> gates
/// delete/resolve of <i>any</i> thread or comment. Editing a comment's own text is available to its
/// author with just <c>comments:create</c> — see <see cref="EditComment"/>.
/// </summary>
[ApiController]
[Route("api/admin/comments")]
[Authorize(Policy = Capabilities.CommentsRead)]
public sealed class AdminCommentsController(ICommentsService comments) : ControllerBase
{
    /// <summary>List every thread attached to a target (schema-level and every value-scoped thread alike), each with its non-deleted comments.</summary>
    /// <param name="targetType">The kind of object the threads are attached to.</param>
    /// <param name="targetId">Id of the object the threads are attached to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The target's threads, unresolved-first then most-recently-active first.</response>
    [HttpGet("threads")]
    [ProducesResponseType(typeof(IReadOnlyList<CommentThreadDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListThreads([FromQuery] CommentTargetType targetType, [FromQuery] Guid targetId, CancellationToken ct)
    {
        var threads = await comments.ListThreadsAsync(targetType, targetId, ct);
        return Ok(threads.Select(CommentThreadDto.From).ToList());
    }

    /// <summary>Count open (unresolved, non-deleted) threads per target id, for a batch of targets in one round trip.</summary>
    /// <param name="targetType">The kind of object the threads are attached to.</param>
    /// <param name="targetIds">The target ids to count for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Open-thread counts keyed by target id; targets with zero open threads are omitted.</response>
    [HttpGet("open-counts")]
    [ProducesResponseType(typeof(OpenCommentCountsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> OpenCounts([FromQuery] CommentTargetType targetType, [FromQuery] List<Guid> targetIds, CancellationToken ct)
    {
        var counts = await comments.CountOpenThreadsAsync(targetType, targetIds, ct);
        return Ok(new OpenCommentCountsResponse(counts));
    }

    /// <summary>Start a new thread with its first comment.</summary>
    /// <param name="body">The target to attach to, optional value scope, and the first comment's text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The created thread.</response>
    /// <response code="400">The target doesn't exist, the value name is unknown, or the text is blank/too long.</response>
    [HttpPost("threads")]
    [Authorize(Policy = Capabilities.CommentsCreate)]
    [ProducesResponseType(typeof(CommentThreadDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateThread([FromBody] CreateCommentThreadRequest body, CancellationToken ct)
    {
        var thread = await comments.CreateThreadAsync(body.TargetType, body.TargetId, body.ValueName, body.Text, ct);
        var dto = CommentThreadDto.From(thread);
        return Created($"/api/admin/comments/threads/{thread.Id}", dto);
    }

    /// <summary>Append a reply to an existing (unresolved) thread.</summary>
    /// <param name="threadId">Id of the thread to reply to.</param>
    /// <param name="body">The reply's text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The thread, with the new comment appended.</response>
    /// <response code="400">The text is blank or too long.</response>
    /// <response code="404">No thread with that id exists.</response>
    /// <response code="409">The thread is resolved (locked) — reopen it first.</response>
    [HttpPost("threads/{threadId:guid}/comments")]
    [Authorize(Policy = Capabilities.CommentsCreate)]
    [ProducesResponseType(typeof(CommentThreadDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddComment(Guid threadId, [FromBody] AddCommentRequest body, CancellationToken ct)
    {
        var thread = await comments.AddCommentAsync(threadId, body.Text, ct);
        return Created($"/api/admin/comments/threads/{thread.Id}", CommentThreadDto.From(thread));
    }

    /// <summary>
    /// Edit a comment's text. Allowed for anyone holding <c>comments:manage</c>, or for the
    /// comment's own author provided they still hold <c>comments:create</c>.
    /// </summary>
    /// <param name="commentId">Id of the comment to edit.</param>
    /// <param name="body">The new text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The thread, with the comment's text updated.</response>
    /// <response code="400">The text is blank or too long.</response>
    /// <response code="403">The caller neither manages any comment nor owns this one.</response>
    /// <response code="404">No comment with that id exists.</response>
    [HttpPut("{commentId:guid}")]
    [ProducesResponseType(typeof(CommentThreadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EditComment(Guid commentId, [FromBody] EditCommentRequest body, CancellationToken ct)
    {
        var canManageAny = User.HasClaim(AuthConstants.CapabilityClaim, Capabilities.CommentsManage);
        var canCreate = User.HasClaim(AuthConstants.CapabilityClaim, Capabilities.CommentsCreate);
        if (!canManageAny && !canCreate)
            return Forbid();

        var thread = await comments.EditCommentAsync(commentId, body.Text, canManageAny, User.CurrentAccountId(), ct);
        return Ok(CommentThreadDto.From(thread));
    }

    /// <summary>Delete a single comment. Deletes any comment, not just the caller's own.</summary>
    /// <param name="commentId">Id of the comment to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The comment was deleted.</response>
    /// <response code="404">No comment with that id exists.</response>
    [HttpDelete("{commentId:guid}")]
    [Authorize(Policy = Capabilities.CommentsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteComment(Guid commentId, CancellationToken ct)
    {
        await comments.DeleteCommentAsync(commentId, ct);
        return NoContent();
    }

    /// <summary>Resolve or reopen a thread. Resolving locks it against new comments.</summary>
    /// <param name="threadId">Id of the thread to resolve/reopen.</param>
    /// <param name="body">The new resolved state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The updated thread.</response>
    /// <response code="404">No thread with that id exists.</response>
    [HttpPut("threads/{threadId:guid}/resolved")]
    [Authorize(Policy = Capabilities.CommentsManage)]
    [ProducesResponseType(typeof(CommentThreadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveThread(Guid threadId, [FromBody] ResolveThreadRequest body, CancellationToken ct)
    {
        var thread = await comments.ResolveThreadAsync(threadId, body.Resolved, ct);
        return Ok(CommentThreadDto.From(thread));
    }

    /// <summary>Delete a thread and every one of its comments. Deletes any thread, not just ones the caller started.</summary>
    /// <param name="threadId">Id of the thread to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The thread (and its comments) were deleted.</response>
    /// <response code="404">No thread with that id exists.</response>
    [HttpDelete("threads/{threadId:guid}")]
    [Authorize(Policy = Capabilities.CommentsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteThread(Guid threadId, CancellationToken ct)
    {
        await comments.DeleteThreadAsync(threadId, ct);
        return NoContent();
    }
}
