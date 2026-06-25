import { useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  Dropdown, MessageBarBody, Option, Tab, TabList,
  Menu, MenuButton, MenuDivider, MenuItem, MenuList, MenuPopover, MenuTrigger,
  Title2, Tooltip as FluentTooltip,
} from '@fluentui/react-components'
import {
  ArrowClockwise20Regular, ArrowDownload20Regular, Image20Regular, Info16Regular, MoreHorizontal20Regular,
} from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { PeriodFilter } from '../components/PeriodFilter'
import { ExplorePresets } from '../components/ExplorePresets'
import { formatApiError } from '../api/client'
import { useAccounts, useExploreAnomalies, useExploreScorecard, useExploreSeries, useSchemas } from '../api/hooks'
import type { Account, ExploreAggregation, ExploreValueSeries, Schema, SchemaValue } from '../api/types'
import { intervalRange, shiftIso, SHIFT_LABELS, type Interval, type ShiftKey } from '../utils/period'
import type { PeriodFilterState } from '../utils/usePeriodFilter'
import { buildCsv } from '../utils/csv'
import { downloadText } from '../utils/download'
import { exportChartPng } from '../utils/chartExport'
import {
  AGG_LABELS, AGGREGATIONS, ANOMALY_THRESHOLD_DEFAULT, ANOMALY_WINDOW_DEFAULT,
  buildExportRows, label, useExploreStyles,
  type ExploreView, type OuterTab,
} from './explore/shared'
import { AnomalyFields } from './explore/AnomalySettings'
import { ScorecardView } from './explore/scorecard/ScorecardView'
import { ExploreContent } from './explore/content/ExploreContent'
import { AnomaliesView } from './explore/anomalies/AnomaliesView'

const SHIFTS: ShiftKey[] = ['1m', '6m', '1y']

/**
 * Explore page orchestrator: owns the URL-backed filter state and data fetching, renders the filter
 * bar / tabs / export menu, and delegates the result area to the cross-schema scorecard
 * (`ScorecardView`) or the per-schema Trend/Compare/Snapshot views (`ExploreContent`).
 */
export function ExplorePage() {
  const s = useExploreStyles()
  const [sp, setSp] = useSearchParams()

  // URL is the single source of truth so a filtered view is shareable by copying the address bar.
  const update = (patch: Record<string, string | null>) => {
    setSp(prev => {
      const next = new URLSearchParams(prev)
      for (const [k, v] of Object.entries(patch)) {
        if (v === null || v === '') next.delete(k)
        else next.set(k, v)
      }
      return next
    }, { replace: true })
  }

  const schemas = useSchemas()
  const schemaList = useMemo(
    () => [...(schemas.data?.items ?? [])].sort((a, b) => label(a).localeCompare(label(b))),
    [schemas.data],
  )

  // Resolve the active schema: the URL value when valid, otherwise the first one available.
  const schemaParam = sp.get('schema') ?? ''
  const schema: Schema | undefined =
    schemaList.find(x => x.name === schemaParam) ?? schemaList[0]
  const schemaName = schema?.name ?? ''

  const numericValues: SchemaValue[] = useMemo(
    () => (schema?.values ?? []).filter(v => v.type === 'Number' || v.type === 'Integer'),
    [schema],
  )
  const valueParam = sp.get('value') ?? ''
  const activeValueName =
    numericValues.find(v => v.name === valueParam)?.name ?? numericValues[0]?.name ?? ''

  const agg = (sp.get('agg') as ExploreAggregation) || 'Average'
  const tab = (sp.get('tab') as OuterTab) || 'analysis'
  const isScorecard = tab === 'scorecard'
  const isAnomalies = tab === 'anomalies'
  const isAnalysis = !isScorecard && !isAnomalies
  const view = (sp.get('view') as ExploreView) || 'trend'
  const onlyIssues = sp.get('attn') === '1'
  const scHideMissing = sp.get('scmiss') === '1'

  // Anomaly detection state (shared by the Trend "Highlight anomalies" toggle and the Anomalies tab).
  const anomalyOn = sp.get('az') === '1'
  const anomalyWindow = Number(sp.get('awin')) || ANOMALY_WINDOW_DEFAULT
  const anomalyThreshold = Number(sp.get('athr')) || ANOMALY_THRESHOLD_DEFAULT
  const anomalyRobust = sp.get('arob') === '1'
  // Anomalies tab: which schemas to scan (empty = all), which period, and whether to hide normals.
  const anomalySchemaNames = useMemo(
    () => (sp.get('aschemas') ?? '').split(',').filter(Boolean),
    [sp],
  )
  const anomalyPeriod = (sp.get('aperiod') as 'current' | 'closed') === 'closed' ? 'closed' : 'current'
  const hideNormal = sp.get('aattn') === '1'
  const anomalyHideMissing = sp.get('amiss') === '1'
  // Scorecard-only: which sample to show per service, and (for last-period) which period to read.
  const scMode = (sp.get('scmode') as 'latest' | 'period') === 'period' ? 'period' : 'latest'
  const scPeriod = (sp.get('scperiod') as 'current' | 'closed') === 'closed' ? 'closed' : 'current'
  const combined = sp.get('combined') === '1'
  const asTable = sp.get('table') === '1'
  const projecting = sp.get('proj') === '1'
  // A single "Compare with previous" dropdown: empty/absent means off, otherwise it's the shift.
  const shift = (sp.get('shift') ?? '') as ShiftKey | ''
  const comparing = shift !== ''

  // Period filter backed by the URL so it round-trips with everything else.
  const interval = (sp.get('period') as Interval) || 'all'
  const customFrom = sp.get('cfrom') ?? ''
  const customTo = sp.get('cto') ?? ''
  const { from, to } = intervalRange(interval, customFrom, customTo)

  // "Compare with previous" only makes sense for a bounded window, so it needs both ends resolved.
  const canCompare = !!from && !!to
  const from2 = comparing && canCompare ? shiftIso(from!, shift as ShiftKey) : undefined
  const to2 = comparing && canCompare ? shiftIso(to!, shift as ShiftKey) : undefined
  const periodState: PeriodFilterState = {
    interval,
    setInterval: v => update({ period: v === 'all' ? null : v }),
    customFrom, setCustomFrom: v => update({ cfrom: v }),
    customTo, setCustomTo: v => update({ cto: v }),
    from, to,
  }

  // Service multiselect options come from the account registry (independent of the data), so the
  // filter is usable even before any series loads.
  const accounts = useAccounts({ role: 'Service', pageSize: 500 })
  const serviceAccounts: Account[] = useMemo(
    () => [...(accounts.data?.items ?? [])].sort((a, b) => label(a).localeCompare(label(b))),
    [accounts.data],
  )
  const selectedServiceIds = useMemo(
    () => (sp.get('services') ?? '').split(',').filter(Boolean),
    [sp],
  )
  const toggleService = (id: string) => {
    const set = new Set(selectedServiceIds)
    if (set.has(id)) set.delete(id)
    else set.add(id)
    update({ services: [...set].join(',') })
  }
  const toggleAnomalySchema = (name: string) => {
    const set = new Set(anomalySchemaNames)
    if (set.has(name)) set.delete(name)
    else set.add(name)
    update({ aschemas: [...set].join(',') })
  }

  // Anomaly scoring on the Trend chart is opt-in and only applies to that view.
  const seriesAnomaly = anomalyOn && view === 'trend' && isAnalysis
  const series = useExploreSeries(
    {
      schema: schemaName,
      serviceIds: selectedServiceIds.length ? selectedServiceIds : undefined,
      from, to, agg,
      anomaly: seriesAnomaly || undefined,
      anomalyWindow, anomalyThreshold, anomalyRobust,
    },
    !!schemaName && isAnalysis,
  )

  // Previous-period overlay: the same query shifted back by `shift`. Only fetched for the Trend
  // view, when the toggle is on and the window is bounded.
  const compareEnabled = comparing && canCompare && !isScorecard && view === 'trend'
  const prevSeries = useExploreSeries(
    {
      schema: schemaName,
      serviceIds: selectedServiceIds.length ? selectedServiceIds : undefined,
      from: from2, to: to2, agg,
    },
    !!schemaName && compareEnabled,
  )

  const scorecard = useExploreScorecard(
    selectedServiceIds.length ? selectedServiceIds : undefined,
    scMode === 'period' ? 'LastPeriod' : 'LatestAvailable',
    scPeriod === 'closed' ? 'LatestClosed' : 'Current',
    isScorecard,
  )

  const anomalies = useExploreAnomalies(
    {
      schemaNames: anomalySchemaNames.length ? anomalySchemaNames : undefined,
      serviceIds: selectedServiceIds.length ? selectedServiceIds : undefined,
      period: anomalyPeriod === 'closed' ? 'LatestClosed' : 'Current',
      window: anomalyWindow,
      threshold: anomalyThreshold,
      robust: anomalyRobust,
    },
    isAnomalies,
  )

  const activeValue: SchemaValue | undefined =
    numericValues.find(v => v.name === activeValueName)
  const activeSeries: ExploreValueSeries | undefined =
    series.data?.values.find(v => v.valueName === activeValueName)
  const prevActiveSeries: ExploreValueSeries | undefined =
    compareEnabled ? prevSeries.data?.values.find(v => v.valueName === activeValueName) : undefined
  const seriesServices = series.data?.services ?? []

  const chartRef = useRef<HTMLDivElement>(null)
  const [exportError, setExportError] = useState<string | null>(null)

  const exportCsv = () => {
    try {
      if (!series.data) return
      const { headers, rows, name } = buildExportRows(view, series.data.values, activeSeries, seriesServices, agg, seriesAnomaly, combined)
      if (rows.length === 0) { setExportError('Nothing to export for this view yet.'); return }
      downloadText(`explore-${schemaName}-${name}.csv`, buildCsv(headers, rows), 'text/csv;charset=utf-8')
    } catch (e) {
      setExportError(formatApiError(e))
    }
  }

  const exportPng = async () => {
    try {
      await exportChartPng(chartRef.current, `explore-${schemaName}-${view}.png`)
    } catch (e) {
      setExportError(formatApiError(e))
    }
  }

  const noNumeric = !!schema && numericValues.length === 0

  return (
    <div className={s.root}>
      <div className={s.header}>
        <Title2>Explore</Title2>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <ExplorePresets
            current={sp.toString()}
            onLoad={q => setSp(new URLSearchParams(q), { replace: true })}
          />
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label="More actions" />
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem icon={<ArrowClockwise20Regular />} onClick={() => (isScorecard ? scorecard.refetch() : isAnomalies ? anomalies.refetch() : series.refetch())}>Refresh</MenuItem>
                {isAnalysis && (
                  <>
                    <MenuDivider />
                    <MenuItem icon={<ArrowDownload20Regular />} disabled={!series.data} onClick={exportCsv}>Export CSV (this view)</MenuItem>
                    {view !== 'snapshot' && (
                      <MenuItem icon={<Image20Regular />} disabled={!series.data} onClick={exportPng}>Export chart (PNG)</MenuItem>
                    )}
                  </>
                )}
              </MenuList>
            </MenuPopover>
          </Menu>
        </div>
      </div>

      <TabList selectedValue={tab} onTabSelect={(_, d) => update({ tab: d.value as string })}>
        <Tab value="scorecard">Scorecard</Tab>
        <Tab value="analysis">Analysis</Tab>
        <Tab value="anomalies">Anomalies</Tab>
      </TabList>

      {(schemas.error || (isScorecard ? scorecard.error : isAnomalies ? anomalies.error : series.error)) && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{formatApiError(schemas.error || (isScorecard ? scorecard.error : isAnomalies ? anomalies.error : series.error))}</MessageBarBody>
        </AutoScrollMessageBar>
      )}
      {exportError && (
        <AutoScrollMessageBar intent="error"><MessageBarBody>{exportError}</MessageBarBody></AutoScrollMessageBar>
      )}

      <div className={s.filters}>
        {isAnalysis && (
          <div className={s.field}>
            <span className={s.fieldLabel}>Schema</span>
            <Dropdown
              className={s.dropdown}
              selectedOptions={schemaName ? [schemaName] : []}
              value={schema ? label(schema) : ''}
              placeholder="Select a schema"
              onOptionSelect={(_, d) => update({ schema: d.optionValue ?? null, value: null })}
            >
              {schemaList.map(x => <Option key={x.name} value={x.name}>{label(x)}</Option>)}
            </Dropdown>
          </div>
        )}

        {isAnomalies && (
          <div className={s.field}>
            <span className={s.fieldLabel}>
              Schemas
              <FluentTooltip
                relationship="description"
                content="Which schemas to scan for anomalies. Leave empty to scan every schema with numeric values."
              >
                <Info16Regular className={s.infoIcon} tabIndex={0} aria-label="What does Schemas do?" />
              </FluentTooltip>
            </span>
            <Dropdown
              className={s.dropdown}
              multiselect
              selectedOptions={anomalySchemaNames}
              value={anomalySchemaNames.length === 0 ? 'All schemas' : `${anomalySchemaNames.length} selected`}
              placeholder="All schemas"
              onOptionSelect={(_, d) => d.optionValue && toggleAnomalySchema(d.optionValue)}
            >
              {schemaList.map(x => <Option key={x.name} value={x.name}>{label(x)}</Option>)}
            </Dropdown>
          </div>
        )}

        {view !== 'snapshot' && isAnalysis && (
          <div className={s.field}>
            <span className={s.fieldLabel}>Value</span>
            <Dropdown
              className={s.dropdown}
              selectedOptions={activeValueName ? [activeValueName] : []}
              value={numericValues.find(v => v.name === activeValueName)?.label || activeValueName}
              placeholder="Select a value"
              disabled={numericValues.length === 0}
              onOptionSelect={(_, d) => update({ value: d.optionValue ?? null })}
            >
              {numericValues.map(v => <Option key={v.name} value={v.name}>{v.label || v.name}</Option>)}
            </Dropdown>
          </div>
        )}

        <div className={s.field}>
          <span className={s.fieldLabel}>Services</span>
          <Dropdown
            className={s.dropdown}
            multiselect
            selectedOptions={selectedServiceIds}
            value={selectedServiceIds.length === 0 ? 'All services' : `${selectedServiceIds.length} selected`}
            placeholder="All services"
            onOptionSelect={(_, d) => d.optionValue && toggleService(d.optionValue)}
          >
            {serviceAccounts.map(a => <Option key={a.id} value={a.id}>{label(a)}</Option>)}
          </Dropdown>
        </div>

        {isScorecard && (
          <div className={s.field}>
            <span className={s.fieldLabel}>
              Show
              <FluentTooltip
                relationship="description"
                content="Latest available shows each service's most recent submission, however old. Last period shows a single period and marks services that didn't submit it as grey 'no submission' cards."
              >
                <Info16Regular className={s.infoIcon} tabIndex={0} aria-label="What does Show do?" />
              </FluentTooltip>
            </span>
            <Dropdown
              className={s.dropdown}
              selectedOptions={[scMode]}
              value={scMode === 'period' ? 'Last period' : 'Latest available'}
              onOptionSelect={(_, d) => update({ scmode: d.optionValue === 'period' ? 'period' : null })}
            >
              <Option value="latest">Latest available</Option>
              <Option value="period">Last period</Option>
            </Dropdown>
          </div>
        )}

        {isScorecard && scMode === 'period' && (
          <div className={s.field}>
            <span className={s.fieldLabel}>Period</span>
            <Dropdown
              className={s.dropdown}
              selectedOptions={[scPeriod]}
              value={scPeriod === 'closed' ? 'Latest closed' : 'Current'}
              onOptionSelect={(_, d) => update({ scperiod: d.optionValue === 'closed' ? 'closed' : null })}
            >
              <Option value="current">Current</Option>
              <Option value="closed">Latest closed</Option>
            </Dropdown>
          </div>
        )}

        {isAnalysis && (
          <div className={s.field}>
            <span className={s.fieldLabel}>
              Aggregation
              <FluentTooltip
                relationship="description"
                content="How the samples that fall in each period are reduced to one number — and how several services are combined into the overall figure. Average is count-weighted; Sample count just tallies how many were submitted."
              >
                <Info16Regular className={s.infoIcon} tabIndex={0} aria-label="What does Aggregation do?" />
              </FluentTooltip>
            </span>
            <Dropdown
              className={s.dropdown}
              selectedOptions={[agg]}
              value={AGG_LABELS[agg]}
              onOptionSelect={(_, d) => update({ agg: (d.optionValue as ExploreAggregation) ?? null })}
            >
              {AGGREGATIONS.map(a => <Option key={a} value={a}>{AGG_LABELS[a]}</Option>)}
            </Dropdown>
          </div>
        )}

        {isAnomalies && (
          <div className={s.field}>
            <span className={s.fieldLabel}>Period</span>
            <Dropdown
              className={s.dropdown}
              selectedOptions={[anomalyPeriod]}
              value={anomalyPeriod === 'closed' ? 'Latest closed' : 'Current'}
              onOptionSelect={(_, d) => update({ aperiod: d.optionValue === 'closed' ? 'closed' : null })}
            >
              <Option value="current">Current</Option>
              <Option value="closed">Latest closed</Option>
            </Dropdown>
          </div>
        )}

      </div>

      {isAnomalies && (
        <div className={s.filters}>
          <AnomalyFields
            window={anomalyWindow}
            threshold={anomalyThreshold}
            robust={anomalyRobust}
            onWindow={n => update({ awin: n === ANOMALY_WINDOW_DEFAULT ? null : String(n) })}
            onThreshold={n => update({ athr: n === ANOMALY_THRESHOLD_DEFAULT ? null : String(n) })}
            onRobust={b => update({ arob: b ? '1' : null })}
          />
        </div>
      )}

      {isAnalysis && (
        <div className={s.filters}>
          <PeriodFilter state={periodState} />

          {view === 'trend' && (
            <div className={s.field}>
              <span className={s.fieldLabel}>
                Compare with previous
                <FluentTooltip
                  relationship="description"
                  content="Overlay the same selection shifted back in time so you can read this period against an earlier one. Needs a Period range (not All time); the two windows may overlap."
                >
                  <Info16Regular className={s.infoIcon} tabIndex={0} aria-label="What does Compare do?" />
                </FluentTooltip>
              </span>
              <Dropdown
                className={s.dropdown}
                disabled={!canCompare}
                selectedOptions={[comparing ? shift : 'off']}
                value={comparing ? SHIFT_LABELS[shift as ShiftKey] : 'No'}
                onOptionSelect={(_, d) => update({ shift: d.optionValue && d.optionValue !== 'off' ? d.optionValue : null })}
              >
                <Option value="off">No</Option>
                {SHIFTS.map(k => <Option key={k} value={k}>{SHIFT_LABELS[k]}</Option>)}
              </Dropdown>
            </div>
          )}
        </div>
      )}

      {isAnalysis && (
        <TabList
          appearance="subtle"
          size="small"
          selectedValue={view}
          onTabSelect={(_, d) => update({ view: d.value as string })}
        >
          <Tab value="trend">Trend</Tab>
          <Tab value="compare">Compare services</Tab>
          <Tab value="snapshot">Snapshot</Tab>
        </TabList>
      )}

      {isScorecard ? (
        <ScorecardView
          data={scorecard.data}
          isLoading={scorecard.isLoading}
          onlyIssues={onlyIssues}
          onToggleOnlyIssues={v => update({ attn: v ? '1' : null })}
          hideMissing={scHideMissing}
          onToggleHideMissing={v => update({ scmiss: v ? '1' : null })}
        />
      ) : isAnomalies ? (
        <AnomaliesView
          data={anomalies.data}
          isLoading={anomalies.isLoading}
          hideNormal={hideNormal}
          onToggleHideNormal={v => update({ aattn: v ? '1' : null })}
          hideMissing={anomalyHideMissing}
          onToggleHideMissing={v => update({ amiss: v ? '1' : null })}
          period={anomalyPeriod}
          window={anomalyWindow}
          threshold={anomalyThreshold}
          robust={anomalyRobust}
        />
      ) : (
        <ExploreContent
          view={view}
          schemaName={schemaName}
          noNumeric={noNumeric}
          isLoading={series.isLoading}
          values={series.data?.values ?? []}
          services={seriesServices}
          agg={agg}
          activeSeries={activeSeries}
          prevActiveSeries={prevActiveSeries}
          activeValue={activeValue}
          activeValueName={activeValueName}
          combined={combined}
          asTable={asTable}
          projecting={projecting}
          comparing={comparing}
          previousLabel={comparing ? SHIFT_LABELS[shift as ShiftKey] : undefined}
          chartRef={chartRef}
          anomaly={{
            on: seriesAnomaly,
            window: anomalyWindow,
            threshold: anomalyThreshold,
            robust: anomalyRobust,
            onToggle: v => update({ az: v ? '1' : null }),
            onWindow: n => update({ awin: n === ANOMALY_WINDOW_DEFAULT ? null : String(n) }),
            onThreshold: n => update({ athr: n === ANOMALY_THRESHOLD_DEFAULT ? null : String(n) }),
            onRobust: b => update({ arob: b ? '1' : null }),
          }}
          onToggleCombined={v => update({ combined: v ? '1' : null })}
          onToggleProjection={v => update({ proj: v ? '1' : null })}
          onToggleTable={v => update({ table: v ? '1' : null })}
        />
      )}
    </div>
  )
}
