import { useEffect, useRef, useState } from 'react'
import {
  Badge, Body1, Button, Drawer, DrawerBody,
  Dropdown, Option, Field, Input, Textarea, Checkbox,
  Dialog, DialogSurface, DialogTitle, DialogBody, DialogContent, DialogActions, DialogTrigger,
  RadioGroup, Radio,
  Tab, TabList,
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, TableCellLayout,
  Title2, Tooltip, makeStyles, MessageBarBody, MessageBarTitle,
  Menu, MenuButton, MenuDivider, MenuItem, MenuList, MenuPopover, MenuTrigger, SplitButton,
  Toolbar, ToolbarButton, tokens,
} from '@fluentui/react-components'
import { Add20Regular, ArrowClockwise20Regular, ArrowDownload20Regular, ArrowRotateClockwise20Regular, ArrowUpload20Regular, Checkmark20Regular, Delete20Regular, Dismiss20Regular, Edit20Regular, Key20Regular, Mail20Regular, MoreHorizontal20Regular, PersonAdd20Regular, ShieldPerson20Regular, Status20Regular } from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { accountsBackupExportUrl, fetchAllAccounts, useAccounts, useApiKeys, useAuthProviders, useCapabilities, useCreateAccount, useDeleteAccount, useDeleteApiKey, useEraseAccount, useImportAccountsBackup, useRevokeApiKey, useRotateApiKey, useUpdateApiKey, useSendAdhocEmail, useUpdateAccount, personalDataExportUrl } from '../api/hooks'
import type { Account, AccountKind, AccountRole, ApiKey, AuthProvider, CreateAccountRequest, ErasureMode, ErasureResult, ExternalLogin, UpdateAccountRequest } from '../api/types'
import { getCapabilityGroups, defaultCapabilitiesForRole, type Capability } from '../api/capabilities'
import { downloadFromUrl, pickTextFile } from '../utils/download'
import { formatApiError, localizeDiagnostics } from '../api/client'
import { RowActions } from '../components/RowActions'
import { OnboardAccountWizard } from '../components/OnboardAccountWizard'
import { AccountAvatar } from '../components/Avatars'
import { DRAWER_EXPANDED_WIDTH, DrawerHeaderWithClose } from '../components/DrawerHeaderWithClose'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { LocalizedTime } from '../components/LocalizedTime'
import { useCsvExport, type ExportColumn } from '../utils/useCsvExport'
import { clickableRowProps } from '../utils/a11y'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  toolbarActions: { display: 'flex', alignItems: 'center', gap: '16px' },
  drawer: { width: 'max(600px, 50vw)' },
  drawerForm: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '16px' },
  editorTabPanel: { display: 'flex', flexDirection: 'column', gap: '12px', marginTop: '4px' },
  twoCol: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' },
  hint: { color: tokens.colorNeutralForeground3, fontSize: '12px', marginBottom: '4px' },
  linkRow: { display: 'grid', gridTemplateColumns: '160px 1fr auto', gap: '8px', alignItems: 'end', marginBottom: '8px' },
  sectionLabel: {
    color: tokens.colorNeutralForeground3,
    fontWeight: 600,
    fontSize: '12px',
    textTransform: 'uppercase',
    marginTop: '12px',
  },
  rotated: { fontFamily: 'monospace', backgroundColor: tokens.colorNeutralBackground3, padding: '12px', borderRadius: '4px', wordBreak: 'break-all' },
  // Keep the description column from pushing the others off-screen: cap it and ellipsise overflow.
  // maxWidth:0 is the table-cell "take leftover width then clip" trick (see nameCell above).
  keyDescCell: { maxWidth: 0, width: '40%' },
  keyDescInner: { display: 'flex', gap: '4px', alignItems: 'center', minWidth: 0 },
  keyDescText: { flexGrow: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  keyDescInput: { flexGrow: 1, minWidth: 0 },
  keyActions: { display: 'flex', gap: '4px', alignItems: 'center', justifyContent: 'flex-end' },
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  // max-width:0 is the classic "don't request width, take whatever's left and clip" trick for HTML
  // table cells — combined with the inner truncate class below, long labels/descriptions ellipsize
  // instead of pushing the other columns off-screen or wrapping onto two lines.
  nameCell: { maxWidth: 0 },
  truncate: {
    display: 'block',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  actionsHeader: { textAlign: 'right' },
  actionsCell:   { textAlign: 'right' },
  rowClickable: {
    cursor: 'pointer',
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '-2px' },
  },
  drawerToolbar: {
    width: '100%',
    boxSizing: 'border-box',
    padding: '0 16px',
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  capPicker: { display: 'flex', flexDirection: 'column', gap: '10px', marginTop: '4px' },
  capGroupLabel: { fontWeight: 600, fontSize: '12px', color: tokens.colorNeutralForeground2, textTransform: 'uppercase', letterSpacing: '0.02em' },
  capGrid: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '10px 16px' },
  capItem: { display: 'flex', flexDirection: 'column' },
  // Indented past the checkbox indicator so the description hangs under its own label.
  capDescription: {
    marginLeft: `calc(16px + ${tokens.spacingHorizontalS})`,
    color: tokens.colorNeutralForeground3,
    fontSize: '12px',
    lineHeight: tokens.lineHeightBase200,
  },
  capNote: { color: tokens.colorNeutralForeground3, fontSize: '12px' },
})

/** Compare two capability lists as sets (order-insensitive). */
function sameCapabilitySet(a: readonly Capability[], b: readonly Capability[]): boolean {
  if (a.length !== b.length) return false
  const set = new Set(a)
  return b.every(c => set.has(c))
}

/**
 * Grouped checkbox picker for an account's effective capabilities. The role dropdown seeds the
 * selection; this lets the admin add or remove individual capabilities on top of (or instead of)
 * the role default. Two roles are non-editable: Admins always hold every capability, and Service
 * accounts hold none (they only submit/view their own data) — both are shown read-only.
 *
 * Note: each Checkbox carries an explicit, unique `id`. Without it the surrounding Field injects a
 * single generated id into every descendant control via context, so all the labels' `htmlFor`
 * collide and clicking one label toggles an unrelated checkbox.
 */
function CapabilityPicker({
  role,
  selected,
  onChange,
}: {
  role: AccountRole
  selected: Capability[]
  onChange: (next: Capability[]) => void
}) {
  const s = useStyles()
  const { t } = useTranslation()
  const isAdmin = role === 'Admin'
  const isService = role === 'Service'
  const readOnly = isAdmin || isService
  const set = new Set(selected)

  function toggle(cap: Capability, checked: boolean) {
    const next = new Set(set)
    if (checked) next.add(cap)
    else next.delete(cap)
    onChange(Array.from(next))
  }

  return (
    <Field label={t('accounts.permissions.title')} hint={t('accounts.permissions.hint')}>
      <div className={s.capPicker}>
        {isAdmin && (
          <Body1 className={s.capNote}>{t('accounts.permissions.adminNote')}</Body1>
        )}
        {isService && (
          <Body1 className={s.capNote}>{t('accounts.permissions.serviceNote')}</Body1>
        )}
        {getCapabilityGroups(t).map(group => (
          <div key={group.group}>
            <div className={s.capGroupLabel}>{group.group}</div>
            <div className={s.capGrid}>
              {group.items.map(item => (
                <div key={item.id} className={s.capItem}>
                  <Checkbox
                    id={`cap-${item.id}`}
                    label={item.label}
                    input={{ 'aria-describedby': `cap-${item.id}-description` }}
                    checked={isAdmin ? true : isService ? false : set.has(item.id)}
                    disabled={readOnly}
                    onChange={(_, d) => toggle(item.id, !!d.checked)}
                  />
                  <span id={`cap-${item.id}-description`} className={s.capDescription}>
                    {item.description}
                  </span>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </Field>
  )
}

/**
 * Multi-select for an account's per-service scope (its assigned-service allowlist). An empty
 * selection means "unrestricted" — the account sees every service; a non-empty selection confines
 * every cross-service read to the chosen services. Only meaningful for back-office roles: Admins are
 * always unrestricted and Service accounts only ever see their own data, so the caller hides it for
 * those roles. The picker is driven off the live Service-account roster.
 */
function AssignedServicesPicker({
  services,
  selected,
  onChange,
}: {
  services: Account[]
  selected: string[]
  onChange: (next: string[]) => void
}) {
  const { t } = useTranslation()
  const set = new Set(selected)
  // Only offer enabled services; keep any already-assigned id even if it's since been disabled so
  // the existing scope isn't silently dropped on save.
  const options = services.filter(a => a.enabled || set.has(a.id))
  const summary = selected.length === 0
    ? t('accounts.scope.allServices')
    : options.filter(a => set.has(a.id)).map(a => a.label || a.name).join(', ')

  return (
    <Field
      label={t('accounts.scope.title')}
      hint={t('accounts.scope.hint')}
    >
      <Dropdown
        multiselect
        placeholder={t('accounts.scope.allServices')}
        selectedOptions={selected}
        value={summary}
        onOptionSelect={(_, d) => onChange(d.selectedOptions)}
      >
        {options.map(a => (
          <Option key={a.id} value={a.id}>{a.label || a.name}</Option>
        ))}
      </Dropdown>
    </Field>
  )
}

const roles: AccountRole[] = ['Service', 'Operator', 'Approver', 'Admin']
const kinds: AccountKind[] = ['User', 'Application']

/** Tabs in the account editor drawer. */
type EditorTab = 'general' | 'permissions' | 'scope'

/** Per-service scope is only meaningful for back-office roles (not Admin — always unrestricted — nor a Service, which only ever sees its own data). */
function roleSupportsScope(role: AccountRole): boolean {
  return role !== 'Admin' && role !== 'Service'
}

// Drop blank rows and trim emails before sending. The server lower-cases and de-duplicates, so we
// only need to filter out half-filled rows here.
function cleanLogins(links?: ExternalLogin[]): ExternalLogin[] {
  return (links ?? [])
    .map(l => ({ provider: l.provider, email: (l.email ?? '').trim() }))
    .filter(l => l.provider && l.email)
}

function roleLabel(t: TFunction, role: AccountRole): string {
  return t(`accounts.roles.${role}`)
}

function kindLabel(t: TFunction, kind: AccountKind): string {
  return t(`accounts.kinds.${kind}`)
}

function accountExportColumns(t: TFunction): ExportColumn<Account>[] {
  return [
    { header: t('accounts.fields.name'), value: a => a.name },
    { header: t('accounts.fields.label'), value: a => a.label ?? '' },
    { header: t('accounts.fields.kind'), value: a => kindLabel(t, a.kind) },
    { header: t('accounts.fields.role'), value: a => roleLabel(t, a.role) },
    { header: t('accounts.fields.status'), value: a => t(a.enabled ? 'accounts.status.enabled' : 'accounts.status.disabled') },
    { header: t('accounts.fields.email'), value: a => a.email ?? '' },
    { header: t('accounts.fields.area'), value: a => a.area ?? '' },
    { header: t('accounts.fields.created'), value: a => a.createdAt },
    { header: t('accounts.fields.createdBy'), value: a => a.createdBy ?? '' },
  ]
}

export function ServicesPage() {
  const s = useStyles()
  const { t } = useTranslation()
  const nav = useNavigate()
  const [sp, setSp] = useSearchParams()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const { data, isLoading, error, refetch } = useAccounts({ page, pageSize })
  // Service-account roster that backs the per-account scope picker. Cheap and cached; only the
  // editor drawer actually reads it.
  const { data: serviceAccounts } = useAccounts({ role: 'Service' })
  const { data: providers } = useAuthProviders()
  const { me, has } = useCapabilities()
  const hasSso = (providers?.length ?? 0) > 0
  const emailEnabled = me?.emailEnabled === true
  const canManageAccounts = has('accounts:manage')
  const canManageKeys = has('apikeys:manage')
  const canErase = has('privacy:manage')
  const canExportPersonal = has('privacy:read')
  const create = useCreateAccount()
  const update = useUpdateAccount()
  const del = useDeleteAccount()

  const [editing, setEditing] = useState<{ kind: 'create' | 'edit'; account: Partial<Account> } | null>(null)
  const [viewing, setViewing] = useState<Account | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [keyDialogFor, setKeyDialogFor] = useState<Account | null>(null)
  const [emailDialogFor, setEmailDialogFor] = useState<Account | null>(null)
  const [eraseDialogFor, setEraseDialogFor] = useState<Account | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [rotatedPlaintext, setRotatedPlaintext] = useState<string | null>(null)
  // Which "onboard new …" wizard is open (role drives the wizard config), or null when none.
  const [onboarding, setOnboarding] = useState<AccountRole | null>(null)
  const accountsExport = useCsvExport({
    filename: 'accounts.csv',
    columns: accountExportColumns(t),
    fetchAll: () => fetchAllAccounts(),
    onError: setActionError,
  })
  const importAccounts = useImportAccountsBackup()
  const [actionInfo, setActionInfo] = useState<string | null>(null)

  async function exportAccountsJson() {
    setActionError(null); setActionInfo(null)
    const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')
    try {
      await downloadFromUrl(accountsBackupExportUrl(), `ingest-accounts-${stamp}.json`)
    } catch (e) {
      setActionError(formatApiError(e))
    }
  }

  async function importAccountsJson() {
    setActionError(null); setActionInfo(null)
    let parsed: unknown
    try {
      const { content } = await pickTextFile('.json,application/json')
      parsed = JSON.parse(content)
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e)
      if (!/no file selected/i.test(msg)) setActionError(t('accounts.import.readError', { error: msg }))
      return
    }
    const ok = window.confirm(t('accounts.import.confirm'))
    if (!ok) return
    try {
      const res = await importAccounts.mutateAsync(parsed)
      const errors = localizeDiagnostics(res.errorDetails, res.errors)
      const tail = errors.length > 0
        ? t('accounts.import.skipped', { count: errors.length, errors: errors.join('; ') })
        : ''
      setActionInfo(t('accounts.import.complete', { created: res.created, updated: res.updated, tail }))
    } catch (e) {
      setActionError(formatApiError(e))
    }
  }
  // Per-drawer "expanded" state so the edit and view drawers can be enlarged independently
  // (e.g. expand the view to read a long description without losing the editor state).
  const [editorExpanded, setEditorExpanded] = useState(false)
  const [viewerExpanded, setViewerExpanded] = useState(false)
  const [editorTab, setEditorTab] = useState<EditorTab>('general')

  function openCreate() {
    setEditing({ kind: 'create', account: { name: '', label: '', description: '', email: '', area: '', kind: 'Application', role: 'Service', enabled: true, capabilities: defaultCapabilitiesForRole('Service'), assignedServiceIds: [] } })
    setSubmitError(null)
    setEditorTab('general')
  }

  // Deep link: /services?new=1 opens the create drawer immediately (used by the global search
  // "Add user / Add service" actions). Guarded so it fires once, after capabilities are known; the
  // param is stripped afterwards so a refresh/back is clean.
  const openedFromUrl = useRef(false)
  useEffect(() => {
    if (openedFromUrl.current) return
    if (sp.get('new') === null || !canManageAccounts) return
    openedFromUrl.current = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- one-shot deep-link open, gated on async capabilities
    openCreate()
    const next = new URLSearchParams(sp)
    next.delete('new')
    setSp(next, { replace: true })
  }, [sp, canManageAccounts, setSp])
  function openEdit(a: Account) {
    // The picker is driven off the *effective* set; on save we collapse it back to either the
    // role-default bundle (stored as no overrides) or an explicit override list.
    setEditing({ kind: 'edit', account: { ...a, capabilities: a.effectiveCapabilities ?? defaultCapabilitiesForRole(a.role ?? 'Service'), assignedServiceIds: a.assignedServiceIds ?? [] } })
    setSubmitError(null)
    setEditorTab('general')
  }
  function editFromView(a: Account) { setViewing(null); openEdit(a) }
  function keysFromView(a: Account) { setViewing(null); setKeyDialogFor(a) }
  function emailFromView(a: Account) { setViewing(null); setEmailDialogFor(a) }
  function statusFromView(a: Account) { setViewing(null); nav(`/services/${encodeURIComponent(a.name)}/status`) }
  function deleteFromView(a: Account) {
    if (!window.confirm(t('accounts.confirmDelete', { target: a.label || a.name }))) return
    setViewing(null)
    del.mutate(a.id)
  }
  function deleteFromRow(a: Account) {
    if (!window.confirm(t('accounts.confirmDelete', { target: a.label || a.name }))) return
    del.mutate(a.id)
  }
  function eraseFromView(a: Account) { setViewing(null); setEraseDialogFor(a) }
  async function exportPersonalData(a: Account) {
    setActionError(null)
    const stamp = new Date().toISOString().slice(0, 10)
    try {
      await downloadFromUrl(personalDataExportUrl(a.id), `personal-data-${a.name}-${stamp}.json`)
    } catch (e) {
      setActionError(formatApiError(e))
    }
  }

  async function onSave() {
    if (!editing) return
    setSubmitError(null)
    const a = editing.account
    const email = (a.email ?? '').trim()
    // Informative-only tag: blank normalises to null (clears it). Never validated here.
    const area = (a.area ?? '').trim() || null
    // Email is required for new accounts; existing accounts may keep an empty email (legacy data),
    // so editing doesn't force one. A non-empty value must still look like an address.
    if (editing.kind === 'create' && !email) { setEditorTab('general'); setSubmitError(t('accounts.validation.emailRequired')); return }
    if (email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) { setEditorTab('general'); setSubmitError(t('accounts.validation.emailInvalid')); return }
    // Only User-kind accounts can hold SSO links; never send them for Application accounts.
    const logins = a.kind === 'User' ? cleanLogins(a.externalLogins) : []
    const role = a.role ?? 'Service'
    // Collapse the picker selection: Admins implicitly hold everything (send []), and a selection
    // identical to the role default bundle is persisted as "no overrides" (so the account keeps
    // tracking the role defaults). Anything else is stored verbatim as an explicit override set.
    const desired = a.capabilities ?? []
    const capabilities: Capability[] =
      role === 'Admin' || sameCapabilitySet(desired, defaultCapabilitiesForRole(role)) ? [] : desired
    // Per-service scope only applies to back-office roles. Admins are always unrestricted and Service
    // accounts only ever see their own data, so we never persist a scope for them (send []).
    const scopeApplies = role !== 'Admin' && role !== 'Service'
    const assignedServiceIds: string[] = scopeApplies ? (a.assignedServiceIds ?? []) : []
    try {
      if (editing.kind === 'create') {
        const req: CreateAccountRequest = {
          name: a.name ?? '',
          label: a.label,
          description: a.description,
          email,
          area,
          kind: a.kind ?? 'Application',
          role,
          enabled: a.enabled ?? true,
          capabilities,
          assignedServiceIds,
          // Only include when SSO is on so an API-key-only deployment never touches this field.
          ...(hasSso ? { externalLogins: logins } : {}),
        }
        await create.mutateAsync(req)
      } else {
        const req: UpdateAccountRequest = {
          label: a.label,
          description: a.description,
          email,
          area,
          role,
          enabled: a.enabled ?? true,
          capabilities,
          assignedServiceIds,
          // Omit entirely when SSO is off (undefined ⇒ "leave links untouched" server-side).
          ...(hasSso ? { externalLogins: logins } : {}),
        }
        await update.mutateAsync({ id: a.id!, req })
      }
      setEditing(null)
    } catch (e) {
      setSubmitError(formatApiError(e))
    }
  }

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <Title2>{t('accounts.title')}</Title2>
        <Toolbar className={s.toolbarActions}>
          {canManageAccounts && <ToolbarButton appearance="primary" icon={<Add20Regular />} onClick={openCreate}>{t('accounts.actions.new')}</ToolbarButton>}
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label={t('accounts.actions.more')} />
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {canManageAccounts && <MenuItem icon={<PersonAdd20Regular />} onClick={() => setOnboarding('Service')}>{t('accounts.actions.onboardService')}</MenuItem>}
                {canManageAccounts && <MenuItem icon={<PersonAdd20Regular />} onClick={() => setOnboarding('Operator')}>{t('accounts.actions.onboardOperator')}</MenuItem>}
                {canManageAccounts && <MenuDivider />}
                <MenuItem icon={<ArrowClockwise20Regular />} onClick={() => refetch()}>{t('accounts.actions.refresh')}</MenuItem>
                <MenuDivider />
                <MenuItem
                  icon={<ArrowDownload20Regular />}
                  disabled={accountsExport.exporting}
                  onClick={accountsExport.exportList}
                >
                  {t(accountsExport.exporting ? 'accounts.actions.exporting' : 'accounts.actions.exportCsv')}
                </MenuItem>
                <MenuDivider />
                <MenuItem icon={<ArrowDownload20Regular />} onClick={exportAccountsJson}>
                  {t('accounts.actions.exportJson')}
                </MenuItem>
                {canManageAccounts && (
                  <MenuItem
                    icon={<ArrowUpload20Regular />}
                    disabled={importAccounts.isPending}
                    onClick={importAccountsJson}
                  >
                    {t(importAccounts.isPending ? 'accounts.actions.importing' : 'accounts.actions.importJson')}
                  </MenuItem>
                )}
              </MenuList>
            </MenuPopover>
          </Menu>
        </Toolbar>
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>{t('accounts.messages.loadFailed')}</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {actionError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>{t('accounts.messages.actionFailed')}</MessageBarTitle>
            {actionError}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {actionInfo && (
        <AutoScrollMessageBar intent="success">
          <MessageBarBody>{actionInfo}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Table size="small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>{t('accounts.fields.name')}</TableHeaderCell>
            <TableHeaderCell>{t('accounts.fields.kind')}</TableHeaderCell>
            <TableHeaderCell>{t('accounts.fields.role')}</TableHeaderCell>
            <TableHeaderCell>{t('accounts.fields.status')}</TableHeaderCell>
            <TableHeaderCell>{t('accounts.fields.created')}</TableHeaderCell>
            <TableHeaderCell>{t('accounts.fields.createdBy')}</TableHeaderCell>
            <TableHeaderCell className={s.actionsHeader}>{t('accounts.fields.actions')}</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={7}>{t('accounts.messages.loading')}</GridMessageRow>}
          {!isLoading && (data?.items ?? []).length === 0 && (
            <GridMessageRow colSpan={7}>{t('accounts.messages.empty')}</GridMessageRow>
          )}
          {(data?.items ?? []).map(a => (
            <TableRow
              key={a.id}
              className={`${s.row} ${s.rowClickable}`}
              {...clickableRowProps(() => setViewing(a), t('accounts.aria.viewAccount', { name: a.label || a.name }))}
            >
              <TableCell className={s.nameCell}>
                <TableCellLayout media={<AccountAvatar account={a} />} description={a.description ?? ''}>
                  <Tooltip content={a.label || a.name} relationship="label">
                    <strong className={s.truncate}>{a.label || a.name}</strong>
                  </Tooltip>
                </TableCellLayout>
              </TableCell>
              <TableCell>{kindLabel(t, a.kind)}</TableCell>
              <TableCell>{roleLabel(t, a.role)}</TableCell>
              <TableCell>
                <Badge appearance="outline" color={a.enabled ? 'success' : 'danger'}>
                  {t(a.enabled ? 'accounts.status.enabled' : 'accounts.status.disabled')}
                </Badge>
              </TableCell>
              <TableCell>
                <Tooltip content={<LocalizedTime value={a.createdAt} />} relationship="label">
                  <LocalizedTime value={a.createdAt} dateOnly />
                </Tooltip>
              </TableCell>
              <TableCell>{a.createdBy || '—'}</TableCell>
              <TableCell className={s.actionsCell} onClick={e => e.stopPropagation()}>
                <RowActions
                  ariaLabel={t('accounts.aria.actionsFor', { name: a.name })}
                  actions={[
                    ...(canManageAccounts ? [{ key: 'edit', label: t('accounts.actions.edit'), icon: <Edit20Regular />, onClick: () => openEdit(a) }] : []),
                    ...(canManageKeys ? [{ key: 'keys', label: t('accounts.actions.apiKeys'), icon: <Key20Regular />, onClick: () => setKeyDialogFor(a) }] : []),
                    ...(emailEnabled && a.email
                      ? [{ key: 'email', label: t('accounts.actions.sendEmail'), icon: <Mail20Regular />, onClick: () => setEmailDialogFor(a) }]
                      : []),
                    ...(a.role === 'Service'
                      ? [{
                          key: 'status',
                          label: t('accounts.actions.viewStatus'),
                          icon: <Status20Regular />,
                          onClick: () => nav(`/services/${encodeURIComponent(a.name)}/status`),
                        }]
                      : []),
                    ...(canExportPersonal ? [{ key: 'export', label: t('accounts.actions.exportPersonalData'), icon: <ArrowDownload20Regular />, onClick: () => exportPersonalData(a) }] : []),
                    ...(canErase ? [{ key: 'erase', label: t('accounts.actions.erase'), icon: <ShieldPerson20Regular />, destructive: true, onClick: () => setEraseDialogFor(a) }] : []),
                    ...(canManageAccounts ? [{ key: 'delete', label: t('accounts.actions.delete'), icon: <Delete20Regular />, destructive: true, onClick: () => deleteFromRow(a) }] : []),
                  ]}
                />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <GridPager
        page={page}
        pageSize={pageSize}
        total={data?.total ?? 0}
        onPageChange={setPage}
        onPageSizeChange={(n) => { setPageSize(n); setPage(1) }}
      />

      <Drawer
        type="overlay"
        separator
        open={!!editing}
        onOpenChange={(_, d) => { if (!d.open) { setEditing(null); setEditorExpanded(false) } }}
        position="end"
        className={s.drawer}
        style={editorExpanded ? { width: DRAWER_EXPANDED_WIDTH } : undefined}
      >
        <DrawerHeaderWithClose
          title={t(editing?.kind === 'create' ? 'accounts.editor.newTitle' : 'accounts.editor.editTitle')}
          onClose={() => { setEditing(null); setEditorExpanded(false) }}
          expanded={editorExpanded}
          onToggleExpand={() => setEditorExpanded(e => !e)}
        />
        <DrawerBody>
          {editing && (() => {
            const role = editing.account.role ?? 'Service'
            const scopeApplies = roleSupportsScope(role)
            // Area is informative-only and always optional. A configured list turns it into a
            // dropdown; an empty list falls back to free text. Keep an out-of-list current value
            // (the configured list may have changed since the account was tagged).
            const configuredAreas = me?.areas ?? []
            const currentArea = editing.account.area ?? ''
            const areaOptions = currentArea && !configuredAreas.includes(currentArea)
              ? [...configuredAreas, currentArea]
              : configuredAreas
            // Guard against the scope tab lingering after a role change makes it irrelevant.
            const activeTab: EditorTab = editorTab === 'scope' && !scopeApplies ? 'permissions' : editorTab
            return (
            <div className={s.drawerForm}>
              <TabList selectedValue={activeTab} onTabSelect={(_, d) => setEditorTab(d.value as EditorTab)}>
                <Tab value="general">{t('accounts.editor.tabs.general')}</Tab>
                <Tab value="permissions">{t('accounts.editor.tabs.permissions')}</Tab>
                {scopeApplies && <Tab value="scope">{t('accounts.scope.title')}</Tab>}
              </TabList>

              {activeTab === 'general' && (
                <div className={s.editorTabPanel}>
                  <Field label={t('accounts.fields.name')} required>
                    <Input
                      value={editing.account.name ?? ''}
                      disabled={editing.kind === 'edit'}
                      onChange={(_, v) => setEditing({ ...editing, account: { ...editing.account, name: v.value } })}
                    />
                  </Field>
                  <Field label={t('accounts.fields.label')}>
                    <Input value={editing.account.label ?? ''} onChange={(_, v) => setEditing({ ...editing, account: { ...editing.account, label: v.value } })} />
                  </Field>
                  <Field label={t('accounts.fields.description')}>
                    <Textarea value={editing.account.description ?? ''} onChange={(_, v) => setEditing({ ...editing, account: { ...editing.account, description: v.value } })} />
                  </Field>
                  <Field label={t('accounts.fields.email')} required={editing.kind === 'create'} hint={t('accounts.editor.emailHint')}>
                    <Input
                      type="email"
                      value={editing.account.email ?? ''}
                      placeholder={t('accounts.placeholders.email')}
                      onChange={(_, v) => setEditing({ ...editing, account: { ...editing.account, email: v.value } })}
                    />
                  </Field>
                  <Field label={t('accounts.fields.area')} hint={t('accounts.editor.areaHint')}>
                    {areaOptions.length > 0 ? (
                      <Dropdown
                        selectedOptions={[currentArea]}
                        value={currentArea || t('accounts.editor.none')}
                        onOptionSelect={(_, d) => setEditing({ ...editing, account: { ...editing.account, area: d.optionValue ?? '' } })}
                      >
                        <Option value="">{t('accounts.editor.none')}</Option>
                        {areaOptions.map(ar => <Option key={ar} value={ar}>{ar}</Option>)}
                      </Dropdown>
                    ) : (
                      <Input
                        value={editing.account.area ?? ''}
                        placeholder={t('accounts.placeholders.area')}
                        onChange={(_, v) => setEditing({ ...editing, account: { ...editing.account, area: v.value } })}
                      />
                    )}
                  </Field>
                  <Field label={t('accounts.fields.kind')} hint={t(`accounts.kindHints.${(editing.account.kind ?? 'Application') as AccountKind}`)}>
                    <Dropdown
                      selectedOptions={[editing.account.kind ?? 'Application']}
                      value={kindLabel(t, editing.account.kind ?? 'Application')}
                      disabled={editing.kind === 'edit'}
                      onOptionSelect={(_, d) => setEditing({ ...editing, account: { ...editing.account, kind: d.optionValue as AccountKind } })}
                    >
                      {kinds.map(k => <Option key={k} value={k}>{kindLabel(t, k)}</Option>)}
                    </Dropdown>
                  </Field>
                  <Checkbox label={t('accounts.status.enabled')} checked={editing.account.enabled ?? true} onChange={(_, d) => setEditing({ ...editing, account: { ...editing.account, enabled: !!d.checked } })} />

                  {hasSso && (editing.account.kind ?? 'Application') === 'User' && (
                    <ExternalLoginsEditor
                      providers={providers ?? []}
                      links={editing.account.externalLogins ?? []}
                      onChange={(next) => setEditing({ ...editing, account: { ...editing.account, externalLogins: next } })}
                    />
                  )}
                </div>
              )}

              {activeTab === 'permissions' && (
                <div className={s.editorTabPanel}>
                  <Field label={t('accounts.fields.role')} hint={t('accounts.editor.roleHint')}>
                    <Dropdown
                      selectedOptions={[role]}
                      value={roleLabel(t, role)}
                      onOptionSelect={(_, d) => {
                        const nextRole = d.optionValue as AccountRole
                        // Re-seed the permissions picker from the new role's default bundle.
                        setEditing({ ...editing, account: { ...editing.account, role: nextRole, capabilities: defaultCapabilitiesForRole(nextRole) } })
                      }}
                    >
                      {roles.map(r => <Option key={r} value={r}>{roleLabel(t, r)}</Option>)}
                    </Dropdown>
                  </Field>

                  <CapabilityPicker
                    role={role}
                    selected={editing.account.capabilities ?? []}
                    onChange={(next) => setEditing({ ...editing, account: { ...editing.account, capabilities: next } })}
                  />
                </div>
              )}

              {activeTab === 'scope' && scopeApplies && (
                <div className={s.editorTabPanel}>
                  <AssignedServicesPicker
                    services={serviceAccounts?.items ?? []}
                    selected={editing.account.assignedServiceIds ?? []}
                    onChange={(next) => setEditing({ ...editing, account: { ...editing.account, assignedServiceIds: next } })}
                  />
                </div>
              )}

              {submitError && (
                <AutoScrollMessageBar intent="error">
                  <MessageBarBody>{submitError}</MessageBarBody>
                </AutoScrollMessageBar>
              )}

              <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', marginTop: 8 }}>
                <Button onClick={() => setEditing(null)}>{t('accounts.actions.cancel')}</Button>
                <Button appearance="primary" onClick={onSave}>{t('accounts.actions.save')}</Button>
              </div>
            </div>
            )
          })()}
        </DrawerBody>
      </Drawer>

      <Drawer
        type="overlay"
        separator
        open={!!viewing}
        onOpenChange={(_, d) => { if (!d.open) { setViewing(null); setViewerExpanded(false) } }}
        position="end"
        className={s.drawer}
        style={viewerExpanded ? { width: DRAWER_EXPANDED_WIDTH } : undefined}
      >
        <DrawerHeaderWithClose
          title={viewing ? (viewing.label || viewing.name) : t('accounts.singular')}
          onClose={() => { setViewing(null); setViewerExpanded(false) }}
          expanded={viewerExpanded}
          onToggleExpand={() => setViewerExpanded(e => !e)}
        />
        {viewing && (
          <Toolbar className={s.drawerToolbar}>
            {canManageAccounts && <ToolbarButton icon={<Edit20Regular />} onClick={() => editFromView(viewing)}>{t('accounts.actions.edit')}</ToolbarButton>}
            {canManageKeys && <ToolbarButton icon={<Key20Regular />} onClick={() => keysFromView(viewing)}>{t('accounts.actions.apiKeys')}</ToolbarButton>}
            {emailEnabled && viewing.email && (
              <ToolbarButton icon={<Mail20Regular />} onClick={() => emailFromView(viewing)}>{t('accounts.actions.sendEmail')}</ToolbarButton>
            )}
            {viewing.role === 'Service' && (
              <ToolbarButton icon={<Status20Regular />} onClick={() => statusFromView(viewing)}>{t('accounts.actions.viewStatus')}</ToolbarButton>
            )}
            {canExportPersonal && <ToolbarButton icon={<ArrowDownload20Regular />} onClick={() => exportPersonalData(viewing)}>{t('accounts.actions.exportData')}</ToolbarButton>}
            {canManageAccounts ? (
              // Default action is Delete; the chevron exposes the heavier GDPR erase as a subitem.
              <Menu positioning="below-end">
                <MenuTrigger disableButtonEnhancement>
                  {(triggerProps) => (
                    <SplitButton
                      menuButton={triggerProps}
                      primaryActionButton={{ onClick: () => deleteFromView(viewing) }}
                      appearance="subtle"
                      icon={<Delete20Regular />}
                    >
                      {t('accounts.actions.delete')}
                    </SplitButton>
                  )}
                </MenuTrigger>
                <MenuPopover>
                  <MenuList>
                    <MenuItem icon={<Delete20Regular />} onClick={() => deleteFromView(viewing)}>
                      {t('accounts.actions.delete')}
                    </MenuItem>
                    {canErase && (
                      <MenuItem
                        icon={<ShieldPerson20Regular />}
                        onClick={() => eraseFromView(viewing)}
                        style={{ color: 'var(--colorPaletteRedForeground1)' }}
                      >
                        {t('accounts.actions.erase')}
                      </MenuItem>
                    )}
                  </MenuList>
                </MenuPopover>
              </Menu>
            ) : canErase ? (
              <ToolbarButton icon={<ShieldPerson20Regular />} onClick={() => eraseFromView(viewing)}>{t('accounts.actions.erase')}</ToolbarButton>
            ) : null}
          </Toolbar>
        )}
        <DrawerBody>
          {viewing && <AccountViewBody account={viewing} services={serviceAccounts?.items ?? []} />}
        </DrawerBody>
      </Drawer>

      <KeysDialog account={keyDialogFor} onClose={() => { setKeyDialogFor(null); setRotatedPlaintext(null) }} rotated={rotatedPlaintext} onRotated={setRotatedPlaintext} />

      <SendEmailDialog account={emailDialogFor} onClose={() => setEmailDialogFor(null)} />

      <EraseDialog account={eraseDialogFor} onClose={() => setEraseDialogFor(null)} />

      {onboarding && (
        <OnboardAccountWizard
          open
          onClose={() => setOnboarding(null)}
          role={onboarding}
          title={t(onboarding === 'Operator' ? 'accounts.onboarding.operatorTitle' : 'accounts.onboarding.serviceTitle')}
          // Operators are always interactive User accounts, so lock the Kind for that flow.
          defaultKind={onboarding === 'Operator' ? 'User' : 'Application'}
          lockKind={onboarding === 'Operator'}
        />
      )}
    </div>
  )
}

// GDPR right-to-erasure. The admin picks anonymise (keep statistical KPI values, strip identity) or
// full delete (remove everything). The destructive action is gated behind an explicit acknowledgement
// because both modes are irreversible and bypass the ordinary "account has data" delete guard.
function EraseDialog({ account, onClose }: { account: Account | null; onClose: () => void }) {
  const { t } = useTranslation()
  const erase = useEraseAccount()
  const [mode, setMode] = useState<ErasureMode>('Anonymise')
  const [ack, setAck] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ErasureResult | null>(null)

  async function onErase() {
    if (!account) return
    setError(null)
    try {
      const r = await erase.mutateAsync({ id: account.id, mode })
      setResult(r)
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  function handleClose() {
    setMode('Anonymise'); setAck(false); setError(null); setResult(null)
    onClose()
  }

  return (
    <Dialog open={!!account} onOpenChange={(_, d) => !d.open && handleClose()}>
      <DialogSurface style={{ minWidth: 560 }}>
        <DialogBody>
          <DialogTitle>{t('accounts.erase.title', { name: account?.label || account?.name })}</DialogTitle>
          <DialogContent>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}
              {result ? (
                <AutoScrollMessageBar intent="success">
                  <MessageBarBody>
                    <MessageBarTitle>
                      {t(result.mode === 'Delete' ? 'accounts.erase.deleted' : 'accounts.erase.anonymised')}
                    </MessageBarTitle>
                    {t('accounts.erase.resultPrefix')} <code>{result.pseudonym}</code> ·{' '}
                    {t('accounts.erase.resultCounts', {
                      submissions: result.submissionsAffected,
                      samples: result.samplesAffected,
                      emails: result.emailsRemoved,
                      auditEntries: result.auditEntriesAffected,
                      keys: result.apiKeysRemoved,
                    })}
                  </MessageBarBody>
                </AutoScrollMessageBar>
              ) : (
                <>
                  <Body1>
                    {t('accounts.erase.warningBefore')} <strong>{t('accounts.erase.irreversible')}</strong>{' '}
                    {t('accounts.erase.warningAfter')}
                  </Body1>
                  <Field label={t('accounts.erase.mode')}>
                    <RadioGroup value={mode} onChange={(_, d) => setMode(d.value as ErasureMode)}>
                      <Radio value="Anonymise" label={t('accounts.erase.anonymiseOption')} />
                      <Radio value="Delete" label={t('accounts.erase.deleteOption')} />
                    </RadioGroup>
                  </Field>
                  <Checkbox
                    label={t('accounts.erase.acknowledgement')}
                    checked={ack}
                    onChange={(_, d) => setAck(!!d.checked)}
                  />
                </>
              )}
            </div>
          </DialogContent>
          <DialogActions>
            {!result && (
              <Button
                appearance="primary"
                icon={<ShieldPerson20Regular />}
                disabled={!ack || erase.isPending}
                onClick={onErase}
              >
                {t(erase.isPending
                  ? 'accounts.erase.erasing'
                  : mode === 'Delete'
                    ? 'accounts.erase.deleteEverything'
                    : 'accounts.erase.anonymise')}
              </Button>
            )}
            <Button appearance="secondary" onClick={handleClose}>{t(result ? 'accounts.actions.close' : 'accounts.actions.cancel')}</Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

// Ad-hoc plain-text email to a single account. The message is queued into the outbox and delivered
// by the email sender like any other; success here just means "accepted into the queue".
function SendEmailDialog({ account, onClose }: { account: Account | null; onClose: () => void }) {
  const { t } = useTranslation()
  const send = useSendAdhocEmail()
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [sent, setSent] = useState(false)

  async function onSend() {
    if (!account) return
    setError(null)
    if (!subject.trim()) { setError(t('accounts.email.subjectRequired')); return }
    try {
      await send.mutateAsync({ accountId: account.id, subject: subject.trim(), body })
      setSent(true)
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  function handleClose() {
    setSubject(''); setBody(''); setError(null); setSent(false)
    onClose()
  }

  return (
    <Dialog open={!!account} onOpenChange={(_, d) => !d.open && handleClose()}>
      <DialogSurface style={{ minWidth: 560 }}>
        <DialogBody>
          <DialogTitle>{t('accounts.email.title', { name: account?.label || account?.name })}</DialogTitle>
          <DialogContent>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <Body1>{t('accounts.email.to', { email: account?.email })}</Body1>
              {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}
              {sent && <AutoScrollMessageBar intent="success"><MessageBarBody>{t('accounts.email.queued')}</MessageBarBody></AutoScrollMessageBar>}
              <Field label={t('accounts.email.subject')} required>
                <Input value={subject} onChange={(_, d) => setSubject(d.value)} disabled={sent} />
              </Field>
              <Field label={t('accounts.email.message')}>
                <Textarea value={body} onChange={(_, d) => setBody(d.value)} rows={8} resize="vertical" disabled={sent} />
              </Field>
            </div>
          </DialogContent>
          <DialogActions>
            {!sent && (
              <Button appearance="primary" icon={<Mail20Regular />} disabled={send.isPending} onClick={onSend}>
                {t(send.isPending ? 'accounts.email.sending' : 'accounts.email.send')}
              </Button>
            )}
            <Button appearance="secondary" onClick={handleClose}>{t(sent ? 'accounts.actions.close' : 'accounts.actions.cancel')}</Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

// Per-account SSO identity links editor. Only rendered for User-kind accounts when SSO is enabled.
// Each row pairs a provider (chosen from the configured set) with the user's verified email.
function ExternalLoginsEditor({
  providers, links, onChange,
}: {
  providers: AuthProvider[]
  links: ExternalLogin[]
  onChange: (next: ExternalLogin[]) => void
}) {
  const s = useStyles()
  const { t } = useTranslation()
  const defaultProvider = providers[0]?.id ?? ''

  function update(i: number, patch: Partial<ExternalLogin>) {
    onChange(links.map((l, idx) => (idx === i ? { ...l, ...patch } : l)))
  }
  function remove(i: number) {
    onChange(links.filter((_, idx) => idx !== i))
  }
  function add() {
    onChange([...links, { provider: defaultProvider, email: '' }])
  }

  return (
    <div>
      <div className={s.sectionLabel}>{t('accounts.sso.title')}</div>
      <div className={s.hint}>
        {t('accounts.sso.hint')}
      </div>
      {links.map((link, i) => (
        <div key={i} className={s.linkRow}>
          <Field label={i === 0 ? t('accounts.sso.provider') : undefined}>
            <Dropdown
              selectedOptions={[link.provider]}
              value={providers.find(p => p.id === link.provider)?.displayName ?? link.provider}
              onOptionSelect={(_, d) => update(i, { provider: d.optionValue as string })}
            >
              {providers.map(p => <Option key={p.id} value={p.id}>{p.displayName}</Option>)}
            </Dropdown>
          </Field>
          <Field label={i === 0 ? t('accounts.fields.email') : undefined}>
            <Input
              type="email"
              value={link.email}
              placeholder={t('accounts.placeholders.email')}
              onChange={(_, v) => update(i, { email: v.value })}
            />
          </Field>
          <Button appearance="subtle" icon={<Delete20Regular />} aria-label={t('accounts.sso.remove')} onClick={() => remove(i)} />
        </div>
      ))}
      <Button appearance="secondary" icon={<Add20Regular />} onClick={add}>{t('accounts.sso.add')}</Button>
    </div>
  )
}

function AccountViewBody({ account, services }: { account: Account; services: Account[] }) {
  const s = useStyles()
  const { t } = useTranslation()
  const links = account.externalLogins ?? []
  // Per-service scope is only meaningful for back-office roles; Admins/Services are always unrestricted.
  const scopeApplies = account.role !== 'Admin' && account.role !== 'Service'
  const scopeIds = account.assignedServiceIds ?? []
  const scopeNames = scopeIds.map(id => {
    const svc = services.find(a => a.id === id)
    return svc ? (svc.label || svc.name) : id
  })
  return (
    <div className={s.drawerForm}>
      <div className={s.twoCol}>
        <Field label={t('accounts.fields.name')}><Body1>{account.name}</Body1></Field>
        <Field label={t('accounts.fields.label')}><Body1>{account.label || '—'}</Body1></Field>
      </div>
      {account.description && <Field label={t('accounts.fields.description')}><Body1>{account.description}</Body1></Field>}

      <div className={s.twoCol}>
        <Field label={t('accounts.fields.email')}><Body1>{account.email || '—'}</Body1></Field>
        <Field label={t('accounts.fields.area')}><Body1>{account.area || '—'}</Body1></Field>
      </div>

      <div className={s.twoCol}>
        <Field label={t('accounts.fields.kind')}><Body1>{kindLabel(t, account.kind)}</Body1></Field>
        <Field label={t('accounts.fields.role')}><Body1>{roleLabel(t, account.role)}</Body1></Field>
      </div>
      <Field label={t('accounts.status.enabled')}><Body1>{t(account.enabled ? 'accounts.common.yes' : 'accounts.common.no')}</Body1></Field>

      {scopeApplies && (
        <Field label={t('accounts.scope.title')} hint={t('accounts.scope.viewHint')}>
          {scopeIds.length === 0 ? (
            <Body1>{t('accounts.scope.unrestricted')}</Body1>
          ) : (
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
              {scopeNames.map((n, i) => (
                <Badge key={scopeIds[i]} appearance="outline">{n}</Badge>
              ))}
            </div>
          )}
        </Field>
      )}

      {links.length > 0 && (
        <Field label={t('accounts.sso.title')}>
          <div>
            {links.map((l, i) => (
              <Body1 key={i} block>{l.provider}: {l.email}</Body1>
            ))}
          </div>
        </Field>
      )}

      {/* Deleted is the only state not already spelled out as a field, so surface it as a badge
          when (and only when) it applies. */}
      {account.isDeleted && (
        <div>
          <Badge appearance="outline" color="danger">{t('accounts.status.deleted')}</Badge>
        </div>
      )}

      <div className={s.sectionLabel}>{t('accounts.audit.title')}</div>
      <div className={s.twoCol}>
        <Field label={t('accounts.fields.created')}>
          <Body1>
            <LocalizedTime value={account.createdAt} />
            {account.createdBy ? t('accounts.audit.by', { name: account.createdBy }) : ''}
          </Body1>
        </Field>
        <Field label={t('accounts.fields.modified')}>
          <Body1>
            <LocalizedTime value={account.modifiedAt} />
            {account.modifiedBy ? t('accounts.audit.by', { name: account.modifiedBy }) : ''}
          </Body1>
        </Field>
      </div>
    </div>
  )
}

/** A date (yyyy-mm-dd) offset from today by the given number of years, for the expiry input bounds. */
function dateInputOffset(years: number, days = 0): string {
  const d = new Date()
  d.setFullYear(d.getFullYear() + years)
  d.setDate(d.getDate() + days)
  return d.toISOString().slice(0, 10)
}

function KeysDialog({ account, onClose, rotated, onRotated }: { account: Account | null; onClose: () => void; rotated: string | null; onRotated: (v: string) => void }) {
  const s = useStyles()
  const { t } = useTranslation()
  const keys = useApiKeys(account?.id)
  const rotate = useRotateApiKey()
  const revoke = useRevokeApiKey()
  const update = useUpdateApiKey()
  const del = useDeleteApiKey()
  const [expiry, setExpiry] = useState('')
  const [description, setDescription] = useState('')
  // Which key's description is being edited inline, and its working text.
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editText, setEditText] = useState('')

  // The server caps a new key's lifetime at two years; mirror that on the input.
  const minExpiry = dateInputOffset(0, 1)
  const maxExpiry = dateInputOffset(2)

  async function doRotate() {
    if (!account) return
    // Treat the chosen day as expiring at end of day (UTC) so picking today still lands in the future.
    const expiresAt = expiry ? new Date(`${expiry}T23:59:59.000Z`).toISOString() : null
    const generated = await rotate.mutateAsync({ accountId: account.id, expiresAt, description: description.trim() || null })
    setExpiry('')
    setDescription('')
    onRotated(generated.plaintext)
  }

  function startEdit(k: ApiKey) {
    setEditingId(k.id)
    setEditText(k.description ?? '')
  }

  async function saveEdit(k: ApiKey) {
    await update.mutateAsync({ accountId: k.accountId, keyId: k.id, description: editText.trim() || null })
    setEditingId(null)
    setEditText('')
  }

  function deleteKey(k: ApiKey) {
    const ok = window.confirm(t(
      k.revokedAt ? 'accounts.keys.confirmDeleteRevoked' : 'accounts.keys.confirmDeleteActive',
      { keyId: k.keyId },
    ))
    if (!ok) return
    del.mutate({ accountId: k.accountId, keyId: k.id })
  }

  function keyStatus(k: { revokedAt?: string | null; expiresAt?: string | null }) {
    if (k.revokedAt) return <Badge color="danger">{t('accounts.keys.status.revoked')}</Badge>
    if (k.expiresAt && new Date(k.expiresAt) <= new Date()) return <Badge color="danger">{t('accounts.keys.status.expired')}</Badge>
    return <Badge color="success">{t('accounts.keys.status.active')}</Badge>
  }

  return (
    <Dialog open={!!account} onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface style={{ minWidth: 'min(900px, 92vw)' }}>
        <DialogBody>
          <DialogTitle>{t('accounts.keys.title', { name: account?.label || account?.name })}</DialogTitle>
          <DialogContent>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {rotated && (
                <AutoScrollMessageBar intent="warning">
                  <MessageBarBody>
                    <MessageBarTitle>{t('accounts.keys.copyNow')}</MessageBarTitle>
                    <div className={s.rotated}>{rotated}</div>
                  </MessageBarBody>
                </AutoScrollMessageBar>
              )}
              <Table size="small">
                <TableHeader>
                  <TableRow>
                    <TableHeaderCell>{t('accounts.keys.keyId')}</TableHeaderCell>
                    <TableHeaderCell>{t('accounts.fields.description')}</TableHeaderCell>
                    <TableHeaderCell>{t('accounts.fields.created')}</TableHeaderCell>
                    <TableHeaderCell>{t('accounts.keys.expires')}</TableHeaderCell>
                    <TableHeaderCell>{t('accounts.fields.status')}</TableHeaderCell>
                    <TableHeaderCell></TableHeaderCell>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(keys.data ?? []).map(k => (
                    <TableRow key={k.id}>
                      <TableCell><code>{k.keyId}</code></TableCell>
                      <TableCell className={s.keyDescCell}>
                        {editingId === k.id ? (
                          <div className={s.keyDescInner}>
                            <Input
                              className={s.keyDescInput}
                              size="small"
                              value={editText}
                              maxLength={200}
                              placeholder={t('accounts.keys.descriptionPlaceholderShort')}
                              onChange={(_, d) => setEditText(d.value)}
                              onKeyDown={(e) => { if (e.key === 'Enter') saveEdit(k); if (e.key === 'Escape') setEditingId(null) }}
                            />
                            <Tooltip content={t('accounts.actions.save')} relationship="label">
                              <Button size="small" appearance="subtle" icon={<Checkmark20Regular />} onClick={() => saveEdit(k)} aria-label={t('accounts.keys.saveDescription')} />
                            </Tooltip>
                            <Tooltip content={t('accounts.actions.cancel')} relationship="label">
                              <Button size="small" appearance="subtle" icon={<Dismiss20Regular />} onClick={() => setEditingId(null)} aria-label={t('accounts.actions.cancel')} />
                            </Tooltip>
                          </div>
                        ) : (
                          <div className={s.keyDescInner}>
                            {k.description ? (
                              <Tooltip content={k.description} relationship="label">
                                <span className={s.keyDescText}>{k.description}</span>
                              </Tooltip>
                            ) : (
                              <span className={s.keyDescText} style={{ color: tokens.colorNeutralForeground3 }}>—</span>
                            )}
                            <Tooltip content={t('accounts.keys.editDescription')} relationship="label">
                              <Button size="small" appearance="subtle" icon={<Edit20Regular />} onClick={() => startEdit(k)} aria-label={t('accounts.keys.editDescription')} />
                            </Tooltip>
                          </div>
                        )}
                      </TableCell>
                      <TableCell style={{ whiteSpace: 'nowrap' }}><LocalizedTime value={k.createdAt} /></TableCell>
                      <TableCell style={{ whiteSpace: 'nowrap' }}>{k.expiresAt ? <LocalizedTime value={k.expiresAt} dateOnly /> : t('accounts.keys.never')}</TableCell>
                      <TableCell>{keyStatus(k)}</TableCell>
                      <TableCell>
                        <div className={s.keyActions}>
                          {!k.revokedAt && (
                            <Button size="small" onClick={() => revoke.mutate({ accountId: k.accountId, keyId: k.id })}>{t('accounts.keys.revoke')}</Button>
                          )}
                          <Tooltip content={t('accounts.keys.delete')} relationship="label">
                            <Button
                              size="small"
                              appearance="subtle"
                              icon={<Delete20Regular />}
                              onClick={() => deleteKey(k)}
                              aria-label={t('accounts.keys.deleteAria', { keyId: k.keyId })}
                            />
                          </Tooltip>
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              <Field label={t('accounts.keys.nextDescription')} hint={t('accounts.keys.descriptionHint')}>
                <Input value={description} maxLength={200} placeholder={t('accounts.keys.descriptionPlaceholder')} onChange={(_, d) => setDescription(d.value)} />
              </Field>
              <Field label={t('accounts.keys.nextExpiry')} hint={t('accounts.keys.expiryHint')}>
                <Input type="date" value={expiry} min={minExpiry} max={maxExpiry} onChange={(_, d) => setExpiry(d.value)} />
              </Field>
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="primary" icon={<ArrowRotateClockwise20Regular />} onClick={doRotate}>{t('accounts.keys.generate')}</Button>
            <DialogTrigger><Button appearance="secondary" onClick={onClose}>{t('accounts.actions.close')}</Button></DialogTrigger>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
