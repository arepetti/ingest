import type { CommentThread, SchemaValue } from '../api/types'

/** Machine name used to mean "the schema as a whole" in the new-thread scope picker (never a real value name). */
export const GENERAL_SCOPE = ''

/**
 * Display label for a thread's scope: "General" for a schema-level thread (`valueName` is
 * null/blank), otherwise the scoped value's label — falling back to its machine name if the
 * value's label is unset, and to the raw stored name if the value itself can no longer be found
 * (e.g. it was since renamed or removed from the schema).
 */
export function threadScopeLabel(thread: Pick<CommentThread, 'valueName'>, values: readonly SchemaValue[]): string {
  if (!thread.valueName) return 'General'
  const value = values.find(v => v.name === thread.valueName)
  return value?.label || value?.name || thread.valueName
}

/** True when `accountId` authored the comment — drives the "edit your own comment" affordance. */
export function isOwnComment(comment: { createdByAccountId?: string | null }, accountId?: string | null): boolean {
  return !!accountId && comment.createdByAccountId === accountId
}
