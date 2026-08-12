import type { AccountRole } from './types'
import type { TFunction } from 'i18next'
import i18n from '../i18n'

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
  integrationsRead: 'integrations:read',
  integrationsManage: 'integrations:manage',
  privacyRead: 'privacy:read',
  privacyManage: 'privacy:manage',
  backupRead: 'backup:read',
  backupManage: 'backup:manage',
  settingsRead: 'settings:read',
  settingsManage: 'settings:manage',
  eventsRead: 'events:read',
  eventsManage: 'events:manage',
  commentsRead: 'comments:read',
  commentsCreate: 'comments:create',
  commentsManage: 'comments:manage',
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
interface CapabilityGroupDefinition {
  key: string
  items: Array<{ id: Capability; key: string }>
}

const CAPABILITY_GROUP_DEFINITIONS: CapabilityGroupDefinition[] = [
  { key: 'schemas', items: [
    { id: CAPABILITIES.schemasRead, key: 'schemasRead' },
    { id: CAPABILITIES.schemasManage, key: 'schemasManage' },
  ] },
  { key: 'submissions', items: [
    { id: CAPABILITIES.submissionsRead, key: 'submissionsRead' },
    { id: CAPABILITIES.submissionsSubmit, key: 'submissionsSubmit' },
    { id: CAPABILITIES.submissionsDelete, key: 'submissionsDelete' },
    { id: CAPABILITIES.submissionsApprove, key: 'submissionsApprove' },
  ] },
  { key: 'analytics', items: [
    { id: CAPABILITIES.queryRead, key: 'queryRead' },
    { id: CAPABILITIES.exploreRead, key: 'exploreRead' },
    { id: CAPABILITIES.statusRead, key: 'statusRead' },
  ] },
  { key: 'reports', items: [
    { id: CAPABILITIES.reportsRead, key: 'reportsRead' },
    { id: CAPABILITIES.reportsManage, key: 'reportsManage' },
  ] },
  { key: 'accountsKeys', items: [
    { id: CAPABILITIES.accountsRead, key: 'accountsRead' },
    { id: CAPABILITIES.accountsManage, key: 'accountsManage' },
    { id: CAPABILITIES.apiKeysRead, key: 'apiKeysRead' },
    { id: CAPABILITIES.apiKeysManage, key: 'apiKeysManage' },
  ] },
  { key: 'oversight', items: [
    { id: CAPABILITIES.auditRead, key: 'auditRead' },
    { id: CAPABILITIES.privacyRead, key: 'privacyRead' },
    { id: CAPABILITIES.privacyManage, key: 'privacyManage' },
    { id: CAPABILITIES.backupRead, key: 'backupRead' },
    { id: CAPABILITIES.backupManage, key: 'backupManage' },
  ] },
  { key: 'notificationsIntegrations', items: [
    { id: CAPABILITIES.notificationsRead, key: 'notificationsRead' },
    { id: CAPABILITIES.notificationsManage, key: 'notificationsManage' },
    { id: CAPABILITIES.webhooksRead, key: 'webhooksRead' },
    { id: CAPABILITIES.webhooksManage, key: 'webhooksManage' },
    { id: CAPABILITIES.integrationsRead, key: 'integrationsRead' },
    { id: CAPABILITIES.integrationsManage, key: 'integrationsManage' },
  ] },
  { key: 'settings', items: [
    { id: CAPABILITIES.settingsRead, key: 'settingsRead' },
    { id: CAPABILITIES.settingsManage, key: 'settingsManage' },
  ] },
  { key: 'events', items: [
    { id: CAPABILITIES.eventsRead, key: 'eventsRead' },
    { id: CAPABILITIES.eventsManage, key: 'eventsManage' },
  ] },
  { key: 'comments', items: [
    { id: CAPABILITIES.commentsRead, key: 'commentsRead' },
    { id: CAPABILITIES.commentsCreate, key: 'commentsCreate' },
    { id: CAPABILITIES.commentsManage, key: 'commentsManage' },
  ] },
]

export function getCapabilityGroups(t: TFunction = i18n.t): CapabilityGroup[] {
  return CAPABILITY_GROUP_DEFINITIONS.map(group => ({
    group: t(`shell.capabilities.groups.${group.key}`),
    items: group.items.map(item => ({
      id: item.id,
      label: t(`shell.capabilities.items.${item.key}.label`),
      description: t(`shell.capabilities.items.${item.key}.description`),
    })),
  }))
}

/** @deprecated Prefer {@link getCapabilityGroups}; retained for cross-slice compatibility. */
export const CAPABILITY_GROUPS = new Proxy([] as CapabilityGroup[], {
  get: (_, property) => Reflect.get(getCapabilityGroups(), property),
})

/** Every capability id, in catalogue order. */
export const ALL_CAPABILITIES: Capability[] =
  CAPABILITY_GROUP_DEFINITIONS.flatMap(group => group.items.map(item => item.id))

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
