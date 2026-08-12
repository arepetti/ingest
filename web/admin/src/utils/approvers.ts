import type { Account, ApproverRequirement, ApproverSpec, Schema } from '../api/types'
import type { TFunction } from 'i18next'

/**
 * Sentinel selection key used by the approval-policy editors to represent the dynamic
 * "service owner" approver. It is never sent to the server — the editors map it to/from an
 * {@link ApproverSpec} with `kind: 'ServiceOwner'` (whose `accountId` is resolved per submission).
 */
export const SERVICE_OWNER_KEY = '$serviceOwner'

/** Human-readable label for the service-owner approver option/row. */
export const SERVICE_OWNER_LABEL = 'Service owner (the submitting service)'

// The service-owner spec has no fixed account; the server ignores AccountId for that kind and
// binds it per submission. We still send the all-zero GUID rather than an empty string because the
// API's `accountId` is a (non-nullable) Guid and an empty string fails JSON deserialization.
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

/** Stable selection key for an approver spec: the sentinel for the service owner, else the account id. */
export function approverKey(a: ApproverSpec): string {
  return a.kind === 'ServiceOwner' ? SERVICE_OWNER_KEY : a.accountId
}

/** Build a fresh approver spec from a selection key (new approvers default to Required). */
export function approverFromKey(key: string, requirement: ApproverRequirement = 'Required'): ApproverSpec {
  return key === SERVICE_OWNER_KEY
    ? { accountId: EMPTY_GUID, requirement, kind: 'ServiceOwner' }
    : { accountId: key, requirement, kind: 'Account' }
}

/** Display label for an approver spec, given a lookup of the candidate accounts. */
export function approverLabel(a: ApproverSpec, accountsById: Map<string, Account>, t?: TFunction): string {
  if (a.kind === 'ServiceOwner') return t ? t('schemasSubmissions.approval.serviceOwner') : SERVICE_OWNER_LABEL
  return accountsById.get(a.accountId)?.label || accountsById.get(a.accountId)?.name || a.accountId
}

/**
 * Whether a schema's submissions are gated by approval — either its own policy is `Required`, or it
 * defers to a global default that currently requires approval. Always false when the workflow is off.
 * `globalDefaultRequired` comes from `/api/me` (so it works for non-admins who can't read the policy).
 */
export function schemaRequiresApproval(
  schema: Pick<Schema, 'approval'>,
  opts: { approvalEnabled: boolean; globalDefaultRequired: boolean },
): boolean {
  if (!opts.approvalEnabled) return false
  switch (schema.approval?.mode) {
    case 'Required':         return true
    case 'UseGlobalDefault': return opts.globalDefaultRequired
    default:                 return false
  }
}
