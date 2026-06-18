import { useMemo, useState } from 'react'
import {
  Badge, Body1, Button, Card, Checkbox, Dropdown, Drawer, DrawerBody, Field, Input,
  Menu, MenuButton, MenuItem, MenuList, MenuPopover, MenuTrigger,
  MessageBarBody, Option, Radio, RadioGroup, Spinner, Switch,
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow, Title3, Tooltip,
  makeStyles, tokens,
} from '@fluentui/react-components'
import {
  Add20Regular, ArrowClockwise20Regular, Delete20Regular, Edit20Regular, MoreHorizontal20Regular,
  PauseCircle20Regular, PlayCircle20Regular, Play20Regular, Send20Regular,
} from '@fluentui/react-icons'
import { AutoScrollMessageBar } from './AutoScrollMessageBar'
import { DrawerHeaderWithClose } from './DrawerHeaderWithClose'
import { GridMessageRow } from './GridPager'
import { RowActions } from './RowActions'
import { clickableRowProps } from '../utils/a11y'
import { confirmDelete } from '../utils/confirm'
import { formatApiError } from '../api/client'
import {
  useAccounts, useSchemas, useCapabilities,
  useIntegrations, useCreateIntegration, useUpdateIntegration, useDeleteIntegration,
  useRunIntegration, useSendIntegrationTest,
  useTeamsConnection, useUpdateTeamsConnection, useTestTeamsConnection,
} from '../api/hooks'
import type {
  Account, Integration, IntegrationFrequency, IntegrationRequest, TeamsConnection, TeamsTargetKind, Weekday,
} from '../api/types'

const WEEKDAYS: Weekday[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']

const FREQUENCIES: { value: IntegrationFrequency; label: string }[] = [
  { value: 'Daily', label: 'Daily' },
  { value: 'Weekly', label: 'Weekly' },
  { value: 'Monthly', label: 'Monthly' },
  { value: 'Quarterly', label: 'Quarterly' },
  { value: 'SemiAnnually', label: 'Semi-annually' },
  { value: 'Yearly', label: 'Yearly' },
]

const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

const frequencyLabel = (f: IntegrationFrequency): string => FREQUENCIES.find(x => x.value === f)?.label ?? f

/** The special day-of-month value used by the dropdown to mean "last day of the month". */
const LAST_DAY = 'last'

/** Day-of-month options offered in the editor: 1-31 plus a "Last day" sentinel. */
const DAY_OF_MONTH_OPTIONS = [...Array.from({ length: 31 }, (_, i) => String(i + 1)), LAST_DAY]

function ordinal(n: number): string {
  const s = ['th', 'st', 'nd', 'rd']
  const v = n % 100
  return n + (s[(v - 20) % 10] || s[v] || s[0])
}

function dayOfMonthLabel(lastDay: boolean, day: number): string {
  return lastDay ? 'the last day' : ordinal(day)
}

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  card: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '20px' },
  cardNarrow: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '20px' },
  sectionTitle: { display: 'block', marginBottom: '2px' },
  help: { color: tokens.colorNeutralForeground3 },
  titleRow: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px' },
  headerActions: { display: 'flex', gap: '8px', alignItems: 'center' },
  actions: { display: 'flex', gap: '12px', flexWrap: 'wrap', marginTop: '4px' },
  row: { display: 'flex', gap: '12px', flexWrap: 'wrap' },
  grow: { flex: 1, minWidth: '220px' },
  table: { tableLayout: 'fixed', width: '100%' },
  tableRow: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  rowClickable: {
    cursor: 'pointer',
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '-2px' },
  },
  truncate: { display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  muted: { color: tokens.colorNeutralForeground3 },
  cellTrunc: { maxWidth: 0 },
  colTarget: { width: '200px' },
  colSchedule: { width: '150px' },
  colStatus: { width: '100px' },
  colActions: { width: '52px' },
  drawer: { width: 'max(600px, 46vw)' },
  drawerForm: { display: 'flex', flexDirection: 'column', gap: '14px' },
})

// --- Teams connection (bot credentials) ---------------------------------------------------

/**
 * "Teams connection" settings subpage. Holds the Microsoft Entra bot app registration credentials
 * used to send proactive Adaptive Cards. Stored in the database; the bot secret is encrypted at
 * rest and never returned. A "Test connection" button verifies the credentials against Entra.
 */
export function TeamsConnectionSection() {
  const { data, isLoading } = useTeamsConnection()
  if (isLoading || !data) return <Spinner label="Loading…" />
  return <TeamsConnectionForm initial={data} key={(data.appId ?? '') + '|' + (data.tenantId ?? '')} />
}

function TeamsConnectionForm({ initial }: { initial: TeamsConnection }) {
  const s = useStyles()
  const { has } = useCapabilities()
  const canManage = has('integrations:manage')
  const update = useUpdateTeamsConnection()
  const test = useTestTeamsConnection()

  const [appId, setAppId] = useState(initial.appId ?? '')
  const [tenantId, setTenantId] = useState(initial.tenantId ?? '')
  const [singleTenant, setSingleTenant] = useState(initial.singleTenant)
  const [changePassword, setChangePassword] = useState(false)
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [testResult, setTestResult] = useState<{ ok: boolean; text: string } | null>(null)

  async function onSave() {
    setError(null); setSaved(false); setTestResult(null)
    try {
      await update.mutateAsync({
        appId: appId.trim() || null,
        tenantId: tenantId.trim() || null,
        singleTenant,
        updatePassword: changePassword,
        password: changePassword ? password : null,
      })
      setSaved(true)
      setChangePassword(false)
      setPassword('')
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  async function onTest() {
    setError(null); setSaved(false); setTestResult(null)
    try {
      const r = await test.mutateAsync()
      setTestResult(r.ok
        ? { ok: true, text: 'Connection succeeded — credentials are valid.' }
        : { ok: false, text: r.error || 'Connection failed.' })
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  return (
    <Card className={s.cardNarrow}>
      <div>
        <Title3 className={s.sectionTitle}>Microsoft Teams connection</Title3>
        <Body1 className={s.help}>
          The bot app registration used to send prompts to Teams. Stored in the database; the bot
          secret is encrypted at rest and never shown again. See the setup guide for how to register
          the bot in Azure.{' '}
          {initial.isConfigured
            ? <Badge appearance="tint" color="success">Configured</Badge>
            : <Badge appearance="tint" color="warning">Not configured</Badge>}
        </Body1>
      </div>

      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}
      {saved && <AutoScrollMessageBar intent="success"><MessageBarBody>Connection saved.</MessageBarBody></AutoScrollMessageBar>}
      {testResult && (
        <AutoScrollMessageBar intent={testResult.ok ? 'success' : 'error'}>
          <MessageBarBody>{testResult.text}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Field label="App (client) ID" required className={s.grow}>
        <Input value={appId} onChange={(_, d) => setAppId(d.value)} placeholder="00000000-0000-0000-0000-000000000000" disabled={!canManage} />
      </Field>

      <Field label="Tenant ID" className={s.grow} hint="Leave blank for a multi-tenant bot.">
        <Input value={tenantId} onChange={(_, d) => setTenantId(d.value)} placeholder="(blank = multi-tenant)" disabled={!canManage} />
      </Field>

      <Switch
        label="Single-tenant app registration"
        checked={singleTenant}
        onChange={(_, d) => setSingleTenant(d.checked)}
        disabled={!canManage}
      />

      <Checkbox
        label={initial.hasPassword ? 'Change the stored bot secret' : 'Set the bot secret'}
        checked={changePassword}
        onChange={(_, d) => setChangePassword(!!d.checked)}
        disabled={!canManage}
      />
      {changePassword && (
        <Field label="Bot secret (client secret)" hint="Leave blank to clear the stored secret.">
          <Input type="password" value={password} onChange={(_, d) => setPassword(d.value)} />
        </Field>
      )}

      {canManage && (
        <div className={s.actions}>
          <Button appearance="primary" disabled={update.isPending} onClick={onSave}>
            {update.isPending ? 'Saving…' : 'Save'}
          </Button>
          <Button disabled={test.isPending || !initial.isConfigured} onClick={onTest}>
            {test.isPending ? 'Testing…' : 'Test connection'}
          </Button>
        </div>
      )}
    </Card>
  )
}

// --- Integrations list --------------------------------------------------------------------

/** Working copy of an integration while it's open in the drawer. "All" is a checkbox that clears the id list. */
interface IntegrationDraft {
  id?: string
  label: string
  enabled: boolean
  allServices: boolean
  serviceIds: string[]
  allSchemas: boolean
  schemaIds: string[]
  targetKind: TeamsTargetKind
  targetId: string
  displayName: string
  frequency: IntegrationFrequency
  days: Weekday[]
  dayOfMonth: number
  lastDayOfMonth: boolean
  anchorMonth: number
  hourUtc: number
  minuteUtc: number
}

function toDraft(i: Integration): IntegrationDraft {
  return {
    id: i.id,
    label: i.label ?? '',
    enabled: i.enabled,
    allServices: i.serviceIds.length === 0,
    serviceIds: i.serviceIds,
    allSchemas: i.schemaIds.length === 0,
    schemaIds: i.schemaIds,
    targetKind: i.teams.kind,
    targetId: i.teams.targetId,
    displayName: i.teams.displayName ?? '',
    frequency: i.schedule.frequency,
    days: i.schedule.days,
    dayOfMonth: i.schedule.dayOfMonth || 1,
    lastDayOfMonth: i.schedule.lastDayOfMonth,
    anchorMonth: i.schedule.anchorMonth || 1,
    hourUtc: i.schedule.hourUtc,
    minuteUtc: i.schedule.minuteUtc,
  }
}

function emptyDraft(): IntegrationDraft {
  return {
    label: '',
    enabled: true,
    allServices: true,
    serviceIds: [],
    allSchemas: true,
    schemaIds: [],
    targetKind: 'User',
    targetId: '',
    displayName: '',
    frequency: 'Daily',
    days: [],
    dayOfMonth: 1,
    lastDayOfMonth: false,
    anchorMonth: 1,
    hourUtc: 8,
    minuteUtc: 0,
  }
}

function draftToRequest(d: IntegrationDraft): IntegrationRequest {
  return {
    label: d.label.trim() || null,
    enabled: d.enabled,
    kind: 'MicrosoftTeams',
    serviceIds: d.allServices ? [] : d.serviceIds,
    schemaIds: d.allSchemas ? [] : d.schemaIds,
    schedule: {
      frequency: d.frequency,
      days: d.frequency === 'Weekly' ? d.days : [],
      dayOfMonth: d.dayOfMonth,
      lastDayOfMonth: d.lastDayOfMonth,
      anchorMonth: d.anchorMonth,
      hourUtc: d.hourUtc,
      minuteUtc: d.minuteUtc,
    },
    teams: { kind: d.targetKind, targetId: d.targetId.trim(), displayName: d.displayName.trim() || null },
  }
}

function pad2(n: number): string { return String(n).padStart(2, '0') }

/**
 * "Integrations" settings subpage. Lists configured integrations (Microsoft Teams today): each one
 * prompts a chosen user or channel for outstanding samples across a scoped set of services and
 * schemas, on a daily schedule (or on demand). The bot connection is configured on its own subpage.
 */
export function IntegrationsSection() {
  const s = useStyles()
  const { has } = useCapabilities()
  const canManage = has('integrations:manage')
  const { data: integrations, isLoading, refetch } = useIntegrations()
  const { data: connection } = useTeamsConnection()
  const { data: accountsPage } = useAccounts({ role: 'Service' })
  const { data: schemasPage } = useSchemas({ pageSize: 200 })
  const del = useDeleteIntegration()
  const update = useUpdateIntegration()
  const run = useRunIntegration()
  const sendTest = useSendIntegrationTest()

  const [editing, setEditing] = useState<IntegrationDraft | null>(null)
  const [banner, setBanner] = useState<{ intent: 'success' | 'error'; text: string } | null>(null)

  const services = useMemo(() => (accountsPage?.items ?? []).filter(a => !a.isDeleted), [accountsPage])
  const servicesById = useMemo(() => new Map(services.map(a => [a.id, a])), [services])
  const schemas = useMemo(() => (schemasPage?.items ?? []), [schemasPage])
  const schemasById = useMemo(() => new Map(schemas.map(sc => [sc.id, sc])), [schemas])

  function serviceSummary(i: Integration): string {
    if (i.serviceIds.length === 0) return 'All services'
    return i.serviceIds.map(id => servicesById.get(id)?.label || servicesById.get(id)?.name || '(removed)').join(', ')
  }
  function schemaSummary(i: Integration): string {
    if (i.schemaIds.length === 0) return 'All schemas'
    return i.schemaIds.map(id => schemasById.get(id)?.label || schemasById.get(id)?.name || '(removed)').join(', ')
  }
  function targetSummary(i: Integration): string {
    const who = i.teams.displayName || i.teams.targetId || '(unset)'
    return `${i.teams.kind === 'Channel' ? 'Channel' : 'User'}: ${who}`
  }
  function scheduleSummary(i: Integration): string {
    const s = i.schedule
    const time = `${pad2(s.hourUtc)}:${pad2(s.minuteUtc)} UTC`
    const day = dayOfMonthLabel(s.lastDayOfMonth, s.dayOfMonth)
    switch (s.frequency) {
      case 'Weekly':
        return s.days.length === 0
          ? `Weekly (every day), ${time}`
          : `${s.days.map(d => d.slice(0, 3)).join(', ')}, ${time}`
      case 'Monthly':
        return `Monthly on ${day}, ${time}`
      case 'Quarterly':
        return `Quarterly from ${MONTHS[Math.min(Math.max(s.anchorMonth, 1), 12) - 1]} on ${day}, ${time}`
      case 'SemiAnnually':
        return `Semi-annually from ${MONTHS[Math.min(Math.max(s.anchorMonth, 1), 12) - 1]} on ${day}, ${time}`
      case 'Yearly':
        return `Yearly in ${MONTHS[Math.min(Math.max(s.anchorMonth, 1), 12) - 1]} on ${day}, ${time}`
      default:
        return `Daily, ${time}`
    }
  }

  async function onDelete(i: Integration) {
    if (!confirmDelete('integration', i.label || targetSummary(i))) return
    setBanner(null)
    try {
      await del.mutateAsync(i.id)
      setBanner({ intent: 'success', text: 'Integration deleted.' })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  async function onToggleEnabled(i: Integration) {
    setBanner(null)
    const req = draftToRequest(toDraft(i))
    req.enabled = !i.enabled
    try {
      await update.mutateAsync({ id: i.id, req })
      setBanner({ intent: 'success', text: i.enabled ? 'Integration disabled.' : 'Integration enabled.' })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  async function onRun(i: Integration) {
    setBanner(null)
    try {
      const r = await run.mutateAsync(i.id)
      setBanner({ intent: 'success', text: `Run complete — prompted ${r.prompted}, skipped ${r.skipped}.` })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  async function onSendTest(i: Integration) {
    setBanner(null)
    try {
      await sendTest.mutateAsync(i.id)
      setBanner({ intent: 'success', text: 'Test prompt enqueued.' })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  const items = integrations ?? []
  const connectionReady = connection?.isConfigured === true

  return (
    <div className={s.root}>
      <Card className={s.card}>
        <div className={s.titleRow}>
          <Title3 className={s.sectionTitle}>Integrations</Title3>
          <div className={s.headerActions}>
            {canManage && (
              <Button appearance="primary" icon={<Add20Regular />} onClick={() => setEditing(emptyDraft())}>
                Add integration
              </Button>
            )}
            <Menu>
              <MenuTrigger disableButtonEnhancement>
                <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label="More actions" />
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  <MenuItem icon={<ArrowClockwise20Regular />} onClick={() => refetch()}>Refresh</MenuItem>
                </MenuList>
              </MenuPopover>
            </Menu>
          </div>
        </div>
        <Body1 className={s.help}>
          Each integration prompts a Teams user or channel for the samples that are still outstanding
          across the services and schemas it covers. Prompts run on a daily schedule, or on demand
          with “Run now”. Disabled and hidden fields are omitted; warnings are surfaced inline.
        </Body1>

        {connection && !connectionReady && (
          <AutoScrollMessageBar intent="warning">
            <MessageBarBody>
              The Microsoft Teams connection isn’t configured yet — set the bot credentials on the
              “Teams connection” subpage before integrations can send prompts.
            </MessageBarBody>
          </AutoScrollMessageBar>
        )}

        {banner && (
          <AutoScrollMessageBar intent={banner.intent}>
            <MessageBarBody>{banner.text}</MessageBarBody>
          </AutoScrollMessageBar>
        )}

        <Table size="small" className={s.table}>
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Label</TableHeaderCell>
              <TableHeaderCell className={s.colTarget}>Target</TableHeaderCell>
              <TableHeaderCell>Services</TableHeaderCell>
              <TableHeaderCell>Schemas</TableHeaderCell>
              <TableHeaderCell className={s.colSchedule}>Schedule</TableHeaderCell>
              <TableHeaderCell className={s.colStatus}>Status</TableHeaderCell>
              <TableHeaderCell className={s.colActions} aria-label="Actions" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <GridMessageRow colSpan={7}>Loading…</GridMessageRow>}
            {!isLoading && items.length === 0 && (
              <GridMessageRow colSpan={7}>No integrations yet{canManage ? ' — click “Add integration” to create one.' : '.'}</GridMessageRow>
            )}
            {items.map(i => (
              <TableRow
                key={i.id}
                className={`${s.tableRow} ${s.rowClickable}`}
                {...clickableRowProps(() => canManage && setEditing(toDraft(i)), `Edit integration ${i.label || targetSummary(i)}`)}
              >
                <TableCell className={s.cellTrunc}>
                  {i.label
                    ? <strong className={s.truncate}>{i.label}</strong>
                    : <span className={`${s.truncate} ${s.muted}`}>—</span>}
                </TableCell>
                <TableCell className={s.colTarget}>
                  <Tooltip content={targetSummary(i)} relationship="label">
                    <span className={s.truncate}>{targetSummary(i)}</span>
                  </Tooltip>
                </TableCell>
                <TableCell className={s.cellTrunc}>
                  <Tooltip content={serviceSummary(i)} relationship="label">
                    <span className={s.truncate}>{serviceSummary(i)}</span>
                  </Tooltip>
                </TableCell>
                <TableCell className={s.cellTrunc}>
                  <Tooltip content={schemaSummary(i)} relationship="label">
                    <span className={s.truncate}>{schemaSummary(i)}</span>
                  </Tooltip>
                </TableCell>
                <TableCell className={s.colSchedule}>
                  <span className={s.truncate}>{scheduleSummary(i)}</span>
                </TableCell>
                <TableCell className={s.colStatus}>
                  <Badge appearance="outline" color={i.enabled ? 'success' : 'informative'}>
                    {i.enabled ? 'Enabled' : 'Disabled'}
                  </Badge>
                </TableCell>
                <TableCell className={s.colActions} onClick={ev => ev.stopPropagation()}>
                  {canManage && (
                    <RowActions
                      ariaLabel={`Actions for integration ${i.label || targetSummary(i)}`}
                      actions={[
                        { key: 'edit', label: 'Edit', icon: <Edit20Regular />, onClick: () => setEditing(toDraft(i)) },
                        { key: 'run', label: 'Run now', icon: <Play20Regular />, disabled: run.isPending, onClick: () => onRun(i) },
                        { key: 'test', label: 'Send test', icon: <Send20Regular />, disabled: sendTest.isPending, onClick: () => onSendTest(i) },
                        {
                          key: 'toggle',
                          label: i.enabled ? 'Disable' : 'Enable',
                          icon: i.enabled ? <PauseCircle20Regular /> : <PlayCircle20Regular />,
                          disabled: update.isPending,
                          onClick: () => onToggleEnabled(i),
                        },
                        { key: 'delete', label: 'Delete', icon: <Delete20Regular />, destructive: true, onClick: () => onDelete(i) },
                      ]}
                    />
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
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
          title={editing?.id ? 'Edit integration' : 'Add integration'}
          onClose={() => setEditing(null)}
        />
        <DrawerBody>
          {editing && (
            <IntegrationEditor
              key={editing.id ?? 'new'}
              draft={editing}
              services={services}
              schemas={schemas.map(sc => ({ id: sc.id, label: sc.label || sc.name }))}
              onClose={() => setEditing(null)}
              onSaved={text => { setEditing(null); setBanner({ intent: 'success', text }) }}
            />
          )}
        </DrawerBody>
      </Drawer>
    </div>
  )
}

// --- Integration create / edit form -------------------------------------------------------

function IntegrationEditor({
  draft, services, schemas, onClose, onSaved,
}: {
  draft: IntegrationDraft
  services: Account[]
  schemas: { id: string; label: string }[]
  onClose: () => void
  onSaved: (text: string) => void
}) {
  const s = useStyles()
  const isNew = !draft.id
  const create = useCreateIntegration()
  const update = useUpdateIntegration()

  const [label, setLabel] = useState(draft.label)
  const [enabled, setEnabled] = useState(draft.enabled)
  const [allServices, setAllServices] = useState(draft.allServices)
  const [serviceIds, setServiceIds] = useState<string[]>(draft.serviceIds)
  const [allSchemas, setAllSchemas] = useState(draft.allSchemas)
  const [schemaIds, setSchemaIds] = useState<string[]>(draft.schemaIds)
  const [targetKind, setTargetKind] = useState<TeamsTargetKind>(draft.targetKind)
  const [targetId, setTargetId] = useState(draft.targetId)
  const [displayName, setDisplayName] = useState(draft.displayName)
  const [frequency, setFrequency] = useState<IntegrationFrequency>(draft.frequency)
  const [days, setDays] = useState<Weekday[]>(draft.days)
  const [lastDayOfMonth, setLastDayOfMonth] = useState(draft.lastDayOfMonth)
  const [dayOfMonth, setDayOfMonth] = useState(draft.dayOfMonth)
  const [anchorMonth, setAnchorMonth] = useState(draft.anchorMonth)
  const [hourUtc, setHourUtc] = useState(String(draft.hourUtc))
  const [minuteUtc, setMinuteUtc] = useState(String(draft.minuteUtc))
  const [error, setError] = useState<string | null>(null)

  const usesDayOfMonth = frequency === 'Monthly' || frequency === 'Quarterly' || frequency === 'SemiAnnually' || frequency === 'Yearly'
  const usesAnchorMonth = frequency === 'Quarterly' || frequency === 'SemiAnnually' || frequency === 'Yearly'
  const dayOfMonthValue = lastDayOfMonth ? LAST_DAY : String(dayOfMonth)

  const servicesById = useMemo(() => new Map(services.map(a => [a.id, a])), [services])
  const schemasById = useMemo(() => new Map(schemas.map(sc => [sc.id, sc])), [schemas])

  async function onSave() {
    setError(null)
    if (!targetId.trim()) { setError('Enter the target user or channel id.'); return }
    if (!allServices && serviceIds.length === 0) { setError('Pick at least one service, or choose “All services”.'); return }
    if (!allSchemas && schemaIds.length === 0) { setError('Pick at least one schema, or choose “All schemas”.'); return }
    const hour = Number(hourUtc)
    const minute = Number(minuteUtc)
    if (!Number.isInteger(hour) || hour < 0 || hour > 23) { setError('Hour must be between 0 and 23.'); return }
    if (!Number.isInteger(minute) || minute < 0 || minute > 59) { setError('Minute must be between 0 and 59.'); return }

    const req = draftToRequest({
      ...draft,
      label, enabled, allServices, serviceIds, allSchemas, schemaIds,
      targetKind, targetId, displayName,
      frequency, days, dayOfMonth, lastDayOfMonth, anchorMonth,
      hourUtc: hour, minuteUtc: minute,
    })
    try {
      if (isNew) await create.mutateAsync(req)
      else await update.mutateAsync({ id: draft.id!, req })
      onSaved(isNew ? 'Integration created.' : 'Integration saved.')
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  const pending = create.isPending || update.isPending

  return (
    <div className={s.drawerForm}>
      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}

      <Field label="Label" hint="Optional name shown only in this list.">
        <Input value={label} onChange={(_, d) => setLabel(d.value)} placeholder="e.g. Daily nudge for the ops channel" />
      </Field>

      <Switch label="Enabled" checked={enabled} onChange={(_, d) => setEnabled(d.checked)} />

      <Field label="Send to">
        <RadioGroup layout="horizontal" value={targetKind} onChange={(_, d) => setTargetKind(d.value as TeamsTargetKind)}>
          <Radio value="User" label="A user" />
          <Radio value="Channel" label="A channel" />
        </RadioGroup>
      </Field>

      <Field
        label={targetKind === 'Channel' ? 'Channel id' : 'User id (Entra object id, UPN, or email)'}
        required
        hint="See the setup guide for how to find this id."
      >
        <Input value={targetId} onChange={(_, d) => setTargetId(d.value)} placeholder={targetKind === 'Channel' ? '19:...@thread.tacv2' : 'user@example.org'} />
      </Field>

      <Field label="Display name" hint="Optional friendly label for this target.">
        <Input value={displayName} onChange={(_, d) => setDisplayName(d.value)} placeholder="e.g. Ops team channel" />
      </Field>

      <Field label="Services">
        <Checkbox label="All services" checked={allServices} onChange={(_, d) => setAllServices(!!d.checked)} />
        {!allServices && (
          <Dropdown
            multiselect
            placeholder="Select services"
            selectedOptions={serviceIds}
            value={serviceIds.map(id => servicesById.get(id)?.label || servicesById.get(id)?.name || id).join(', ')}
            onOptionSelect={(_, d) => setServiceIds(d.selectedOptions)}
          >
            {services.map(a => (
              <Option key={a.id} value={a.id} text={a.label || a.name}>{a.label || a.name}</Option>
            ))}
          </Dropdown>
        )}
      </Field>

      <Field label="Schemas">
        <Checkbox label="All schemas" checked={allSchemas} onChange={(_, d) => setAllSchemas(!!d.checked)} />
        {!allSchemas && (
          <Dropdown
            multiselect
            placeholder="Select schemas"
            selectedOptions={schemaIds}
            value={schemaIds.map(id => schemasById.get(id)?.label || id).join(', ')}
            onOptionSelect={(_, d) => setSchemaIds(d.selectedOptions)}
          >
            {schemas.map(sc => (
              <Option key={sc.id} value={sc.id} text={sc.label}>{sc.label}</Option>
            ))}
          </Dropdown>
        )}
      </Field>

      <Field label="Frequency" hint="How often the pass looks for outstanding values. It also runs on demand with “Run now”.">
        <Dropdown
          value={frequencyLabel(frequency)}
          selectedOptions={[frequency]}
          onOptionSelect={(_, d) => setFrequency(d.optionValue as IntegrationFrequency)}
        >
          {FREQUENCIES.map(f => (
            <Option key={f.value} value={f.value} text={f.label}>{f.label}</Option>
          ))}
        </Dropdown>
      </Field>

      {frequency === 'Weekly' && (
        <Field label="Days of the week" hint="Leave empty to run every day.">
          <Dropdown
            multiselect
            placeholder="Every day"
            selectedOptions={days}
            value={days.length === 0 ? 'Every day' : days.join(', ')}
            onOptionSelect={(_, d) => setDays(d.selectedOptions as Weekday[])}
          >
            {WEEKDAYS.map(d => (
              <Option key={d} value={d} text={d}>{d}</Option>
            ))}
          </Dropdown>
        </Field>
      )}

      {usesAnchorMonth && (
        <Field label="Anchor month" hint="The period repeats from this month (e.g. quarterly from February = Feb, May, Aug, Nov).">
          <Dropdown
            value={MONTHS[Math.min(Math.max(anchorMonth, 1), 12) - 1]}
            selectedOptions={[String(anchorMonth)]}
            onOptionSelect={(_, d) => setAnchorMonth(Number(d.optionValue))}
          >
            {MONTHS.map((m, idx) => (
              <Option key={m} value={String(idx + 1)} text={m}>{m}</Option>
            ))}
          </Dropdown>
        </Field>
      )}

      {usesDayOfMonth && (
        <Field label="Day of the month" hint="Runs on (or after) this day within the period; “Last day” runs on the final day of the month.">
          <Dropdown
            value={lastDayOfMonth ? 'Last day' : ordinal(dayOfMonth)}
            selectedOptions={[dayOfMonthValue]}
            onOptionSelect={(_, d) => {
              if (d.optionValue === LAST_DAY) { setLastDayOfMonth(true) }
              else { setLastDayOfMonth(false); setDayOfMonth(Number(d.optionValue)) }
            }}
          >
            {DAY_OF_MONTH_OPTIONS.map(opt => (
              <Option key={opt} value={opt} text={opt === LAST_DAY ? 'Last day' : ordinal(Number(opt))}>
                {opt === LAST_DAY ? 'Last day' : ordinal(Number(opt))}
              </Option>
            ))}
          </Dropdown>
        </Field>
      )}

      <div className={s.row}>
        <Field label="Hour (UTC)" required>
          <Input type="number" value={hourUtc} onChange={(_, d) => setHourUtc(d.value)} style={{ width: '110px' }} />
        </Field>
        <Field label="Minute (UTC)" required>
          <Input type="number" value={minuteUtc} onChange={(_, d) => setMinuteUtc(d.value)} style={{ width: '110px' }} />
        </Field>
      </div>

      <div className={s.actions}>
        <Button appearance="primary" disabled={pending} onClick={onSave}>
          {pending ? 'Saving…' : isNew ? 'Create integration' : 'Save changes'}
        </Button>
        <Button appearance="secondary" disabled={pending} onClick={onClose}>Cancel</Button>
      </div>
    </div>
  )
}
