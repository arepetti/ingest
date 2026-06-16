import { useState } from 'react'
import {
  Badge, Body1, Button, Drawer, DrawerBody,
  Dropdown, Option, Field, Input, Textarea, Checkbox,
  Dialog, DialogSurface, DialogTitle, DialogBody, DialogContent, DialogActions, DialogTrigger,
  RadioGroup, Radio,
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, TableCellLayout,
  Title2, Tooltip, makeStyles, MessageBarBody, MessageBarTitle,
  Menu, MenuButton, MenuDivider, MenuItem, MenuList, MenuPopover, MenuTrigger, SplitButton,
  Toolbar, ToolbarButton, tokens,
} from '@fluentui/react-components'
import { Add20Regular, ArrowClockwise20Regular, ArrowDownload20Regular, ArrowRotateClockwise20Regular, Delete20Regular, Edit20Regular, Key20Regular, Mail20Regular, MoreHorizontal20Regular, PersonAdd20Regular, ShieldPerson20Regular, Status20Regular } from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { useNavigate } from 'react-router-dom'
import { fetchAllAccounts, useAccounts, useApiKeys, useAuthProviders, useCreateAccount, useDeleteAccount, useEraseAccount, useMe, useRevokeApiKey, useRotateApiKey, useSendAdhocEmail, useUpdateAccount, personalDataExportUrl } from '../api/hooks'
import type { Account, AccountKind, AccountRole, AuthProvider, CreateAccountRequest, ErasureMode, ErasureResult, ExternalLogin, UpdateAccountRequest } from '../api/types'
import { downloadFromUrl } from '../utils/download'
import { formatApiError } from '../api/client'
import { RowActions } from '../components/RowActions'
import { OnboardAccountWizard } from '../components/OnboardAccountWizard'
import { AccountAvatar } from '../components/Avatars'
import { DRAWER_EXPANDED_WIDTH, DrawerHeaderWithClose } from '../components/DrawerHeaderWithClose'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { useCsvExport, type ExportColumn } from '../utils/useCsvExport'
import { confirmDelete } from '../utils/confirm'
import { formatDate, formatDateTime } from '../utils/format'
import { clickableRowProps } from '../utils/a11y'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  toolbarActions: { display: 'flex', alignItems: 'center', gap: '16px' },
  drawer: { width: 'max(600px, 50vw)' },
  drawerForm: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '16px' },
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
})

const roles: AccountRole[] = ['Service', 'Operator', 'Admin']
const kinds: AccountKind[] = ['User', 'Application']

// Drop blank rows and trim emails before sending. The server lower-cases and de-duplicates, so we
// only need to filter out half-filled rows here.
function cleanLogins(links?: ExternalLogin[]): ExternalLogin[] {
  return (links ?? [])
    .map(l => ({ provider: l.provider, email: (l.email ?? '').trim() }))
    .filter(l => l.provider && l.email)
}

// Friendly one-liners for the Kind dropdown — Application is the default for service credentials,
// User is for humans who'll also log in to this admin console.
const kindHints: Record<AccountKind, string> = {
  Application: 'API-only credential (cannot log in to the UI)',
  User: 'Interactive account (can log in to the UI and call APIs)',
}

const ACCOUNT_EXPORT_COLUMNS: ExportColumn<Account>[] = [
  { header: 'Name', value: a => a.name },
  { header: 'Label', value: a => a.label ?? '' },
  { header: 'Kind', value: a => a.kind },
  { header: 'Role', value: a => a.role },
  { header: 'Status', value: a => (a.enabled ? 'Enabled' : 'Disabled') },
  { header: 'Email', value: a => a.email ?? '' },
  { header: 'Created', value: a => a.createdAt },
  { header: 'Created by', value: a => a.createdBy ?? '' },
]

export function ServicesPage() {
  const s = useStyles()
  const nav = useNavigate()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const { data, isLoading, error, refetch } = useAccounts({ page, pageSize })
  const { data: providers } = useAuthProviders()
  const { data: me } = useMe()
  const hasSso = (providers?.length ?? 0) > 0
  const emailEnabled = me?.emailEnabled === true
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
    columns: ACCOUNT_EXPORT_COLUMNS,
    fetchAll: () => fetchAllAccounts(),
    onError: setActionError,
  })
  // Per-drawer "expanded" state so the edit and view drawers can be enlarged independently
  // (e.g. expand the view to read a long description without losing the editor state).
  const [editorExpanded, setEditorExpanded] = useState(false)
  const [viewerExpanded, setViewerExpanded] = useState(false)

  function openCreate() {
    setEditing({ kind: 'create', account: { name: '', label: '', description: '', email: '', kind: 'Application', role: 'Service', enabled: true } })
    setSubmitError(null)
  }
  function openEdit(a: Account) {
    setEditing({ kind: 'edit', account: { ...a } })
    setSubmitError(null)
  }
  function editFromView(a: Account) { setViewing(null); openEdit(a) }
  function keysFromView(a: Account) { setViewing(null); setKeyDialogFor(a) }
  function emailFromView(a: Account) { setViewing(null); setEmailDialogFor(a) }
  function statusFromView(a: Account) { setViewing(null); nav(`/services/${encodeURIComponent(a.name)}/status`) }
  function deleteFromView(a: Account) {
    if (!confirmDelete('account', a.label || a.name)) return
    setViewing(null)
    del.mutate(a.id)
  }
  function deleteFromRow(a: Account) {
    if (!confirmDelete('account', a.label || a.name)) return
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
    // Email is required for new accounts; existing accounts may keep an empty email (legacy data),
    // so editing doesn't force one. A non-empty value must still look like an address.
    if (editing.kind === 'create' && !email) { setSubmitError('Email is required.'); return }
    if (email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) { setSubmitError('Enter a valid email address.'); return }
    // Only User-kind accounts can hold SSO links; never send them for Application accounts.
    const logins = a.kind === 'User' ? cleanLogins(a.externalLogins) : []
    try {
      if (editing.kind === 'create') {
        const req: CreateAccountRequest = {
          name: a.name ?? '',
          label: a.label,
          description: a.description,
          email,
          kind: a.kind ?? 'Application',
          role: a.role ?? 'Service',
          enabled: a.enabled ?? true,
          // Only include when SSO is on so an API-key-only deployment never touches this field.
          ...(hasSso ? { externalLogins: logins } : {}),
        }
        await create.mutateAsync(req)
      } else {
        const req: UpdateAccountRequest = {
          label: a.label,
          description: a.description,
          email,
          role: a.role ?? 'Service',
          enabled: a.enabled ?? true,
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
        <Title2>Accounts</Title2>
        <Toolbar className={s.toolbarActions}>
          <ToolbarButton appearance="primary" icon={<Add20Regular />} onClick={openCreate}>New account</ToolbarButton>
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label="More actions" />
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem icon={<PersonAdd20Regular />} onClick={() => setOnboarding('Service')}>Onboard new service</MenuItem>
                <MenuItem icon={<PersonAdd20Regular />} onClick={() => setOnboarding('Operator')}>Onboard new operator</MenuItem>
                <MenuDivider />
                <MenuItem icon={<ArrowClockwise20Regular />} onClick={() => refetch()}>Refresh</MenuItem>
                <MenuDivider />
                <MenuItem
                  icon={<ArrowDownload20Regular />}
                  disabled={accountsExport.exporting}
                  onClick={accountsExport.exportList}
                >
                  {accountsExport.exporting ? 'Exporting…' : 'Export this list'}
                </MenuItem>
              </MenuList>
            </MenuPopover>
          </Menu>
        </Toolbar>
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Failed to load</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {actionError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Action failed</MessageBarTitle>
            {actionError}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Table size="small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>Name</TableHeaderCell>
            <TableHeaderCell>Kind</TableHeaderCell>
            <TableHeaderCell>Role</TableHeaderCell>
            <TableHeaderCell>Status</TableHeaderCell>
            <TableHeaderCell>Created</TableHeaderCell>
            <TableHeaderCell>Created by</TableHeaderCell>
            <TableHeaderCell className={s.actionsHeader}>Actions</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={7}>Loading…</GridMessageRow>}
          {!isLoading && (data?.items ?? []).length === 0 && (
            <GridMessageRow colSpan={7}>No accounts yet — click “New account” to add one.</GridMessageRow>
          )}
          {(data?.items ?? []).map(a => (
            <TableRow
              key={a.id}
              className={`${s.row} ${s.rowClickable}`}
              {...clickableRowProps(() => setViewing(a), `View account ${a.label || a.name}`)}
            >
              <TableCell className={s.nameCell}>
                <TableCellLayout media={<AccountAvatar account={a} />} description={a.description ?? ''}>
                  <Tooltip content={a.label || a.name} relationship="label">
                    <strong className={s.truncate}>{a.label || a.name}</strong>
                  </Tooltip>
                </TableCellLayout>
              </TableCell>
              <TableCell>{a.kind}</TableCell>
              <TableCell>{a.role}</TableCell>
              <TableCell>
                <Badge appearance="outline" color={a.enabled ? 'success' : 'danger'}>
                  {a.enabled ? 'Enabled' : 'Disabled'}
                </Badge>
              </TableCell>
              <TableCell>
                <Tooltip content={formatDateTime(a.createdAt)} relationship="label">
                  <span>{formatDate(a.createdAt)}</span>
                </Tooltip>
              </TableCell>
              <TableCell>{a.createdBy || '—'}</TableCell>
              <TableCell className={s.actionsCell} onClick={e => e.stopPropagation()}>
                <RowActions
                  ariaLabel={`Actions for ${a.name}`}
                  actions={[
                    { key: 'edit', label: 'Edit', icon: <Edit20Regular />, onClick: () => openEdit(a) },
                    { key: 'keys', label: 'API keys', icon: <Key20Regular />, onClick: () => setKeyDialogFor(a) },
                    ...(emailEnabled && a.email
                      ? [{ key: 'email', label: 'Send email', icon: <Mail20Regular />, onClick: () => setEmailDialogFor(a) }]
                      : []),
                    ...(a.role === 'Service'
                      ? [{
                          key: 'status',
                          label: 'View status',
                          icon: <Status20Regular />,
                          onClick: () => nav(`/services/${encodeURIComponent(a.name)}/status`),
                        }]
                      : []),
                    { key: 'export', label: 'Export personal data', icon: <ArrowDownload20Regular />, onClick: () => exportPersonalData(a) },
                    { key: 'erase', label: 'Erase (GDPR)', icon: <ShieldPerson20Regular />, destructive: true, onClick: () => setEraseDialogFor(a) },
                    { key: 'delete', label: 'Delete', icon: <Delete20Regular />, destructive: true, onClick: () => deleteFromRow(a) },
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
          title={editing?.kind === 'create' ? 'New account' : 'Edit account'}
          onClose={() => { setEditing(null); setEditorExpanded(false) }}
          expanded={editorExpanded}
          onToggleExpand={() => setEditorExpanded(e => !e)}
        />
        <DrawerBody>
          {editing && (
            <div className={s.drawerForm}>
              <Field label="Name" required>
                <Input
                  value={editing.account.name ?? ''}
                  disabled={editing.kind === 'edit'}
                  onChange={(_, v) => setEditing({ ...editing, account: { ...editing.account, name: v.value } })}
                />
              </Field>
              <Field label="Label">
                <Input value={editing.account.label ?? ''} onChange={(_, v) => setEditing({ ...editing, account: { ...editing.account, label: v.value } })} />
              </Field>
              <Field label="Description">
                <Textarea value={editing.account.description ?? ''} onChange={(_, v) => setEditing({ ...editing, account: { ...editing.account, description: v.value } })} />
              </Field>
              <Field label="Email" required={editing.kind === 'create'} hint="Used for email notifications and ad-hoc messages.">
                <Input
                  type="email"
                  value={editing.account.email ?? ''}
                  placeholder="user@example.com"
                  onChange={(_, v) => setEditing({ ...editing, account: { ...editing.account, email: v.value } })}
                />
              </Field>
              <Field label="Kind" hint={kindHints[(editing.account.kind ?? 'Application') as AccountKind]}>
                <Dropdown
                  selectedOptions={[editing.account.kind ?? 'Application']}
                  value={editing.account.kind ?? 'Application'}
                  disabled={editing.kind === 'edit'}
                  onOptionSelect={(_, d) => setEditing({ ...editing, account: { ...editing.account, kind: d.optionValue as AccountKind } })}
                >
                  {kinds.map(k => <Option key={k} value={k}>{k}</Option>)}
                </Dropdown>
              </Field>
              <Field label="Role">
                <Dropdown
                  selectedOptions={[editing.account.role ?? 'Service']}
                  value={editing.account.role ?? 'Service'}
                  onOptionSelect={(_, d) => setEditing({ ...editing, account: { ...editing.account, role: d.optionValue as AccountRole } })}
                >
                  {roles.map(r => <Option key={r} value={r}>{r}</Option>)}
                </Dropdown>
              </Field>
              <Checkbox label="Enabled" checked={editing.account.enabled ?? true} onChange={(_, d) => setEditing({ ...editing, account: { ...editing.account, enabled: !!d.checked } })} />

              {hasSso && (editing.account.kind ?? 'Application') === 'User' && (
                <ExternalLoginsEditor
                  providers={providers ?? []}
                  links={editing.account.externalLogins ?? []}
                  onChange={(next) => setEditing({ ...editing, account: { ...editing.account, externalLogins: next } })}
                />
              )}

              {submitError && (
                <AutoScrollMessageBar intent="error">
                  <MessageBarBody>{submitError}</MessageBarBody>
                </AutoScrollMessageBar>
              )}

              <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', marginTop: 8 }}>
                <Button onClick={() => setEditing(null)}>Cancel</Button>
                <Button appearance="primary" onClick={onSave}>Save</Button>
              </div>
            </div>
          )}
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
          title={viewing ? (viewing.label || viewing.name) : 'Account'}
          onClose={() => { setViewing(null); setViewerExpanded(false) }}
          expanded={viewerExpanded}
          onToggleExpand={() => setViewerExpanded(e => !e)}
        />
        {viewing && (
          <Toolbar className={s.drawerToolbar}>
            <ToolbarButton icon={<Edit20Regular />} onClick={() => editFromView(viewing)}>Edit</ToolbarButton>
            <ToolbarButton icon={<Key20Regular />} onClick={() => keysFromView(viewing)}>API keys</ToolbarButton>
            {emailEnabled && viewing.email && (
              <ToolbarButton icon={<Mail20Regular />} onClick={() => emailFromView(viewing)}>Send email</ToolbarButton>
            )}
            {viewing.role === 'Service' && (
              <ToolbarButton icon={<Status20Regular />} onClick={() => statusFromView(viewing)}>View status</ToolbarButton>
            )}
            <ToolbarButton icon={<ArrowDownload20Regular />} onClick={() => exportPersonalData(viewing)}>Export data</ToolbarButton>
            {/* Default action is Delete; the chevron exposes the heavier GDPR erase as a subitem. */}
            <Menu positioning="below-end">
              <MenuTrigger disableButtonEnhancement>
                {(triggerProps) => (
                  <SplitButton
                    menuButton={triggerProps}
                    primaryActionButton={{ onClick: () => deleteFromView(viewing) }}
                    appearance="subtle"
                    icon={<Delete20Regular />}
                  >
                    Delete
                  </SplitButton>
                )}
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  <MenuItem icon={<Delete20Regular />} onClick={() => deleteFromView(viewing)}>
                    Delete
                  </MenuItem>
                  <MenuItem
                    icon={<ShieldPerson20Regular />}
                    onClick={() => eraseFromView(viewing)}
                    style={{ color: 'var(--colorPaletteRedForeground1)' }}
                  >
                    Erase (GDPR)
                  </MenuItem>
                </MenuList>
              </MenuPopover>
            </Menu>
          </Toolbar>
        )}
        <DrawerBody>
          {viewing && <AccountViewBody account={viewing} />}
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
          title={onboarding === 'Operator' ? 'Onboard a new operator' : 'Onboard a new service'}
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
          <DialogTitle>Erase {account?.label || account?.name}</DialogTitle>
          <DialogContent>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}
              {result ? (
                <AutoScrollMessageBar intent="success">
                  <MessageBarBody>
                    <MessageBarTitle>
                      {result.mode === 'Delete' ? 'Account deleted.' : 'Account anonymised.'}
                    </MessageBarTitle>
                    Pseudonym <code>{result.pseudonym}</code> · {result.submissionsAffected} submission(s),
                    {' '}{result.samplesAffected} sample(s), {result.emailsRemoved} email(s),
                    {' '}{result.auditEntriesAffected} audit entr(ies), {result.apiKeysRemoved} key(s).
                  </MessageBarBody>
                </AutoScrollMessageBar>
              ) : (
                <>
                  <Body1>
                    This satisfies a right-to-erasure request. It is <strong>irreversible</strong> and
                    bypasses the usual “account has submitted data” protection.
                  </Body1>
                  <Field label="Mode">
                    <RadioGroup value={mode} onChange={(_, d) => setMode(d.value as ErasureMode)}>
                      <Radio value="Anonymise" label="Anonymise — keep numeric/date KPI values for reporting, strip all identity (pseudonymise the account, redact free-text, drop keys & emails)." />
                      <Radio value="Delete" label="Delete — permanently remove the account and everything tied to it (submissions, samples, emails, audit trail)." />
                    </RadioGroup>
                  </Field>
                  <Checkbox
                    label="I understand this cannot be undone."
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
                {erase.isPending ? 'Erasing…' : (mode === 'Delete' ? 'Delete everything' : 'Anonymise')}
              </Button>
            )}
            <Button appearance="secondary" onClick={handleClose}>{result ? 'Close' : 'Cancel'}</Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

// Ad-hoc plain-text email to a single account. The message is queued into the outbox and delivered
// by the email sender like any other; success here just means "accepted into the queue".
function SendEmailDialog({ account, onClose }: { account: Account | null; onClose: () => void }) {
  const send = useSendAdhocEmail()
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [sent, setSent] = useState(false)

  async function onSend() {
    if (!account) return
    setError(null)
    if (!subject.trim()) { setError('Subject is required.'); return }
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
          <DialogTitle>Send email to {account?.label || account?.name}</DialogTitle>
          <DialogContent>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <Body1>To: {account?.email}</Body1>
              {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}
              {sent && <AutoScrollMessageBar intent="success"><MessageBarBody>Email queued for delivery.</MessageBarBody></AutoScrollMessageBar>}
              <Field label="Subject" required>
                <Input value={subject} onChange={(_, d) => setSubject(d.value)} disabled={sent} />
              </Field>
              <Field label="Message">
                <Textarea value={body} onChange={(_, d) => setBody(d.value)} rows={8} resize="vertical" disabled={sent} />
              </Field>
            </div>
          </DialogContent>
          <DialogActions>
            {!sent && (
              <Button appearance="primary" icon={<Mail20Regular />} disabled={send.isPending} onClick={onSend}>
                {send.isPending ? 'Sending…' : 'Send'}
              </Button>
            )}
            <Button appearance="secondary" onClick={handleClose}>{sent ? 'Close' : 'Cancel'}</Button>
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
      <div className={s.sectionLabel}>SSO sign-in</div>
      <div className={s.hint}>
        Link an identity-provider account so this user can sign in with “Continue with …”. Matched on
        the provider and the user’s verified email.
      </div>
      {links.map((link, i) => (
        <div key={i} className={s.linkRow}>
          <Field label={i === 0 ? 'Provider' : undefined}>
            <Dropdown
              selectedOptions={[link.provider]}
              value={providers.find(p => p.id === link.provider)?.displayName ?? link.provider}
              onOptionSelect={(_, d) => update(i, { provider: d.optionValue as string })}
            >
              {providers.map(p => <Option key={p.id} value={p.id}>{p.displayName}</Option>)}
            </Dropdown>
          </Field>
          <Field label={i === 0 ? 'Email' : undefined}>
            <Input
              type="email"
              value={link.email}
              placeholder="user@example.com"
              onChange={(_, v) => update(i, { email: v.value })}
            />
          </Field>
          <Button appearance="subtle" icon={<Delete20Regular />} aria-label="Remove SSO link" onClick={() => remove(i)} />
        </div>
      ))}
      <Button appearance="secondary" icon={<Add20Regular />} onClick={add}>Add SSO link</Button>
    </div>
  )
}

function AccountViewBody({ account }: { account: Account }) {
  const s = useStyles()
  const links = account.externalLogins ?? []
  return (
    <div className={s.drawerForm}>
      <div className={s.twoCol}>
        <Field label="Name"><Body1>{account.name}</Body1></Field>
        <Field label="Label"><Body1>{account.label || '—'}</Body1></Field>
      </div>
      {account.description && <Field label="Description"><Body1>{account.description}</Body1></Field>}

      <Field label="Email"><Body1>{account.email || '—'}</Body1></Field>

      <div className={s.twoCol}>
        <Field label="Kind"><Body1>{account.kind}</Body1></Field>
        <Field label="Role"><Body1>{account.role}</Body1></Field>
      </div>
      <Field label="Enabled"><Body1>{account.enabled ? 'Yes' : 'No'}</Body1></Field>

      {links.length > 0 && (
        <Field label="SSO sign-in">
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
          <Badge appearance="outline" color="danger">Deleted</Badge>
        </div>
      )}

      <div className={s.sectionLabel}>Audit</div>
      <div className={s.twoCol}>
        <Field label="Created">
          <Body1>
            {new Date(account.createdAt).toLocaleString()}
            {account.createdBy ? ` · by ${account.createdBy}` : ''}
          </Body1>
        </Field>
        <Field label="Modified">
          <Body1>
            {new Date(account.modifiedAt).toLocaleString()}
            {account.modifiedBy ? ` · by ${account.modifiedBy}` : ''}
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
  const keys = useApiKeys(account?.id)
  const rotate = useRotateApiKey()
  const revoke = useRevokeApiKey()
  const [expiry, setExpiry] = useState('')

  // The server caps a new key's lifetime at two years; mirror that on the input.
  const minExpiry = dateInputOffset(0, 1)
  const maxExpiry = dateInputOffset(2)

  async function doRotate() {
    if (!account) return
    // Treat the chosen day as expiring at end of day (UTC) so picking today still lands in the future.
    const expiresAt = expiry ? new Date(`${expiry}T23:59:59.000Z`).toISOString() : null
    const generated = await rotate.mutateAsync({ accountId: account.id, expiresAt })
    setExpiry('')
    onRotated(generated.plaintext)
  }

  function keyStatus(k: { revokedAt?: string | null; expiresAt?: string | null }) {
    if (k.revokedAt) return <Badge color="danger">Revoked</Badge>
    if (k.expiresAt && new Date(k.expiresAt) <= new Date()) return <Badge color="danger">Expired</Badge>
    return <Badge color="success">Active</Badge>
  }

  return (
    <Dialog open={!!account} onOpenChange={(_, d) => !d.open && onClose()}>
      <DialogSurface style={{ minWidth: 560 }}>
        <DialogBody>
          <DialogTitle>API keys for {account?.label || account?.name}</DialogTitle>
          <DialogContent>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {rotated && (
                <AutoScrollMessageBar intent="warning">
                  <MessageBarBody>
                    <MessageBarTitle>Copy this key now — it will not be shown again.</MessageBarTitle>
                    <div className={s.rotated}>{rotated}</div>
                  </MessageBarBody>
                </AutoScrollMessageBar>
              )}
              <Table size="small">
                <TableHeader>
                  <TableRow>
                    <TableHeaderCell>Key ID</TableHeaderCell>
                    <TableHeaderCell>Created</TableHeaderCell>
                    <TableHeaderCell>Expires</TableHeaderCell>
                    <TableHeaderCell>Status</TableHeaderCell>
                    <TableHeaderCell></TableHeaderCell>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(keys.data ?? []).map(k => (
                    <TableRow key={k.id}>
                      <TableCell><code>{k.keyId}</code></TableCell>
                      <TableCell>{new Date(k.createdAt).toLocaleString()}</TableCell>
                      <TableCell>{k.expiresAt ? new Date(k.expiresAt).toLocaleDateString() : 'Never'}</TableCell>
                      <TableCell>{keyStatus(k)}</TableCell>
                      <TableCell>
                        {!k.revokedAt && (
                          <Button size="small" onClick={() => revoke.mutate({ accountId: k.accountId, keyId: k.id })}>Revoke</Button>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              <Field label="Expiry for the next key (optional)" hint="Leave blank for a key that never expires. Maximum two years from today.">
                <Input type="date" value={expiry} min={minExpiry} max={maxExpiry} onChange={(_, d) => setExpiry(d.value)} />
              </Field>
            </div>
          </DialogContent>
          <DialogActions>
            <Button appearance="primary" icon={<ArrowRotateClockwise20Regular />} onClick={doRotate}>Generate new key</Button>
            <DialogTrigger><Button appearance="secondary" onClick={onClose}>Close</Button></DialogTrigger>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}
