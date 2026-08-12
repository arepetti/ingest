import { useParams } from 'react-router-dom'
import {
  Badge, Dropdown, Option, Field, Title2, Tooltip,
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow,
  makeStyles, MessageBarBody, tokens,
} from '@fluentui/react-components'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { LocalizedTime } from '../components/LocalizedTime'
import { formatApiError } from '../api/client'
import { useAccounts, useServiceStatus } from '../api/hooks'
import { useMemo, useState, type ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import type { Cadence, SchemaStatus } from '../api/types'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '24px' },
  toolbar: { display: 'flex', alignItems: 'center', gap: '16px', justifyContent: 'space-between' },
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  // Slightly muted text for the "y" of "x/y", so the eye lands on the submitted count first.
  total:       { color: tokens.colorNeutralForeground3 },
  requiredHint:{ color: tokens.colorNeutralForeground3, fontSize: '12px' },
  // Same "ellipsize instead of wrap" trick used by the other grids — combined with the inner
  // truncate class, long schema labels stay on one line and get a tooltip with the full text.
  nameCell: { maxWidth: 0 },
  truncate: {
    display: 'block',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
})

const periods = ['day', 'week', 'fortnight', 'month', 'quarter', 'halfyear', 'year']

// Schemas can mix cadences across their values; when that happens we surface the sentinel "Mixed"
// (or "—" when nothing is active) instead of a real Cadence, and those obviously have no
// prettification rule. Keep the helper local so the shared cadenceLabel() stays strictly typed.
type CadenceCellValue = Cadence | '—' | 'Mixed'

function displayCadence(value: CadenceCellValue, t: TFunction): string {
  if (value === '—') return value
  if (value === 'Mixed') return t('analytics.serviceStatus.mixed')
  return t(`analytics.cadence.${value.toLowerCase()}`)
}

/**
 * One row per schema, summarising how complete its submissions are in the current cadence
 * window. We deliberately skip the per-value table — it gets unreadable once a service
 * publishes more than a handful of schemas.
 */
export function ServiceStatusPage() {
  const s = useStyles()
  const { t } = useTranslation()
  const { name } = useParams<{ name: string }>()
  const [period, setPeriod] = useState('week')
  const { data, isLoading, error } = useServiceStatus(name, period)
  // The status endpoint returns names only; resolve the friendly label client-side so the
  // title matches what users see everywhere else (sidebar, services grid, submissions).
  const services = useAccounts({ role: 'Service' })
  const displayName = useMemo(() => {
    const acc = services.data?.items.find(a => a.name === name)
    return acc?.label || acc?.name || name || ''
  }, [services.data, name])

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <Title2>{t('analytics.serviceStatus.title', { service: displayName })}</Title2>
        <Field label={t('analytics.common.period')}>
          <Dropdown selectedOptions={[period]} value={t(`analytics.serviceStatus.periods.${period}`)} onOptionSelect={(_, d) => setPeriod(d.optionValue as string)}>
            {periods.map(p => <Option key={p} value={p}>{t(`analytics.serviceStatus.periods.${p}`)}</Option>)}
          </Dropdown>
        </Field>
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{formatApiError(error)}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {isLoading && <div>{t('analytics.common.loading')}</div>}

      <Table size="small">
        <TableHeader>
          <TableRow>
            <TableHeaderCell>{t('analytics.common.schema')}</TableHeaderCell>
            <TableHeaderCell>{t('analytics.common.cadence')}</TableHeaderCell>
            <TableHeaderCell>{t('analytics.serviceStatus.columns.submitted')}</TableHeaderCell>
            <TableHeaderCell>{t('analytics.serviceStatus.columns.lastSample')}</TableHeaderCell>
            <TableHeaderCell>{t('analytics.serviceStatus.columns.status')}</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {(data?.schemas ?? []).map(schema => <SchemaRow key={schema.schemaName} schema={schema} className={s.row} />)}
          {!isLoading && (data?.schemas ?? []).length === 0 && (
            <TableRow><TableCell colSpan={5}>{t('analytics.serviceStatus.empty')}</TableCell></TableRow>
          )}
        </TableBody>
      </Table>
    </div>
  )
}

function SchemaRow({ schema, className }: { schema: SchemaStatus; className: string }) {
  const s = useStyles()
  const { t } = useTranslation()
  const summary = summarise(schema, t)
  const display = schema.label || schema.schemaName

  return (
    <TableRow className={className}>
      <TableCell className={s.nameCell}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
          <Tooltip content={display} relationship="label">
            <strong className={s.truncate}>{display}</strong>
          </Tooltip>
          {!schema.enabled && <Badge appearance="outline" color="subtle">{t('analytics.serviceStatus.disabled')}</Badge>}
        </div>
      </TableCell>
      <TableCell>{displayCadence(summary.cadence, t)}</TableCell>
      <TableCell>
        <Tooltip
          relationship="description"
          content={t('analytics.serviceStatus.submittedHelp', {
            submitted: summary.submitted,
            total: summary.total,
            requiredSubmitted: summary.requiredSubmitted,
            requiredTotal: summary.requiredTotal,
          })}
        >
          <span>
            <strong>{summary.submitted}</strong>
            <span className={s.total}>/{summary.total}</span>
            {summary.requiredTotal > 0 && (
              <> <span className={s.requiredHint}>· {t('analytics.serviceStatus.requiredRatio', {
                submitted: summary.requiredSubmitted,
                total: summary.requiredTotal,
              })}</span></>
            )}
          </span>
        </Tooltip>
      </TableCell>
      <TableCell><LocalizedTime value={summary.lastTimestamp} /></TableCell>
      <TableCell>
        {summary.statusBadge}
      </TableCell>
    </TableRow>
  )
}

interface SchemaSummary {
  total: number
  submitted: number
  requiredTotal: number
  requiredSubmitted: number
  cadence: CadenceCellValue
  lastTimestamp?: string
  statusBadge: ReactElement
}

function summarise(schema: SchemaStatus, t: TFunction): SchemaSummary {
  // A disabled schema short-circuits everything — its values are inert by definition.
  if (!schema.enabled) {
    return {
      total: 0,
      submitted: 0,
      requiredTotal: 0,
      requiredSubmitted: 0,
      cadence: '—',
      statusBadge: <Badge appearance="outline" color="subtle">{t('analytics.serviceStatus.statuses.inert')}</Badge>,
    }
  }

  // Disabled values can't be submitted, so excluding them from the denominator keeps the
  // ratio meaningful (otherwise a schema with N disabled values would never reach 100%).
  const active = schema.values.filter(v => v.enabled)
  const submitted = active.filter(v => v.satisfied).length
  const requiredTotal = active.filter(v => v.required).length
  const requiredSubmitted = active.filter(v => v.required && v.satisfied).length

  // Pick the freshest timestamp across all values so the row tells you "when was the last
  // time this service sent anything for this schema".
  const lastTimestamp = active
    .map(v => v.lastTimestamp)
    .filter((t): t is string => !!t)
    .sort()
    .pop()

  const cadences = new Set<Cadence>(active.map(v => v.cadence))
  // Cell value is either a real Cadence (when the schema's values agree on one) or one of two
  // sentinels we render verbatim. The explicit annotation keeps the display helper happy.
  const cadence: CadenceCellValue = cadences.size === 0 ? '—' : cadences.size === 1 ? [...cadences][0] : 'Mixed'

  let statusBadge: ReactElement
  if (active.length === 0) {
    statusBadge = <Badge appearance="outline" color="subtle">{t('analytics.serviceStatus.statuses.noValues')}</Badge>
  } else if (requiredTotal === 0) {
    statusBadge = submitted > 0
      ? <Badge appearance="filled" color="success">{t('analytics.serviceStatus.statuses.submitted')}</Badge>
      : <Badge appearance="outline" color="subtle">{t('analytics.serviceStatus.statuses.optional')}</Badge>
  } else if (requiredSubmitted === requiredTotal) {
    statusBadge = <Badge appearance="filled" color="success">{t('analytics.serviceStatus.statuses.submitted')}</Badge>
  } else {
    statusBadge = <Badge appearance="filled" color="danger">{t('analytics.serviceStatus.statuses.missing')}</Badge>
  }

  return { total: active.length, submitted, requiredTotal, requiredSubmitted, cadence, lastTimestamp, statusBadge }
}
