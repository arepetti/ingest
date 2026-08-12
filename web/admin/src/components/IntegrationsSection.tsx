import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
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
import { formatApiError, localizeDiagnostic } from '../api/client'
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

const FREQUENCIES: IntegrationFrequency[] = ['Daily', 'Weekly', 'Monthly', 'Quarterly', 'SemiAnnually', 'Yearly']

const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

/** The special day-of-month value used by the dropdown to mean "last day of the month". */
const LAST_DAY = 'last'

/** Day-of-month options offered in the editor: 1-31 plus a "Last day" sentinel. */
const DAY_OF_MONTH_OPTIONS = [...Array.from({ length: 31 }, (_, i) => String(i + 1)), LAST_DAY]

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
  const { t } = useTranslation()
  const { data, isLoading } = useTeamsConnection()
  if (isLoading || !data) return <Spinner label={t('settings.common.loading')} />
  return <TeamsConnectionForm initial={data} key={(data.appId ?? '') + '|' + (data.tenantId ?? '')} />
}

function TeamsConnectionForm({ initial }: { initial: TeamsConnection }) {
  const s = useStyles()
  const { t } = useTranslation()
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
        ? { ok: true, text: t('settings.teamsConnection.testSucceeded') }
        : {
            ok: false,
            text: r.errorDetail
              ? localizeDiagnostic(r.errorDetail, r.error)
              : r.error || t('settings.teamsConnection.testFailed'),
          })
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  return (
    <Card className={s.cardNarrow}>
      <div>
        <Title3 className={s.sectionTitle}>{t('settings.teamsConnection.title')}</Title3>
        <Body1 className={s.help}>
          {t('settings.teamsConnection.description')}{' '}
          {initial.isConfigured
            ? <Badge appearance="tint" color="success">{t('settings.common.configured')}</Badge>
            : <Badge appearance="tint" color="warning">{t('settings.common.notConfigured')}</Badge>}
        </Body1>
      </div>

      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}
      {saved && <AutoScrollMessageBar intent="success"><MessageBarBody>{t('settings.teamsConnection.saved')}</MessageBarBody></AutoScrollMessageBar>}
      {testResult && (
        <AutoScrollMessageBar intent={testResult.ok ? 'success' : 'error'}>
          <MessageBarBody>{testResult.text}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Field label={t('settings.teamsConnection.appId')} required className={s.grow}>
        <Input value={appId} onChange={(_, d) => setAppId(d.value)} placeholder={t('settings.teamsConnection.appIdPlaceholder')} disabled={!canManage} />
      </Field>

      <Field label={t('settings.teamsConnection.tenantId')} className={s.grow} hint={t('settings.teamsConnection.tenantHint')}>
        <Input value={tenantId} onChange={(_, d) => setTenantId(d.value)} placeholder={t('settings.teamsConnection.tenantPlaceholder')} disabled={!canManage} />
      </Field>

      <Switch
        label={t('settings.teamsConnection.singleTenant')}
        checked={singleTenant}
        onChange={(_, d) => setSingleTenant(d.checked)}
        disabled={!canManage}
      />

      <Checkbox
        label={initial.hasPassword ? t('settings.teamsConnection.changeSecret') : t('settings.teamsConnection.setSecret')}
        checked={changePassword}
        onChange={(_, d) => setChangePassword(!!d.checked)}
        disabled={!canManage}
      />
      {changePassword && (
        <Field label={t('settings.teamsConnection.secret')} hint={t('settings.teamsConnection.secretHint')}>
          <Input type="password" value={password} onChange={(_, d) => setPassword(d.value)} />
        </Field>
      )}

      {canManage && (
        <div className={s.actions}>
          <Button appearance="primary" disabled={update.isPending} onClick={onSave}>
            {update.isPending ? t('settings.common.saving') : t('settings.common.save')}
          </Button>
          <Button disabled={test.isPending || !initial.isConfigured} onClick={onTest}>
            {test.isPending ? t('settings.teamsConnection.testing') : t('settings.teamsConnection.test')}
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
  const { t, i18n } = useTranslation()
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
  const monthLabels = MONTHS.map(month => t(`settings.common.months.${month.toLowerCase()}`))
  const ordinalRules = new Intl.PluralRules(i18n.resolvedLanguage ?? i18n.language, { type: 'ordinal' })
  const ordinal = (n: number) => t(`settings.integrations.ordinal.${ordinalRules.select(n)}`, { count: n })
  const dayOfMonthLabel = (lastDay: boolean, day: number) =>
    lastDay ? t('settings.integrations.theLastDay') : ordinal(day)

  function serviceSummary(i: Integration): string {
    if (i.serviceIds.length === 0) return t('settings.common.allServices')
    return i.serviceIds.map(id => servicesById.get(id)?.label || servicesById.get(id)?.name || t('settings.common.removed')).join(', ')
  }
  function schemaSummary(i: Integration): string {
    if (i.schemaIds.length === 0) return t('settings.common.allSchemas')
    return i.schemaIds.map(id => schemasById.get(id)?.label || schemasById.get(id)?.name || t('settings.common.removed')).join(', ')
  }
  function targetSummary(i: Integration): string {
    const who = i.teams.displayName || i.teams.targetId || t('settings.common.unset')
    return t('settings.integrations.targetSummary', {
      kind: i.teams.kind === 'Channel' ? t('settings.integrations.channel') : t('settings.integrations.user'),
      target: who,
    })
  }
  function scheduleSummary(i: Integration): string {
    const s = i.schedule
    const time = `${pad2(s.hourUtc)}:${pad2(s.minuteUtc)} UTC`
    const day = dayOfMonthLabel(s.lastDayOfMonth, s.dayOfMonth)
    switch (s.frequency) {
      case 'Weekly':
        return s.days.length === 0
          ? t('settings.integrations.schedule.weeklyEveryDay', { time })
          : t('settings.integrations.schedule.weeklyDays', {
              days: s.days.map(d => t(`settings.common.weekDaysShort.${d.toLowerCase()}`)).join(', '),
              time,
            })
      case 'Monthly':
        return t('settings.integrations.schedule.monthly', { day, time })
      case 'Quarterly':
        return t('settings.integrations.schedule.quarterly', { month: monthLabels[Math.min(Math.max(s.anchorMonth, 1), 12) - 1], day, time })
      case 'SemiAnnually':
        return t('settings.integrations.schedule.semiAnnually', { month: monthLabels[Math.min(Math.max(s.anchorMonth, 1), 12) - 1], day, time })
      case 'Yearly':
        return t('settings.integrations.schedule.yearly', { month: monthLabels[Math.min(Math.max(s.anchorMonth, 1), 12) - 1], day, time })
      default:
        return t('settings.integrations.schedule.daily', { time })
    }
  }

  async function onDelete(i: Integration) {
    if (!confirmDelete(t('settings.integrations.deleteType'), i.label || targetSummary(i))) return
    setBanner(null)
    try {
      await del.mutateAsync(i.id)
      setBanner({ intent: 'success', text: t('settings.integrations.deleted') })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  async function onToggleEnabled(i: Integration) {
    setBanner(null)
    const req = draftToRequest(toDraft(i))
    req.enabled = !i.enabled
    try {
      await update.mutateAsync({ id: i.id, req })
      setBanner({ intent: 'success', text: i.enabled ? t('settings.integrations.disabledMessage') : t('settings.integrations.enabledMessage') })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  async function onRun(i: Integration) {
    setBanner(null)
    try {
      const r = await run.mutateAsync(i.id)
      setBanner({ intent: 'success', text: t('settings.integrations.runComplete', { prompted: r.prompted, skipped: r.skipped }) })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  async function onSendTest(i: Integration) {
    setBanner(null)
    try {
      await sendTest.mutateAsync(i.id)
      setBanner({ intent: 'success', text: t('settings.integrations.testEnqueued') })
    } catch (err) { setBanner({ intent: 'error', text: formatApiError(err) }) }
  }

  const items = integrations ?? []
  const connectionReady = connection?.isConfigured === true

  return (
    <div className={s.root}>
      <Card className={s.card}>
        <div className={s.titleRow}>
          <Title3 className={s.sectionTitle}>{t('settings.integrations.title')}</Title3>
          <div className={s.headerActions}>
            {canManage && (
              <Button appearance="primary" icon={<Add20Regular />} onClick={() => setEditing(emptyDraft())}>
                {t('settings.integrations.add')}
              </Button>
            )}
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
          {t('settings.integrations.description')}
        </Body1>

        {connection && !connectionReady && (
          <AutoScrollMessageBar intent="warning">
            <MessageBarBody>
              {t('settings.integrations.connectionWarning')}
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
              <TableHeaderCell>{t('settings.common.label')}</TableHeaderCell>
              <TableHeaderCell className={s.colTarget}>{t('settings.integrations.target')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.common.services')}</TableHeaderCell>
              <TableHeaderCell>{t('settings.common.schemas')}</TableHeaderCell>
              <TableHeaderCell className={s.colSchedule}>{t('settings.integrations.scheduleLabel')}</TableHeaderCell>
              <TableHeaderCell className={s.colStatus}>{t('settings.common.status')}</TableHeaderCell>
              <TableHeaderCell className={s.colActions} aria-label={t('settings.common.actions')} />
            </TableRow>
          </TableHeader>
          <TableBody>
            {isLoading && <GridMessageRow colSpan={7}>{t('settings.common.loading')}</GridMessageRow>}
            {!isLoading && items.length === 0 && (
              <GridMessageRow colSpan={7}>
                {canManage ? t('settings.integrations.emptyManage') : t('settings.integrations.emptyReadOnly')}
              </GridMessageRow>
            )}
            {items.map(i => (
              <TableRow
                key={i.id}
                className={`${s.tableRow} ${s.rowClickable}`}
                {...clickableRowProps(() => canManage && setEditing(toDraft(i)), t('settings.integrations.editAria', { name: i.label || targetSummary(i) }))}
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
                    {i.enabled ? t('settings.common.enabled') : t('settings.common.disabled')}
                  </Badge>
                </TableCell>
                <TableCell className={s.colActions} onClick={ev => ev.stopPropagation()}>
                  {canManage && (
                    <RowActions
                      ariaLabel={t('settings.integrations.actionsAria', { name: i.label || targetSummary(i) })}
                      actions={[
                        { key: 'edit', label: t('settings.common.edit'), icon: <Edit20Regular />, onClick: () => setEditing(toDraft(i)) },
                        { key: 'run', label: t('settings.integrations.runNow'), icon: <Play20Regular />, disabled: run.isPending, onClick: () => onRun(i) },
                        { key: 'test', label: t('settings.integrations.sendTest'), icon: <Send20Regular />, disabled: sendTest.isPending, onClick: () => onSendTest(i) },
                        {
                          key: 'toggle',
                          label: i.enabled ? t('settings.common.disable') : t('settings.common.enable'),
                          icon: i.enabled ? <PauseCircle20Regular /> : <PlayCircle20Regular />,
                          disabled: update.isPending,
                          onClick: () => onToggleEnabled(i),
                        },
                        { key: 'delete', label: t('settings.common.delete'), icon: <Delete20Regular />, destructive: true, onClick: () => onDelete(i) },
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
          title={editing?.id ? t('settings.integrations.edit') : t('settings.integrations.add')}
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
  const { t, i18n } = useTranslation()
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
  const monthLabels = MONTHS.map(month => t(`settings.common.months.${month.toLowerCase()}`))
  const ordinalRules = new Intl.PluralRules(i18n.resolvedLanguage ?? i18n.language, { type: 'ordinal' })
  const ordinal = (n: number) => t(`settings.integrations.ordinal.${ordinalRules.select(n)}`, { count: n })

  async function onSave() {
    setError(null)
    if (!targetId.trim()) { setError(t('settings.integrations.targetRequired')); return }
    if (!allServices && serviceIds.length === 0) { setError(t('settings.common.serviceRequired')); return }
    if (!allSchemas && schemaIds.length === 0) { setError(t('settings.common.schemaRequired')); return }
    const hour = Number(hourUtc)
    const minute = Number(minuteUtc)
    if (!Number.isInteger(hour) || hour < 0 || hour > 23) { setError(t('settings.integrations.hourValidation')); return }
    if (!Number.isInteger(minute) || minute < 0 || minute > 59) { setError(t('settings.integrations.minuteValidation')); return }

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
      onSaved(isNew ? t('settings.integrations.created') : t('settings.integrations.saved'))
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  const pending = create.isPending || update.isPending

  return (
    <div className={s.drawerForm}>
      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}

      <Field label={t('settings.common.label')} hint={t('settings.common.optionalListName')}>
        <Input value={label} onChange={(_, d) => setLabel(d.value)} placeholder={t('settings.integrations.labelPlaceholder')} />
      </Field>

      <Switch label={t('settings.common.enabled')} checked={enabled} onChange={(_, d) => setEnabled(d.checked)} />

      <Field label={t('settings.integrations.sendTo')}>
        <RadioGroup layout="horizontal" value={targetKind} onChange={(_, d) => setTargetKind(d.value as TeamsTargetKind)}>
          <Radio value="User" label={t('settings.integrations.aUser')} />
          <Radio value="Channel" label={t('settings.integrations.aChannel')} />
        </RadioGroup>
      </Field>

      <Field
        label={targetKind === 'Channel' ? t('settings.integrations.channelId') : t('settings.integrations.userId')}
        required
        hint={t('settings.integrations.targetIdHint')}
      >
        <Input value={targetId} onChange={(_, d) => setTargetId(d.value)} placeholder={targetKind === 'Channel' ? t('settings.integrations.channelIdPlaceholder') : t('settings.integrations.userIdPlaceholder')} />
      </Field>

      <Field label={t('settings.integrations.displayName')} hint={t('settings.integrations.displayNameHint')}>
        <Input value={displayName} onChange={(_, d) => setDisplayName(d.value)} placeholder={t('settings.integrations.displayNamePlaceholder')} />
      </Field>

      <Field label={t('settings.common.services')}>
        <Checkbox label={t('settings.common.allServices')} checked={allServices} onChange={(_, d) => setAllServices(!!d.checked)} />
        {!allServices && (
          <Dropdown
            multiselect
            placeholder={t('settings.common.selectServices')}
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

      <Field label={t('settings.common.schemas')}>
        <Checkbox label={t('settings.common.allSchemas')} checked={allSchemas} onChange={(_, d) => setAllSchemas(!!d.checked)} />
        {!allSchemas && (
          <Dropdown
            multiselect
            placeholder={t('settings.common.selectSchemas')}
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

      <Field label={t('settings.integrations.frequency')} hint={t('settings.integrations.frequencyHint')}>
        <Dropdown
          value={t(`settings.common.cadences.${frequency}`)}
          selectedOptions={[frequency]}
          onOptionSelect={(_, d) => setFrequency(d.optionValue as IntegrationFrequency)}
        >
          {FREQUENCIES.map(f => (
            <Option key={f} value={f} text={t(`settings.common.cadences.${f}`)}>{t(`settings.common.cadences.${f}`)}</Option>
          ))}
        </Dropdown>
      </Field>

      {frequency === 'Weekly' && (
        <Field label={t('settings.integrations.daysOfWeek')} hint={t('settings.integrations.daysOfWeekHint')}>
          <Dropdown
            multiselect
            placeholder={t('settings.integrations.everyDay')}
            selectedOptions={days}
            value={days.length === 0 ? t('settings.integrations.everyDay') : days.map(d => t(`settings.common.weekDays.${d.toLowerCase()}`)).join(', ')}
            onOptionSelect={(_, d) => setDays(d.selectedOptions as Weekday[])}
          >
            {WEEKDAYS.map(d => (
              <Option key={d} value={d} text={t(`settings.common.weekDays.${d.toLowerCase()}`)}>{t(`settings.common.weekDays.${d.toLowerCase()}`)}</Option>
            ))}
          </Dropdown>
        </Field>
      )}

      {usesAnchorMonth && (
        <Field label={t('settings.integrations.anchorMonth')} hint={t('settings.integrations.anchorMonthHint')}>
          <Dropdown
            value={monthLabels[Math.min(Math.max(anchorMonth, 1), 12) - 1]}
            selectedOptions={[String(anchorMonth)]}
            onOptionSelect={(_, d) => setAnchorMonth(Number(d.optionValue))}
          >
            {MONTHS.map((m, idx) => (
              <Option key={m} value={String(idx + 1)} text={monthLabels[idx]}>{monthLabels[idx]}</Option>
            ))}
          </Dropdown>
        </Field>
      )}

      {usesDayOfMonth && (
        <Field label={t('settings.integrations.dayOfMonth')} hint={t('settings.integrations.dayOfMonthHint')}>
          <Dropdown
            value={lastDayOfMonth ? t('settings.integrations.lastDay') : ordinal(dayOfMonth)}
            selectedOptions={[dayOfMonthValue]}
            onOptionSelect={(_, d) => {
              if (d.optionValue === LAST_DAY) { setLastDayOfMonth(true) }
              else { setLastDayOfMonth(false); setDayOfMonth(Number(d.optionValue)) }
            }}
          >
            {DAY_OF_MONTH_OPTIONS.map(opt => (
              <Option key={opt} value={opt} text={opt === LAST_DAY ? t('settings.integrations.lastDay') : ordinal(Number(opt))}>
                {opt === LAST_DAY ? t('settings.integrations.lastDay') : ordinal(Number(opt))}
              </Option>
            ))}
          </Dropdown>
        </Field>
      )}

      <div className={s.row}>
        <Field label={t('settings.integrations.hourUtc')} required>
          <Input type="number" value={hourUtc} onChange={(_, d) => setHourUtc(d.value)} style={{ width: '110px' }} />
        </Field>
        <Field label={t('settings.integrations.minuteUtc')} required>
          <Input type="number" value={minuteUtc} onChange={(_, d) => setMinuteUtc(d.value)} style={{ width: '110px' }} />
        </Field>
      </div>

      <div className={s.actions}>
        <Button appearance="primary" disabled={pending} onClick={onSave}>
          {pending ? t('settings.common.saving') : isNew ? t('settings.integrations.create') : t('settings.common.saveChanges')}
        </Button>
        <Button appearance="secondary" disabled={pending} onClick={onClose}>{t('settings.common.cancel')}</Button>
      </div>
    </div>
  )
}
