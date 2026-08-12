import { useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import {
  Badge, Body1, Button, Card, Checkbox, Dialog, DialogActions, DialogBody, DialogContent,
  DialogSurface, DialogTitle, Dropdown, Drawer, DrawerBody, Field, Input,
  Menu, MenuButton, MenuItem, MenuList, MenuPopover, MenuTrigger, MessageBarBody,
  Option, Switch, Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow,
  Textarea, Title3, Tooltip, makeStyles, tokens,
} from '@fluentui/react-components'
import {
  Add20Regular, ArrowClockwise20Regular, Copy20Regular, Send20Regular, KeyReset20Regular,
  Delete20Regular, MoreHorizontal20Regular,
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
const EVENTS: { kind: WebhookEventKind; wire: string }[] = [
  { kind: 'SubmissionAccepted', wire: 'submission.accepted' },
  { kind: 'SubmissionWarnings', wire: 'submission.warnings' },
  { kind: 'WindowUpcoming', wire: 'window.upcoming' },
  { kind: 'WindowMissed', wire: 'window.missed' },
  { kind: 'SubmissionPendingApproval', wire: 'submission.pending_approval' },
  { kind: 'SubmissionApproved', wire: 'submission.approved' },
  { kind: 'SubmissionRejected', wire: 'submission.rejected' },
]

const ALL = '__all__'

const useStyles = makeStyles({
  card: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '20px' },
  sectionTitle: { display: 'block', marginBottom: '2px' },
  help: { color: tokens.colorNeutralForeground3 },
  titleRow: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px' },
  headerActions: { display: 'flex', gap: '8px', alignItems: 'center' },
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
  const { t } = useTranslation()
  const { data: endpoints, isLoading, refetch } = useWebhookEndpoints()
  const [editing, setEditing] = useState<WebhookEndpoint | 'new' | null>(null)
  // Plaintext secret to reveal once after create/rotate (cleared when the dialog closes).
  const [revealed, setRevealed] = useState<{ name: string; secret: string } | null>(null)
  const [banner, setBanner] = useState<{ intent: 'success' | 'error'; text: string } | null>(null)

  const del = useDeleteWebhookEndpoint()
  const rotate = useRotateWebhookSecret()
  const test = useSendWebhookTest()

  async function onDelete(e: WebhookEndpoint) {
    if (!window.confirm(t('settings.webhooks.deleteConfirm', { name: e.name }))) return
    setBanner(null)
    try {
      await del.mutateAsync(e.id)
      setBanner({ intent: 'success', text: t('settings.webhooks.deleted', { name: e.name }) })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  async function onRotate(e: WebhookEndpoint) {
    if (!window.confirm(t('settings.webhooks.rotateConfirm', { name: e.name }))) return
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
      setBanner({ intent: 'success', text: t('settings.webhooks.testQueued', { name: e.name }) })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  const items = endpoints ?? []

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
      <Card className={s.card}>
        <div className={s.titleRow}>
          <Title3 className={s.sectionTitle}>{t('settings.webhooks.title')}</Title3>
          <div className={s.headerActions}>
            <Button appearance="primary" icon={<Add20Regular />} onClick={() => setEditing('new')}>
              {t('settings.webhooks.add')}
            </Button>
            <Menu>
              <MenuTrigger disableButtonEnhancement>
                <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label={t('settings.common.moreActions')} />
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  <MenuItem icon={<ArrowClockwise20Regular />} onClick={() => refetch()}>{t('settings.common.refresh')}</MenuItem>
                </MenuList>
              </MenuPopover>
            </Menu>
          </div>
        </div>
        <Body1 className={s.help}>
          {t('settings.webhooks.description')}
        </Body1>

        {banner && (
          <AutoScrollMessageBar intent={banner.intent}>
            <MessageBarBody>{banner.text}</MessageBarBody>
          </AutoScrollMessageBar>
        )}

        <Table size="small" className={s.table}>
          <TableHeader>
            <TableRow>
              <TableHeaderCell>{t('settings.webhooks.name')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.webhooks.url')}</TableHeaderCell>
              <TableHeaderCell className={s.colEvents}>{t('settings.webhooks.eventsLabel')}</TableHeaderCell>
              <TableHeaderCell className={s.colStatus}>{t('settings.common.status')}</TableHeaderCell>
              <TableHeaderCell className={s.colActions} aria-label={t('settings.common.actions')} />
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <GridMessageRow colSpan={5}>{t('settings.common.loading')}</GridMessageRow>}
            {!isLoading && items.length === 0 && (
              <GridMessageRow colSpan={5}>{t('settings.webhooks.empty')}</GridMessageRow>
            )}
            {items.map(e => (
              <TableRow
                key={e.id}
                className={`${s.row} ${s.rowClickable}`}
                {...clickableRowProps(() => setEditing(e), t('settings.webhooks.editAria', { name: e.name }))}
              >
                <TableCell className={s.cellTrunc}>
                  <strong className={s.truncate}>{e.name}</strong>
                  {!e.hasSecret && (
                    <Tooltip content={t('settings.webhooks.unsignedHint')} relationship="label">
                      <span className={s.truncate} style={{ color: tokens.colorPaletteDarkOrangeForeground1, fontSize: tokens.fontSizeBase200 }}>
                        {t('settings.webhooks.unsigned')}
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
                  <Tooltip content={e.events.map(wireName).join(', ') || t('settings.webhooks.noEvents')} relationship="label">
                    <span className={s.truncate}>{t('settings.webhooks.eventCount', { count: e.events.length })}</span>
                  </Tooltip>
                </TableCell>
                <TableCell className={s.colStatus}>
                  <Badge appearance="outline" color={e.enabled ? 'success' : 'informative'}>
                    {e.enabled ? t('settings.common.enabled') : t('settings.common.disabled')}
                  </Badge>
                </TableCell>
                <TableCell className={s.colActions} onClick={ev => ev.stopPropagation()}>
                  <RowActions
                    ariaLabel={t('settings.webhooks.actionsAria', { name: e.name })}
                    actions={[
                      { key: 'test', label: t('settings.integrations.sendTest'), icon: <Send20Regular />, onClick: () => onTest(e), disabled: test.isPending },
                      { key: 'rotate', label: e.hasSecret ? t('settings.webhooks.rotateSecret') : t('settings.webhooks.generateSecret'), icon: <KeyReset20Regular />, onClick: () => onRotate(e), disabled: rotate.isPending },
                      { key: 'delete', label: t('settings.common.delete'), icon: <Delete20Regular />, destructive: true, onClick: () => onDelete(e) },
                    ]}
                  />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <Body1 className={s.help}>
          <Trans
            i18nKey="settings.webhooks.deliveryHistory"
            components={{
              auditLink: <RouterLink to="/audit" />,
              sectionName: <strong />,
            }}
          />
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
          title={editing === 'new'
            ? t('settings.webhooks.addTitle')
            : t('settings.webhooks.editTitle', { name: editing?.name ?? '' })}
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
  const { t } = useTranslation()
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
    if (!name.trim()) { setError(t('settings.webhooks.nameRequired')); return }
    if (!/^https?:\/\//i.test(url.trim())) { setError(t('settings.webhooks.urlValidation')); return }
    if (events.length === 0) { setError(t('settings.webhooks.eventRequired')); return }

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

      <Field label={t('settings.webhooks.name')} required>
        <Input value={name} onChange={(_, d) => setName(d.value)} placeholder={t('settings.webhooks.namePlaceholder')} />
      </Field>

      <Field label={t('settings.webhooks.destinationUrl')} required hint={t('settings.webhooks.destinationUrlHint')}>
        <Input value={url} onChange={(_, d) => setUrl(d.value)} placeholder={t('settings.webhooks.urlPlaceholder')} />
      </Field>

      <Switch label={t('settings.common.enabled')} checked={enabled} onChange={(_, d) => setEnabled(d.checked)} />

      <Field label={t('settings.webhooks.eventsLabel')} required>
        <div className={s.eventList}>
          {EVENTS.map(ev => (
            <div key={ev.kind}>
              <Checkbox
                label={`${t(`settings.webhooks.events.${ev.kind}.label`)} (${ev.wire})`}
                checked={events.includes(ev.kind)}
                onChange={(_, d) => toggleEvent(ev.kind, !!d.checked)}
              />
              <div className={s.eventHelp}>{t(`settings.webhooks.events.${ev.kind}.description`)}</div>
            </div>
          ))}
        </div>
      </Field>

      <Field label={t('settings.webhooks.onlyForService')} hint={t('settings.webhooks.onlyForServiceHint')}>
        <Dropdown
          placeholder={t('settings.common.allServices')}
          selectedOptions={serviceId ? [serviceId] : []}
          value={selectedService ? (selectedService.label || selectedService.name) : t('settings.common.allServices')}
          onOptionSelect={(_, d) => setServiceId(d.optionValue === ALL ? '' : (d.optionValue ?? ''))}
        >
          <Option value={ALL}>{t('settings.common.allServices')}</Option>
          {services.map(a => (
            <Option key={a.id} value={a.id} text={a.label || a.name}>{a.label || a.name}</Option>
          ))}
        </Dropdown>
      </Field>

      <Field label={t('settings.webhooks.descriptionLabel')} hint={t('settings.webhooks.descriptionHint')}>
        <Textarea value={description} onChange={(_, d) => setDescription(d.value)} rows={2} resize="vertical" />
      </Field>

      {isNew && (
        <Checkbox
          label={t('settings.webhooks.generateOnCreate')}
          checked={generateSecret}
          onChange={(_, d) => setGenerateSecret(!!d.checked)}
        />
      )}

      <div className={s.actions}>
        <Button appearance="primary" disabled={pending} onClick={onSave}>
          {pending ? t('settings.common.saving') : isNew ? t('settings.webhooks.create') : t('settings.common.saveChanges')}
        </Button>
        <Button appearance="secondary" disabled={pending} onClick={onClose}>{t('settings.common.cancel')}</Button>
      </div>
    </div>
  )
}

// --- One-time secret reveal dialog --------------------------------------------------------

function SecretDialog({ reveal, onClose }: { reveal: { name: string; secret: string } | null; onClose: () => void }) {
  const s = useStyles()
  const { t } = useTranslation()
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
          <DialogTitle>{t('settings.webhooks.secretTitle', { name: reveal?.name })}</DialogTitle>
          <DialogContent>
            <p>
              {t('settings.webhooks.secretInstructions')}
            </p>
            <div className={s.secretBox}>{reveal?.secret}</div>
          </DialogContent>
          <DialogActions>
            <Button appearance="primary" icon={<Copy20Regular />} onClick={copy}>
              {copied ? t('settings.webhooks.copied') : t('settings.webhooks.copySecret')}
            </Button>
            <Button appearance="secondary" onClick={() => { setCopied(false); onClose() }}>{t('settings.common.done')}</Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

