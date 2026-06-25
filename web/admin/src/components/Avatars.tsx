import { Avatar, type AvatarProps } from '@fluentui/react-components'
import { DocumentBulletList20Regular } from '@fluentui/react-icons'
import type { Account, AccountRole, ApprovalStatus, AuditChangeType, AuditTargetType, Schema, SchemaValueType } from '../api/types'

type AvatarColor = AvatarProps['color']
type AvatarSize = AvatarProps['size']

/** Status of a queued email or webhook delivery — the avatar tints to match the row's status badge. */
export type DeliveryStatus = 'Pending' | 'Sending' | 'Sent' | 'Failed'

// Reserves a single color for each role so users can scan a long list and pick out
// service vs operator vs admin at a glance.
function colorForRole(role: AccountRole, active: boolean): AvatarColor {
  if (!active) return 'anchor'
  switch (role) {
    case 'Admin':    return 'grape'
    case 'Operator': return 'forest'
    case 'Service':  return 'cornflower'
    default:         return 'brand'
  }
}

export function AccountAvatar({ account, size = 32 }: { account: Account; size?: AvatarSize }) {
  const active = account.enabled && !account.isDeleted
  return (
    <Avatar
      name={account.label || account.name}
      color={colorForRole(account.role, active)}
      size={size}
      badge={active ? undefined : { status: 'offline' }}
      aria-label={`${account.kind} · ${account.role}${active ? '' : ' (disabled)'}`}
    />
  )
}

export function SchemaAvatar({ schema, size = 32 }: { schema: Schema; size?: AvatarSize }) {
  const active = schema.enabled
  return (
    <Avatar
      name={schema.label || schema.name}
      // 'colorful' derives a stable color from the name — useful in long schema lists.
      color={active ? 'colorful' : 'anchor'}
      size={size}
      badge={active ? undefined : { status: 'offline' }}
      aria-label={active ? 'Schema' : 'Schema (disabled)'}
    />
  )
}

// Approval status → avatar colour, mirroring the Status badge on the submissions grid so the
// leftmost column doubles as an at-a-glance approval signal. NotRequired (and legacy rows) stay
// neutral navy so nothing changes visually when the approval workflow is off.
function colorForApproval(status?: ApprovalStatus): AvatarColor {
  switch (status) {
    case 'Pending':  return 'marigold'
    case 'Approved': return 'forest'
    case 'Rejected': return 'red'
    default:         return 'navy'
  }
}

export function SubmissionAvatar({ status, isDraft = false, size = 32 }: { status?: ApprovalStatus; isDraft?: boolean; size?: AvatarSize }) {
  // A draft is a lifecycle of its own (independent of approval), so it wins the tint and gets a
  // distinct grape colour (none of the approval states use it). Otherwise fall back to the
  // approval-status colour.
  const label = isDraft ? 'Submission · Draft' : status && status !== 'NotRequired' ? `Submission · ${status}` : 'Submission'
  return (
    <Avatar
      icon={<DocumentBulletList20Regular />}
      color={isDraft ? 'grape' : colorForApproval(status)}
      size={size}
      aria-label={label}
    />
  )
}

// Audit "Changes" log → avatar colour mirrors the operation badge (create/edit/delete and the
// approval decisions) so the leftmost column reads as the action at a glance.
function colorForChange(change: AuditChangeType): AvatarColor {
  switch (change) {
    case 'Create':
    case 'Approve': return 'forest'
    case 'Delete':
    case 'Reject':  return 'red'
    default:        return 'royal-blue' // Edit
  }
}

// Short, distinct two-letter tags per target type (no icon). Distinct so colliding first letters
// (Schema vs Submission, Account vs ApiKey) stay tellable apart at a glance.
const TARGET_TYPE_INITIALS: Record<AuditTargetType, string> = {
  User:          'Us',
  Account:       'Ac',
  Schema:        'Sc',
  ApiKey:        'Ak',
  Submission:    'Sb',
  Report:        'Rp',
  SchemaHistory: 'Sh',
  ApprovalRule:  'Ar',
  Settings:      'St',
  Backup:        'Bk',
}

export function AuditChangeAvatar({ change, targetType, size = 32 }: { change: AuditChangeType; targetType: AuditTargetType; size?: AvatarSize }) {
  return (
    <Avatar
      initials={TARGET_TYPE_INITIALS[targetType]}
      color={colorForChange(change)}
      size={size}
      aria-label={`${targetType} · ${change}`}
    />
  )
}

// Email outbox / webhook delivery → avatar colour mirrors the status badge (ok / pending / error).
function colorForStatus(status: DeliveryStatus): AvatarColor {
  switch (status) {
    case 'Sent':    return 'forest'
    case 'Failed':  return 'red'
    case 'Sending': return 'royal-blue'
    default:        return 'marigold' // Pending
  }
}

export function StatusAvatar({
  status, name, label = 'Status', size = 32,
}: {
  status: DeliveryStatus
  /** Source text for the avatar's initials (e.g. the recipient, or the webhook event name). */
  name: string
  label?: string
  size?: AvatarSize
}) {
  return (
    <Avatar
      name={name}
      color={colorForStatus(status)}
      size={size}
      aria-label={`${label} · ${status}`}
    />
  )
}

// Single-letter tag per value type (Integer/Number/Date/Boolean/String all start distinctly).
const VALUE_TYPE_INITIALS: Record<SchemaValueType, string> = {
  String:  'S',
  Integer: 'I',
  Number:  'N',
  Date:    'D',
  Boolean: 'B',
}

export function SchemaValueAvatar({ type, enabled, size = 32 }: { type: SchemaValueType; enabled: boolean; size?: AvatarSize }) {
  // Mirrors SchemaAvatar: a stable per-type colour when enabled, neutral 'anchor' plus a small
  // offline badge when disabled — so disabled reads from the badge indicator, not a colour shade.
  return (
    <Avatar
      initials={VALUE_TYPE_INITIALS[type]}
      color={enabled ? 'colorful' : 'anchor'}
      idForColor={enabled ? type : undefined}
      size={size}
      badge={enabled ? undefined : { status: 'offline' }}
      aria-label={`${type}${enabled ? '' : ' (disabled)'}`}
    />
  )
}
