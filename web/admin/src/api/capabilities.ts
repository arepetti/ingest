import type { AccountRole } from './types'

/**
 * The capability catalogue, mirroring `Ingest.Core.Security.Capabilities` on the server. Capabilities
 * are the real unit of authorization; roles are decorative templates that seed a default bundle.
 * The SPA gates sidebar entries, pages, buttons and row actions off the effective capability set
 * returned by `/api/me`.
 */
export const CAPABILITIES = {
  schemasRead: 'schemas:read',
  schemasManage: 'schemas:manage',
  submissionsRead: 'submissions:read',
  submissionsSubmit: 'submissions:submit',
  submissionsDelete: 'submissions:delete',
  submissionsApprove: 'submissions:approve',
  queryRead: 'query:read',
  exploreRead: 'explore:read',
  statusRead: 'status:read',
  reportsRead: 'reports:read',
  reportsManage: 'reports:manage',
  accountsRead: 'accounts:read',
  accountsManage: 'accounts:manage',
  apiKeysRead: 'apikeys:read',
  apiKeysManage: 'apikeys:manage',
  auditRead: 'audit:read',
  webhooksRead: 'webhooks:read',
  webhooksManage: 'webhooks:manage',
  notificationsRead: 'notifications:read',
  notificationsManage: 'notifications:manage',
  privacyRead: 'privacy:read',
  privacyManage: 'privacy:manage',
  backupRead: 'backup:read',
  backupManage: 'backup:manage',
  settingsRead: 'settings:read',
  settingsManage: 'settings:manage',
} as const

export type Capability = (typeof CAPABILITIES)[keyof typeof CAPABILITIES]

/** True when an account's resolved (effective) capability set includes the given capability. */
export function accountHasCapability(account: { effectiveCapabilities?: Capability[] }, cap: Capability): boolean {
  return (account.effectiveCapabilities ?? []).includes(cap)
}

/** A capability the admin permissions panel can grant, with its display metadata. */
export interface CapabilityInfo {
  id: Capability
  label: string
  description: string
}

/** A labelled group of related capabilities, used to render the permissions picker. */
export interface CapabilityGroup {
  group: string
  items: CapabilityInfo[]
}

/**
 * The catalogue grouped for display in the account permissions panel. The order here is the order
 * shown in the UI; it matches the server-side `Capabilities.All` grouping.
 */
export const CAPABILITY_GROUPS: CapabilityGroup[] = [
  {
    group: 'Schemas',
    items: [
      { id: CAPABILITIES.schemasRead, label: 'View schemas', description: 'Browse the schema catalogue, detail and history.' },
      { id: CAPABILITIES.schemasManage, label: 'Manage schemas', description: 'Create, edit, clone and delete schemas.' },
    ],
  },
  {
    group: 'Submissions',
    items: [
      { id: CAPABILITIES.submissionsRead, label: 'View submissions', description: 'See submissions across every service.' },
      { id: CAPABILITIES.submissionsSubmit, label: 'Submit on behalf', description: 'Create, edit and bulk-import submissions for a service.' },
      { id: CAPABILITIES.submissionsDelete, label: 'Delete submissions', description: 'Permanently remove a submission.' },
      { id: CAPABILITIES.submissionsApprove, label: 'Approve submissions', description: 'Approve or reject pending submissions.' },
    ],
  },
  {
    group: 'Analytics',
    items: [
      { id: CAPABILITIES.queryRead, label: 'Query data', description: 'Use the OData feed and ad-hoc query endpoint.' },
      { id: CAPABILITIES.exploreRead, label: 'Explore', description: 'Use the in-app Explore analytics.' },
      { id: CAPABILITIES.statusRead, label: 'View status', description: 'See cross-service status and missing-submission analytics.' },
    ],
  },
  {
    group: 'Reports',
    items: [
      { id: CAPABILITIES.reportsRead, label: 'View reports', description: 'Browse and render the report catalogue.' },
      { id: CAPABILITIES.reportsManage, label: 'Manage reports', description: 'Upload and delete report definitions.' },
    ],
  },
  {
    group: 'Accounts & keys',
    items: [
      { id: CAPABILITIES.accountsRead, label: 'View accounts', description: 'Browse accounts.' },
      { id: CAPABILITIES.accountsManage, label: 'Manage accounts', description: 'Create, edit and delete accounts and their permissions.' },
      { id: CAPABILITIES.apiKeysRead, label: 'View API keys', description: 'See the API keys attached to accounts.' },
      { id: CAPABILITIES.apiKeysManage, label: 'Manage API keys', description: 'Issue and revoke API keys.' },
    ],
  },
  {
    group: 'Oversight',
    items: [
      { id: CAPABILITIES.auditRead, label: 'View audit log', description: 'Read and export the audit trail.' },
      { id: CAPABILITIES.privacyRead, label: 'Export personal data', description: 'Run a data-subject access export.' },
      { id: CAPABILITIES.privacyManage, label: 'Erase / retention', description: 'Erase personal data and run retention.' },
      { id: CAPABILITIES.backupRead, label: 'Export backup', description: 'Download a full backup.' },
      { id: CAPABILITIES.backupManage, label: 'Restore backup', description: 'Restore the registry from a backup.' },
    ],
  },
  {
    group: 'Notifications & integrations',
    items: [
      { id: CAPABILITIES.notificationsRead, label: 'View notifications', description: 'See email/notification config, templates and the outbox.' },
      { id: CAPABILITIES.notificationsManage, label: 'Manage notifications', description: 'Edit email/notification config and templates; send mail.' },
      { id: CAPABILITIES.webhooksRead, label: 'View webhooks', description: 'See webhook endpoints and deliveries.' },
      { id: CAPABILITIES.webhooksManage, label: 'Manage webhooks', description: 'Create/edit webhooks, rotate secrets, redeliver and drain.' },
    ],
  },
  {
    group: 'Settings',
    items: [
      { id: CAPABILITIES.settingsRead, label: 'View settings', description: 'Read global settings (e.g. the default approval policy).' },
      { id: CAPABILITIES.settingsManage, label: 'Manage settings', description: 'Change global settings (e.g. the default approval policy).' },
    ],
  },
]

/** Every capability id, in catalogue order. */
export const ALL_CAPABILITIES: Capability[] = CAPABILITY_GROUPS.flatMap(g => g.items.map(i => i.id))

const OPERATOR_DEFAULTS: Capability[] = [
  CAPABILITIES.schemasRead,
  CAPABILITIES.submissionsRead,
  CAPABILITIES.queryRead,
  CAPABILITIES.exploreRead,
  CAPABILITIES.statusRead,
  CAPABILITIES.reportsRead,
]

const APPROVER_DEFAULTS: Capability[] = [CAPABILITIES.submissionsRead, CAPABILITIES.submissionsApprove]

/**
 * The default capability bundle a role seeds, mirroring `RoleCapabilities.DefaultsFor` on the server.
 * Used to pre-fill the permissions picker when an account has no explicit overrides.
 */
export function defaultCapabilitiesForRole(role: AccountRole): Capability[] {
  switch (role) {
    case 'Admin':
      return [...ALL_CAPABILITIES]
    case 'Operator':
      return [...OPERATOR_DEFAULTS]
    case 'Approver':
      return [...APPROVER_DEFAULTS]
    default:
      return []
  }
}
