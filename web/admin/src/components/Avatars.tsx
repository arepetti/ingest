import type { ReactElement } from 'react'
import { Avatar, type AvatarProps } from '@fluentui/react-components'
import { ArrowRight20Regular, DocumentBulletList20Regular, Record20Regular, Resize20Regular } from '@fluentui/react-icons'
import type { Account, AccountRole, ApprovalStatus, AuditChangeType, AuditTargetType, EventKind, Schema, SchemaValueType } from '../api/types'
import { eventKindLabel } from '../utils/eventKind'
import { useTranslation } from 'react-i18next'

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
  const { t } = useTranslation()
  const active = account.enabled && !account.isDeleted
  return (
    <Avatar
      name={account.label || account.name}
      color={colorForRole(account.role, active)}
      size={size}
      badge={active ? undefined : { status: 'offline' }}
      aria-label={t('shell.avatars.account', {
        kind: t(`shell.account.kinds.${account.kind}`),
        role: t(`shell.account.roles.${account.role}`),
        context: active ? '' : t('shell.avatars.disabledSuffix'),
      })}
    />
  )
}

export function SchemaAvatar({ schema, size = 32 }: { schema: Schema; size?: AvatarSize }) {
  const { t } = useTranslation()
  const active = schema.enabled
  return (
    <Avatar
      name={schema.label || schema.name}
      // 'colorful' derives a stable color from the name — useful in long schema lists.
      color={active ? 'colorful' : 'anchor'}
      size={size}
      badge={active ? undefined : { status: 'offline' }}
      aria-label={active ? t('shell.avatars.schema') : t('shell.avatars.schemaDisabled')}
    />
  )
}

// Each event kind gets its own icon + colour so the leftmost column reads as "what shape of time
// span is this" at a glance: a dot for an instant, a span glyph for a bounded interval, and a
// forward arrow for an open-ended "from now on" event.
const EVENT_KIND_ICONS: Record<EventKind, ReactElement> = {
  PointInTime: <Record20Regular />,
  Interval: <Resize20Regular />,
  FromNowOn: <ArrowRight20Regular />,
}

const EVENT_KIND_COLORS: Record<EventKind, AvatarColor> = {
  PointInTime: 'cornflower',
  Interval: 'forest',
  FromNowOn: 'marigold',
}

export function EventKindAvatar({ kind, size = 32 }: { kind: EventKind; size?: AvatarSize }) {
  const { t } = useTranslation()
  return (
    <Avatar
      icon={EVENT_KIND_ICONS[kind]}
      color={EVENT_KIND_COLORS[kind]}
      size={size}
      aria-label={eventKindLabel(kind, t)}
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
  const { t } = useTranslation()
  // A draft is a lifecycle of its own (independent of approval), so it wins the tint and gets a
  // distinct grape colour (none of the approval states use it). Otherwise fall back to the
  // approval-status colour.
  const label = isDraft
    ? t('shell.avatars.submissionStatus', { status: t('shell.submissionStatus.Draft') })
    : status && status !== 'NotRequired'
      ? t('shell.avatars.submissionStatus', { status: t(`shell.submissionStatus.${status}`) })
      : t('shell.avatars.submission')
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

export function AuditChangeAvatar({ change, targetType, size = 32 }: { change: AuditChangeType; targetType: AuditTargetType; size?: AvatarSize }) {
  const { t } = useTranslation()
  return (
    <Avatar
      initials={t(`shell.auditTargetInitials.${targetType}`)}
      color={colorForChange(change)}
      size={size}
      aria-label={t('shell.avatars.auditChange', {
        target: t(`shell.auditTarget.${targetType}`),
        change: t(`shell.auditChange.${change}`),
      })}
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
  status, name, label, size = 32,
}: {
  status: DeliveryStatus
  /** Source text for the avatar's initials (e.g. the recipient, or the webhook event name). */
  name: string
  label?: string
  size?: AvatarSize
}) {
  const { t } = useTranslation()
  return (
    <Avatar
      name={name}
      color={colorForStatus(status)}
      size={size}
      aria-label={t('shell.avatars.deliveryStatus', {
        label: label ?? t('shell.avatars.status'),
        status: t(`shell.deliveryStatus.${status}`),
      })}
    />
  )
}

export function SchemaValueAvatar({ type, enabled, size = 32 }: { type: SchemaValueType; enabled: boolean; size?: AvatarSize }) {
  const { t } = useTranslation()
  // Mirrors SchemaAvatar: a stable per-type colour when enabled, neutral 'anchor' plus a small
  // offline badge when disabled — so disabled reads from the badge indicator, not a colour shade.
  return (
    <Avatar
      initials={t(`shell.valueTypeInitials.${type}`)}
      color={enabled ? 'colorful' : 'anchor'}
      idForColor={enabled ? type : undefined}
      size={size}
      badge={enabled ? undefined : { status: 'offline' }}
      aria-label={t('shell.avatars.schemaValue', {
        type: t(`shell.valueType.${type}`),
        context: enabled ? '' : t('shell.avatars.disabledSuffix'),
      })}
    />
  )
}
