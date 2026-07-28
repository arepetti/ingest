import { useMemo, useState } from 'react'
import {
  Avatar, Badge, Button, Card, Divider, Drawer, DrawerBody, Dropdown, Field, Option, Spinner,
  Text, Textarea,
  Menu, MenuButton, MenuItem, MenuList, MenuPopover, MenuTrigger,
  MessageBarBody,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { CheckmarkCircle20Regular, Delete20Regular, Edit20Regular, LockClosed20Regular, MoreHorizontal20Regular } from '@fluentui/react-icons'
import { AutoScrollMessageBar } from './AutoScrollMessageBar'
import { DrawerHeaderWithClose } from './DrawerHeaderWithClose'
import { formatApiError } from '../api/client'
import { formatDateTime } from '../utils/format'
import { confirmDelete } from '../utils/confirm'
import { GENERAL_SCOPE, isOwnComment, threadScopeLabel } from '../utils/comments'
import {
  useAddComment, useCapabilities, useCommentThreads, useCreateCommentThread, useDeleteComment,
  useDeleteThread, useEditComment, useResolveThread,
} from '../api/hooks'
import type { SchemaValue } from '../api/types'

const useStyles = makeStyles({
  drawer: { width: 'max(480px, 36vw)' },
  body: { display: 'flex', flexDirection: 'column', gap: '16px', padding: '16px' },
  composer: { display: 'flex', flexDirection: 'column', gap: '8px' },
  composerActions: { display: 'flex', justifyContent: 'flex-end', gap: '8px' },
  threadCard: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '12px' },
  threadHeader: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '8px' },
  threadHeaderLeft: { display: 'flex', alignItems: 'center', gap: '8px' },
  comment: { display: 'flex', gap: '8px' },
  commentBody: { flex: 1, display: 'flex', flexDirection: 'column', gap: '4px', minWidth: 0 },
  commentMeta: { display: 'flex', alignItems: 'center', gap: '6px', color: tokens.colorNeutralForeground3, fontSize: '12px' },
  commentText: { whiteSpace: 'pre-wrap', wordBreak: 'break-word' },
  commentActions: { display: 'flex', gap: '4px' },
  resolvedNote: {
    display: 'flex', alignItems: 'center', gap: '6px',
    color: tokens.colorNeutralForeground3, fontStyle: 'italic', fontSize: '13px',
  },
  errorText: { color: tokens.colorPaletteRedForeground1, fontSize: '12px' },
  empty: { color: tokens.colorNeutralForeground3, textAlign: 'center', padding: '24px 0' },
})

/**
 * Right-hand drawer showing every comment thread on a schema (schema-level plus any scoped to a
 * specific value), with composers to start new threads, reply, edit, delete and resolve/reopen.
 * `comments:read` is assumed to already gate whether this is even reachable (checked by the
 * caller before rendering the toolbar button that opens it).
 */
export function SchemaCommentsDrawer({
  open, onClose, schemaId, values,
}: {
  open: boolean
  onClose: () => void
  schemaId: string
  values: SchemaValue[]
}) {
  const s = useStyles()
  const { has, me } = useCapabilities()
  const canCreate = has('comments:create')
  const canManage = has('comments:manage')

  const { data: threads, isLoading, error } = useCommentThreads('Schema', schemaId, open)

  const createThread = useCreateCommentThread()
  const addComment = useAddComment()
  const editComment = useEditComment()
  const deleteComment = useDeleteComment()
  const resolveThread = useResolveThread()
  const deleteThread = useDeleteThread()

  const [newScope, setNewScope] = useState<string>(GENERAL_SCOPE)
  const [newText, setNewText] = useState('')
  const [newError, setNewError] = useState<string | null>(null)

  const [replyDrafts, setReplyDrafts] = useState<Record<string, string>>({})
  const [replyErrors, setReplyErrors] = useState<Record<string, string | null>>({})
  const [editingCommentId, setEditingCommentId] = useState<string | null>(null)
  const [editText, setEditText] = useState('')
  const [editError, setEditError] = useState<string | null>(null)

  const scopeOptions = useMemo(
    () => [{ name: GENERAL_SCOPE, label: 'General' }, ...values.map(v => ({ name: v.name, label: v.label || v.name }))],
    [values],
  )

  async function submitNewThread() {
    const text = newText.trim()
    if (!text) { setNewError('Comment text is required.'); return }
    setNewError(null)
    try {
      await createThread.mutateAsync({ targetType: 'Schema', targetId: schemaId, valueName: newScope || null, text })
      setNewScope(GENERAL_SCOPE)
      setNewText('')
    } catch (e) {
      setNewError(formatApiError(e))
    }
  }

  async function submitReply(threadId: string) {
    const text = (replyDrafts[threadId] ?? '').trim()
    if (!text) { setReplyErrors(prev => ({ ...prev, [threadId]: 'Comment text is required.' })); return }
    setReplyErrors(prev => ({ ...prev, [threadId]: null }))
    try {
      await addComment.mutateAsync({ threadId, text })
      setReplyDrafts(prev => ({ ...prev, [threadId]: '' }))
    } catch (e) {
      setReplyErrors(prev => ({ ...prev, [threadId]: formatApiError(e) }))
    }
  }

  function startEdit(commentId: string, text: string) {
    setEditingCommentId(commentId)
    setEditText(text)
    setEditError(null)
  }

  async function submitEdit() {
    if (!editingCommentId) return
    const text = editText.trim()
    if (!text) { setEditError('Comment text is required.'); return }
    try {
      await editComment.mutateAsync({ commentId: editingCommentId, text })
      setEditingCommentId(null)
    } catch (e) {
      setEditError(formatApiError(e))
    }
  }

  function onDeleteComment(commentId: string) {
    if (!confirmDelete('comment')) return
    deleteComment.mutate(commentId)
  }

  function onDeleteThread(threadId: string) {
    if (!confirmDelete('thread', undefined, 'This deletes every comment in the thread. This cannot be undone.')) return
    deleteThread.mutate(threadId)
  }

  return (
    <Drawer type="overlay" separator open={open} onOpenChange={(_, d) => { if (!d.open) onClose() }} position="end" className={s.drawer}>
      <DrawerHeaderWithClose title="Comments" onClose={onClose} />
      <DrawerBody>
        <div className={s.body}>
          {error && (
            <AutoScrollMessageBar intent="error">
              <MessageBarBody>{formatApiError(error)}</MessageBarBody>
            </AutoScrollMessageBar>
          )}

          {canCreate && (
            <Card className={s.threadCard}>
              <Text weight="semibold">New thread</Text>
              <div className={s.composer}>
                <Field label="Scope">
                  <Dropdown
                    selectedOptions={[newScope]}
                    value={scopeOptions.find(o => o.name === newScope)?.label ?? 'General'}
                    onOptionSelect={(_, d) => setNewScope(d.optionValue ?? GENERAL_SCOPE)}
                  >
                    {scopeOptions.map(o => (
                      <Option key={o.name || 'general'} value={o.name} text={o.label}>{o.label}</Option>
                    ))}
                  </Dropdown>
                </Field>
                <Textarea placeholder="Write a comment…" value={newText} onChange={(_, d) => setNewText(d.value)} resize="vertical" />
                {newError && <Text className={s.errorText}>{newError}</Text>}
                <div className={s.composerActions}>
                  <Button appearance="primary" disabled={createThread.isPending || !newText.trim()} onClick={submitNewThread}>
                    Post
                  </Button>
                </div>
              </div>
            </Card>
          )}

          {isLoading && <Spinner label="Loading comments…" />}

          {!isLoading && threads && threads.length === 0 && (
            <div className={s.empty}>No comments yet.</div>
          )}

          {!isLoading && threads?.map(thread => (
            <Card key={thread.id} className={s.threadCard}>
              <div className={s.threadHeader}>
                <div className={s.threadHeaderLeft}>
                  <Badge appearance="tint" color="informative">{threadScopeLabel(thread, values)}</Badge>
                  <Badge appearance="tint" color={thread.resolved ? 'success' : 'warning'}>
                    {thread.resolved ? 'Resolved' : 'Open'}
                  </Badge>
                </div>
                {canManage && (
                  <Menu>
                    <MenuTrigger disableButtonEnhancement>
                      <MenuButton appearance="subtle" size="small" icon={<MoreHorizontal20Regular />} aria-label="Thread actions" />
                    </MenuTrigger>
                    <MenuPopover>
                      <MenuList>
                        <MenuItem
                          icon={<CheckmarkCircle20Regular />}
                          onClick={() => resolveThread.mutate({ threadId: thread.id, resolved: !thread.resolved })}
                        >
                          {thread.resolved ? 'Reopen' : 'Resolve'}
                        </MenuItem>
                        <MenuItem icon={<Delete20Regular />} onClick={() => onDeleteThread(thread.id)}>
                          Delete thread
                        </MenuItem>
                      </MenuList>
                    </MenuPopover>
                  </Menu>
                )}
              </div>

              <Divider />

              {thread.comments.map(comment => {
                const own = isOwnComment(comment, me?.id)
                const canEditThis = canManage || (own && canCreate)
                const isEditing = editingCommentId === comment.id
                return (
                  <div key={comment.id} className={s.comment}>
                    <Avatar name={comment.createdBy ?? 'Unknown'} size={24} />
                    <div className={s.commentBody}>
                      <div className={s.commentMeta}>
                        <Text weight="semibold" size={200}>{comment.createdBy ?? 'Unknown'}</Text>
                        <span>{formatDateTime(comment.createdAt)}</span>
                        {comment.edited && <span>(edited)</span>}
                      </div>
                      {isEditing ? (
                        <div className={s.composer}>
                          <Textarea value={editText} onChange={(_, d) => setEditText(d.value)} resize="vertical" />
                          {editError && <Text className={s.errorText}>{editError}</Text>}
                          <div className={s.composerActions}>
                            <Button appearance="secondary" size="small" onClick={() => setEditingCommentId(null)}>Cancel</Button>
                            <Button appearance="primary" size="small" disabled={editComment.isPending} onClick={submitEdit}>Save</Button>
                          </div>
                        </div>
                      ) : (
                        <>
                          <Text className={s.commentText}>{comment.text}</Text>
                          {canEditThis && (
                            <div className={s.commentActions}>
                              <Button appearance="subtle" size="small" icon={<Edit20Regular />} onClick={() => startEdit(comment.id, comment.text)}>
                                Edit
                              </Button>
                              {canManage && (
                                <Button appearance="subtle" size="small" icon={<Delete20Regular />} onClick={() => onDeleteComment(comment.id)}>
                                  Delete
                                </Button>
                              )}
                            </div>
                          )}
                        </>
                      )}
                    </div>
                  </div>
                )
              })}

              {thread.resolved ? (
                <div className={s.resolvedNote}>
                  <LockClosed20Regular />
                  <span>This thread is resolved{canManage ? ' — reopen it to add another comment.' : '.'}</span>
                </div>
              ) : canCreate ? (
                <div className={s.composer}>
                  <Textarea
                    placeholder="Reply…"
                    value={replyDrafts[thread.id] ?? ''}
                    onChange={(_, d) => setReplyDrafts(prev => ({ ...prev, [thread.id]: d.value }))}
                    resize="vertical"
                  />
                  {replyErrors[thread.id] && <Text className={s.errorText}>{replyErrors[thread.id]}</Text>}
                  <div className={s.composerActions}>
                    <Button
                      appearance="primary"
                      size="small"
                      disabled={addComment.isPending || !(replyDrafts[thread.id] ?? '').trim()}
                      onClick={() => submitReply(thread.id)}
                    >
                      Reply
                    </Button>
                  </div>
                </div>
              ) : null}
            </Card>
          ))}
        </div>
      </DrawerBody>
    </Drawer>
  )
}
