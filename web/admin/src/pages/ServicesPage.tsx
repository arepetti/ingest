import { useState } from 'react'
import {
  Badge, Body1, Button, Drawer, DrawerBody,
  Dropdown, Option, Field, Input, Textarea, Checkbox,
  Dialog, DialogSurface, DialogTitle, DialogBody, DialogContent, DialogActions, DialogTrigger,
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, TableCellLayout,
  Title2, Tooltip, makeStyles, MessageBarBody, MessageBarTitle,
  Toolbar, ToolbarButton, tokens,
} from '@fluentui/react-components'
import { Add20Regular, ArrowRotateClockwise20Regular, Delete20Regular, Edit20Regular, Key20Regular, Status20Regular } from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { useNavigate } from 'react-router-dom'
import { useAccounts, useApiKeys, useCreateAccount, useDeleteAccount, useRevokeApiKey, useRotateApiKey, useUpdateAccount } from '../api/hooks'
import type { Account, AccountKind, AccountRole, CreateAccountRequest, UpdateAccountRequest } from '../api/types'
import { formatApiError } from '../api/client'
import { RowActions } from '../components/RowActions'
import { AccountAvatar } from '../components/Avatars'
import { DRAWER_EXPANDED_WIDTH, DrawerHeaderWithClose } from '../components/DrawerHeaderWithClose'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { confirmDelete } from '../utils/confirm'
import { formatDate, formatDateTime } from '../utils/format'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  drawer: { width: 'max(600px, 50vw)' },
  drawerForm: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '16px' },
  twoCol: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' },
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
  rowClickable: { cursor: 'pointer' },
  drawerToolbar: {
    width: '100%',
    boxSizing: 'border-box',
    padding: '0 16px',
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
})

const roles: AccountRole[] = ['Service', 'Operator', 'Admin']
const kinds: AccountKind[] = ['User', 'Application']

// Friendly one-liners for the Kind dropdown — Application is the default for service credentials,
// User is for humans who'll also log in to this admin console.
const kindHints: Record<AccountKind, string> = {
  Application: 'API-only credential (cannot log in to the UI)',
  User: 'Interactive account (can log in to the UI and call APIs)',
}

export function ServicesPage() {
  const s = useStyles()
  const nav = useNavigate()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const { data, isLoading, error } = useAccounts({ page, pageSize })
  const create = useCreateAccount()
  const update = useUpdateAccount()
  const del = useDeleteAccount()

  const [editing, setEditing] = useState<{ kind: 'create' | 'edit'; account: Partial<Account> } | null>(null)
  const [viewing, setViewing] = useState<Account | null>(null)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [keyDialogFor, setKeyDialogFor] = useState<Account | null>(null)
  const [rotatedPlaintext, setRotatedPlaintext] = useState<string | null>(null)
  // Per-drawer "expanded" state so the edit and view drawers can be enlarged independently
  // (e.g. expand the view to read a long description without losing the editor state).
  const [editorExpanded, setEditorExpanded] = useState(false)
  const [viewerExpanded, setViewerExpanded] = useState(false)

  function openCreate() {
    setEditing({ kind: 'create', account: { name: '', label: '', description: '', kind: 'Application', role: 'Service', enabled: true } })
    setSubmitError(null)
  }
  function openEdit(a: Account) {
    setEditing({ kind: 'edit', account: { ...a } })
    setSubmitError(null)
  }
  function editFromView(a: Account) { setViewing(null); openEdit(a) }
  function keysFromView(a: Account) { setViewing(null); setKeyDialogFor(a) }
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

  async function onSave() {
    if (!editing) return
    setSubmitError(null)
    const a = editing.account
    try {
      if (editing.kind === 'create') {
        const req: CreateAccountRequest = {
          name: a.name ?? '',
          label: a.label,
          description: a.description,
          kind: a.kind ?? 'Application',
          role: a.role ?? 'Service',
          enabled: a.enabled ?? true,
        }
        await create.mutateAsync(req)
      } else {
        const req: UpdateAccountRequest = {
          label: a.label,
          description: a.description,
          role: a.role ?? 'Service',
          enabled: a.enabled ?? true,
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
        <Toolbar>
          <ToolbarButton appearance="primary" icon={<Add20Regular />} onClick={openCreate}>New account</ToolbarButton>
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
              onClick={() => setViewing(a)}
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
                    ...(a.role === 'Service'
                      ? [{
                          key: 'status',
                          label: 'View status',
                          icon: <Status20Regular />,
                          onClick: () => nav(`/services/${encodeURIComponent(a.name)}/status`),
                        }]
                      : []),
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
            {viewing.role === 'Service' && (
              <ToolbarButton icon={<Status20Regular />} onClick={() => statusFromView(viewing)}>View status</ToolbarButton>
            )}
            <ToolbarButton icon={<Delete20Regular />} onClick={() => deleteFromView(viewing)}>Delete</ToolbarButton>
          </Toolbar>
        )}
        <DrawerBody>
          {viewing && <AccountViewBody account={viewing} />}
        </DrawerBody>
      </Drawer>

      <KeysDialog account={keyDialogFor} onClose={() => { setKeyDialogFor(null); setRotatedPlaintext(null) }} rotated={rotatedPlaintext} onRotated={setRotatedPlaintext} />
    </div>
  )
}

function AccountViewBody({ account }: { account: Account }) {
  const s = useStyles()
  return (
    <div className={s.drawerForm}>
      <div className={s.twoCol}>
        <Field label="Name"><Body1>{account.name}</Body1></Field>
        <Field label="Label"><Body1>{account.label || '—'}</Body1></Field>
      </div>
      {account.description && <Field label="Description"><Body1>{account.description}</Body1></Field>}

      <div className={s.twoCol}>
        <Field label="Kind"><Body1>{account.kind}</Body1></Field>
        <Field label="Role"><Body1>{account.role}</Body1></Field>
      </div>
      <Field label="Enabled"><Body1>{account.enabled ? 'Yes' : 'No'}</Body1></Field>

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

function KeysDialog({ account, onClose, rotated, onRotated }: { account: Account | null; onClose: () => void; rotated: string | null; onRotated: (v: string) => void }) {
  const s = useStyles()
  const keys = useApiKeys(account?.id)
  const rotate = useRotateApiKey()
  const revoke = useRevokeApiKey()

  async function doRotate() {
    if (!account) return
    const generated = await rotate.mutateAsync(account.id)
    onRotated(generated.plaintext)
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
                    <TableHeaderCell>Status</TableHeaderCell>
                    <TableHeaderCell></TableHeaderCell>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {(keys.data ?? []).map(k => (
                    <TableRow key={k.id}>
                      <TableCell><code>{k.keyId}</code></TableCell>
                      <TableCell>{new Date(k.createdAt).toLocaleString()}</TableCell>
                      <TableCell>
                        {k.revokedAt ? <Badge color="danger">Revoked</Badge> : <Badge color="success">Active</Badge>}
                      </TableCell>
                      <TableCell>
                        {!k.revokedAt && (
                          <Button size="small" onClick={() => revoke.mutate({ accountId: k.accountId, keyId: k.id })}>Revoke</Button>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
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
