import { Avatar, type AvatarProps } from '@fluentui/react-components'
import { DocumentBulletList20Regular } from '@fluentui/react-icons'
import type { Account, AccountRole, Schema } from '../api/types'

type AvatarColor = AvatarProps['color']
type AvatarSize = AvatarProps['size']

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

// Submissions don't have a meaningful axis to differentiate visually, so this is just a glyph
// to give the leftmost column a consistent visual anchor.
export function SubmissionAvatar({ size = 32 }: { size?: AvatarSize }) {
  return (
    <Avatar
      icon={<DocumentBulletList20Regular />}
      color="navy"
      size={size}
      aria-label="Submission"
    />
  )
}
