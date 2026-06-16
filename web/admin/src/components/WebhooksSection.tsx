import { useState } from 'react'
import {
  Badge, Body1, Button, Card, Checkbox, Dialog, DialogActions, DialogBody, DialogContent,
  DialogSurface, DialogTitle, Dropdown, Drawer, DrawerBody, Field, Input, MessageBarBody,
  Option, Switch, Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow,
  Textarea, Title3, Tooltip, makeStyles, tokens,
} from '@fluentui/react-components'
import {
  Add20Regular, Copy20Regular, Send20Regular, KeyReset20Regular, Delete20Regular,
} from '@fluentui/react-icons'
import { Link as RouterLink } from 'react-router-dom'
import { AutoScrollMessageBar } from './AutoScrollMessageBar'
import { DrawerHeaderWithClose } from './DrawerHeaderWithClose'
import { GridMessageRow } from './GridPager'
import { RowActions } from './RowActions'
import { clickableRowProps } from '../utils/a11y'
import { formatApiError } from '../api/client'
import {
  useAccounts,
  useWebhookEndpoints, useCreateWebhookEndpoint, useUpdateWebhookEndpoint, useDeleteWebhookEndpoint,
  useRotateWebhookSecret, useSendWebhookTest,
} from '../api/hooks'
import type {
  WebhookEndpoint, WebhookEventKind,
} from '../api/types'

/** Catalogue of subscribable events with their consumer-facing dotted name + a one-line gloss. */
const EVENTS: { kind: WebhookEventKind; label: string; wire: string; desc: string }[] = [
  { kind: 'SubmissionAccepted', label: 'Submission accepted', wire: 'submission.accepted', desc: 'A service submitted data and it was accepted.' },
  { kind: 'SubmissionWarnings', label: 'Submission warnings', wire: 'submission.warnings', desc: 'An accepted submission carried non-blocking warnings.' },
  { kind: 'WindowUpcoming', label: 'Window upcoming', wire: 'window.upcoming', desc: 'A submission window is approaching its close.' },
  { kind: 'WindowMissed', label: 'Window missed', wire: 'window.missed', desc: 'A window closed without the required submission.' },
  { kind: 'SubmissionPendingApproval', label: 'Submission pending approval', wire: 'submission.pending_approval', desc: 'A submission was accepted but is held awaiting approval.' },
  { kind: 'SubmissionApproved', label: 'Submission approved', wire: 'submission.approved', desc: 'A pending submission was approved and is now live.' },
  { kind: 'SubmissionRejected', label: 'Submission rejected', wire: 'submission.rejected', desc: 'A pending submission was rejected and will not go live.' },
]

const ALL = '__all__'

const useStyles = makeStyles({
  card: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '20px' },
  sectionTitle: { display: 'block', marginBottom: '2px' },
  help: { color: tokens.colorNeutralForeground3 },
  headerRow: { display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap' },
  actions: { display: 'flex', gap: '12px', flexWrap: 'wrap', marginTop: '4px' },
  table: { tableLayout: 'fixed', width: '100%' },
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  rowClickable: {
    cursor: 'pointer',
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '-2px' },
  },
  truncate: { display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  cellTrunc: { maxWidth: 0 },
  colStatus: { width: '110px' },
  colEvents: { width: '120px' },
  colActions: { width: '52px' },
  mono: { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200, wordBreak: 'break-all' },
  drawer: { width: 'max(560px, 42vw)' },
  drawerForm: { display: 'flex', flexDirection: 'column', gap: '14px' },
  eventList: { display: 'flex', flexDirection: 'column', gap: '4px' },
  eventHelp: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200, marginLeft: '26px' },
  secretBox: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase300,
    padding: '10px 12px',
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    wordBreak: 'break-all',
  },
})

/**
 * Outbound webhooks admin section: register endpoints (with a per-event subscription and an
 * optional per-service filter), manage each endpoint's HMAC signing secret, and fire a test ping.
 * The delivery log itself lives on the Audit page (Audit → Webhook deliveries), next to sent emails.
 */
export function WebhooksSection() {
  const s = useStyles()
  const { data: endpoints, isLoading } = useWebhookEndpoints()
  const [editing, setEditing] = useState<WebhookEndpoint | 'new' | null>(null)
  // Plaintext secret to reveal once after create/rotate (cleared when the dialog closes).
  const [revealed, setRevealed] = useState<{ name: string; secret: string } | null>(null)
  const [banner, setBanner] = useState<{ intent: 'success' | 'error'; text: string } | null>(null)

  const del = useDeleteWebhookEndpoint()
  const rotate = useRotateWebhookSecret()
  const test = useSendWebhookTest()

  async function onDelete(e: WebhookEndpoint) {
    if (!window.confirm(`Delete the webhook endpoint “${e.name}”?\n\nIts past deliveries are kept for audit.`)) return
    setBanner(null)
    try {
      await del.mutateAsync(e.id)
      setBanner({ intent: 'success', text: `Deleted “${e.name}”.` })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  async function onRotate(e: WebhookEndpoint) {
    if (!window.confirm(`Generate a new signing secret for “${e.name}”?\n\nThe old secret stops working immediately.`)) return
    setBanner(null)
    try {
      const res = await rotate.mutateAsync(e.id)
      setRevealed({ name: e.name, secret: res.secret })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  async function onTest(e: WebhookEndpoint) {
    setBanner(null)
    try {
      await test.mutateAsync(e.id)
      setBanner({ intent: 'success', text: `Test delivery queued for “${e.name}”. Check the delivery log below.` })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  const items = endpoints ?? []

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
      <Card className={s.card}>
        <div className={s.headerRow}>
          <div>
            <Title3 className={s.sectionTitle}>Webhook endpoints</Title3>
            <Body1 className={s.help}>
              Send a signed HTTP POST to an external URL (Teams, Power Automate, your own service)
              when a subscribed event happens — no polling required.
            </Body1>
          </div>
          <Button appearance="primary" icon={<Add20Regular />} onClick={() => setEditing('new')}>
            Add endpoint
          </Button>
        </div>

        {banner && (
          <AutoScrollMessageBar intent={banner.intent}>
            <MessageBarBody>{banner.text}</MessageBarBody>
          </AutoScrollMessageBar>
        )}

        <Table size="small" className={s.table}>
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Name</TableHeaderCell>
              <TableHeaderCell>URL</TableHeaderCell>
              <TableHeaderCell className={s.colEvents}>Events</TableHeaderCell>
              <TableHeaderCell className={s.colStatus}>Status</TableHeaderCell>
              <TableHeaderCell className={s.colActions} aria-label="Actions" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <GridMessageRow colSpan={5}>Loading…</GridMessageRow>}
            {!isLoading && items.length === 0 && (
              <GridMessageRow colSpan={5}>No endpoints yet. Add one to start receiving events.</GridMessageRow>
            )}
            {items.map(e => (
              <TableRow
                key={e.id}
                className={`${s.row} ${s.rowClickable}`}
                {...clickableRowProps(() => setEditing(e), `Edit endpoint ${e.name}`)}
              >
                <TableCell className={s.cellTrunc}>
                  <strong className={s.truncate}>{e.name}</strong>
                  {!e.hasSecret && (
                    <Tooltip content="No signing secret set — deliveries are sent unsigned." relationship="label">
                      <span className={s.truncate} style={{ color: tokens.colorPaletteDarkOrangeForeground1, fontSize: tokens.fontSizeBase200 }}>
                        unsigned
                      </span>
                    </Tooltip>
                  )}
                </TableCell>
                <TableCell className={s.cellTrunc}>
                  <Tooltip content={e.url} relationship="label">
                    <span className={`${s.truncate} ${s.mono}`}>{e.url}</span>
                  </Tooltip>
                </TableCell>
                <TableCell className={s.colEvents}>
                  <Tooltip content={e.events.map(wireName).join(', ') || 'No events'} relationship="label">
                    <span className={s.truncate}>{e.events.length} event{e.events.length === 1 ? '' : 's'}</span>
                  </Tooltip>
                </TableCell>
                <TableCell className={s.colStatus}>
                  <Badge appearance="outline" color={e.enabled ? 'success' : 'informative'}>
                    {e.enabled ? 'Enabled' : 'Disabled'}
                  </Badge>
                </TableCell>
                <TableCell className={s.colActions} onClick={ev => ev.stopPropagation()}>
                  <RowActions
                    ariaLabel={`Actions for ${e.name}`}
                    actions={[
                      { key: 'test', label: 'Send test', icon: <Send20Regular />, onClick: () => onTest(e), disabled: test.isPending },
                      { key: 'rotate', label: e.hasSecret ? 'Rotate secret' : 'Generate secret', icon: <KeyReset20Regular />, onClick: () => onRotate(e), disabled: rotate.isPending },
                      { key: 'delete', label: 'Delete', icon: <Delete20Regular />, destructive: true, onClick: () => onDelete(e) },
                    ]}
                  />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <Body1 className={s.help}>
          Delivery history — status, retries, and manual redelivery — lives on the{' '}
          <RouterLink to="/audit">Audit page</RouterLink> under <strong>Webhook deliveries</strong>.
        </Body1>
      </Card>

      <Drawer
        type="overlay"
        separator
        open={!!editing}
        onOpenChange={(_, d) => { if (!d.open) setEditing(null) }}
        position="end"
        className={s.drawer}
      >
        <DrawerHeaderWithClose
          title={editing === 'new' ? 'Add webhook endpoint' : `Edit endpoint${editing ? ` — ${editing.name}` : ''}`}
          onClose={() => setEditing(null)}
        />
        <DrawerBody>
          {editing && (
            <EndpointEditor
              endpoint={editing === 'new' ? null : editing}
              key={editing === 'new' ? 'new' : editing.id}
              onClose={() => setEditing(null)}
              onSecret={(name, secret) => setRevealed({ name, secret })}
            />
          )}
        </DrawerBody>
      </Drawer>

      <SecretDialog reveal={revealed} onClose={() => setRevealed(null)} />
    </div>
  )
}

function wireName(kind: WebhookEventKind): string {
  return EVENTS.find(e => e.kind === kind)?.wire ?? kind
}

// --- Endpoint create / edit form ----------------------------------------------------------

function EndpointEditor({
  endpoint, onClose, onSecret,
}: {
  endpoint: WebhookEndpoint | null
  onClose: () => void
  onSecret: (name: string, secret: string) => void
}) {
  const s = useStyles()
  const isNew = endpoint === null
  const create = useCreateWebhookEndpoint()
  const update = useUpdateWebhookEndpoint()
  const { data: accountsPage } = useAccounts({ kind: undefined, role: 'Service' })

  const [name, setName] = useState(endpoint?.name ?? '')
  const [url, setUrl] = useState(endpoint?.url ?? '')
  const [enabled, setEnabled] = useState(endpoint?.enabled ?? true)
  const [events, setEvents] = useState<WebhookEventKind[]>(endpoint?.events ?? [])
  const [serviceId, setServiceId] = useState<string>(endpoint?.serviceAccountId ?? '')
  const [description, setDescription] = useState(endpoint?.description ?? '')
  const [generateSecret, setGenerateSecret] = useState(isNew)
  const [error, setError] = useState<string | null>(null)

  const services = (accountsPage?.items ?? []).filter(a => !a.isDeleted)
  const selectedService = services.find(a => a.id === serviceId)

  function toggleEvent(kind: WebhookEventKind, on: boolean) {
    setEvents(prev => on ? [...new Set([...prev, kind])] : prev.filter(k => k !== kind))
  }

  async function onSave() {
    setError(null)
    if (!name.trim()) { setError('A name is required.'); return }
    if (!/^https?:\/\//i.test(url.trim())) { setError('Enter an absolute http(s) URL.'); return }
    if (events.length === 0) { setError('Select at least one event to subscribe to.'); return }

    const common = {
      name: name.trim(),
      url: url.trim(),
      enabled,
      events,
      serviceAccountId: serviceId || null,
      description: description.trim() || null,
    }
    try {
      if (isNew) {
        const res = await create.mutateAsync({ ...common, generateSecret })
        if (res.secret) onSecret(res.endpoint.name, res.secret)
      } else {
        await update.mutateAsync({ id: endpoint!.id, req: common })
      }
      onClose()
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  const pending = create.isPending || update.isPending

  return (
    <div className={s.drawerForm}>
      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}

      <Field label="Name" required>
        <Input value={name} onChange={(_, d) => setName(d.value)} placeholder="Teams – submissions channel" />
      </Field>

      <Field label="Destination URL" required hint="The signed POST is sent here. Must be an absolute http(s) URL.">
        <Input value={url} onChange={(_, d) => setUrl(d.value)} placeholder="https://example.org/hooks/ingest" />
      </Field>

      <Switch label="Enabled" checked={enabled} onChange={(_, d) => setEnabled(d.checked)} />

      <Field label="Events" required>
        <div className={s.eventList}>
          {EVENTS.map(ev => (
            <div key={ev.kind}>
              <Checkbox
                label={`${ev.label} (${ev.wire})`}
                checked={events.includes(ev.kind)}
                onChange={(_, d) => toggleEvent(ev.kind, !!d.checked)}
              />
              <div className={s.eventHelp}>{ev.desc}</div>
            </div>
          ))}
        </div>
      </Field>

      <Field label="Only for service" hint="Limit deliveries to one service account. Leave blank to fire for every service.">
        <Dropdown
          placeholder="All services"
          selectedOptions={serviceId ? [serviceId] : []}
          value={selectedService ? (selectedService.label || selectedService.name) : 'All services'}
          onOptionSelect={(_, d) => setServiceId(d.optionValue === ALL ? '' : (d.optionValue ?? ''))}
        >
          <Option value={ALL}>All services</Option>
          {services.map(a => (
            <Option key={a.id} value={a.id} text={a.label || a.name}>{a.label || a.name}</Option>
          ))}
        </Dropdown>
      </Field>

      <Field label="Description" hint="Optional note shown only in this admin list.">
        <Textarea value={description} onChange={(_, d) => setDescription(d.value)} rows={2} resize="vertical" />
      </Field>

      {isNew && (
        <Checkbox
          label="Generate a signing secret (HMAC-SHA256). Shown once after saving."
          checked={generateSecret}
          onChange={(_, d) => setGenerateSecret(!!d.checked)}
        />
      )}

      <div className={s.actions}>
        <Button appearance="primary" disabled={pending} onClick={onSave}>
          {pending ? 'Saving…' : isNew ? 'Create endpoint' : 'Save changes'}
        </Button>
        <Button appearance="secondary" disabled={pending} onClick={onClose}>Cancel</Button>
      </div>
    </div>
  )
}

// --- One-time secret reveal dialog --------------------------------------------------------

function SecretDialog({ reveal, onClose }: { reveal: { name: string; secret: string } | null; onClose: () => void }) {
  const s = useStyles()
  const [copied, setCopied] = useState(false)

  async function copy() {
    try {
      await navigator.clipboard.writeText(reveal!.secret)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch { /* clipboard may be blocked; the value is selectable above */ }
  }

  return (
    <Dialog open={!!reveal} onOpenChange={(_, d) => { if (!d.open) { setCopied(false); onClose() } }}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Signing secret for “{reveal?.name}”</DialogTitle>
          <DialogContent>
            <p>
              Copy this now — it is shown <strong>once</strong> and cannot be retrieved later.
              Use it to verify the <code>X-Ingest-Signature</code> header on each delivery.
            </p>
            <div className={s.secretBox}>{reveal?.secret}</div>
          </DialogContent>
          <DialogActions>
            <Button appearance="primary" icon={<Copy20Regular />} onClick={copy}>
              {copied ? 'Copied' : 'Copy secret'}
            </Button>
            <Button appearance="secondary" onClick={() => { setCopied(false); onClose() }}>Done</Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

