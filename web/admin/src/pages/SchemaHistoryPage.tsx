import { useMemo } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  Badge, Button, Card, CardHeader, MessageBar, MessageBarBody,
  Text, Title2, makeStyles, tokens,
} from '@fluentui/react-components'
import { ArrowLeft20Regular } from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { formatApiError } from '../api/client'
import {
  CartesianGrid, ComposedChart, ErrorBar, Line, ReferenceArea, ResponsiveContainer,
  Tooltip, XAxis, YAxis,
} from 'recharts'
import { useSchemaHistory } from '../api/hooks'
import type { SchemaValueHistory } from '../api/types'
import { ragBandRects } from '../utils/targetBand'
import { cadenceLabel } from '../utils/cadence'
import { formatPeriodLabel } from '../utils/periodFormat'
import { useTranslation } from 'react-i18next'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  header: { display: 'flex', alignItems: 'center', gap: '12px' },
  grid: { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(360px, 1fr))', gap: '16px' },
  card: { padding: '16px' },
  cardHeader: { marginBottom: '8px' },
  cardSub: { color: tokens.colorNeutralForeground3, fontSize: '12px' },
  empty: { color: tokens.colorNeutralForeground3, padding: '24px 0', textAlign: 'center' },
})

interface ChartRow {
  /** Bucket label on the X axis, formatted by cadence. */
  period: string
  /** Underlying ISO date for sorting / tooltip detail. */
  periodIso: string
  min: number
  max: number
  avg: number
  count: number
  /** [avg - min, max - avg]: the asymmetric "whiskers" recharts ErrorBar wants. */
  errorRange: [number, number]
}

export function SchemaHistoryPage() {
  const s = useStyles()
  const { t } = useTranslation()
  const { name } = useParams<{ name: string }>()
  const { data, isLoading, error } = useSchemaHistory(name)

  return (
    <div className={s.root}>
      <div className={s.header}>
        <Button as="a" appearance="subtle" icon={<ArrowLeft20Regular />}>
          <Link to="/schemas">{t('schemasSubmissions.common.back')}</Link>
        </Button>
        <Title2>{t('schemasSubmissions.schemaHistory.title')}{data?.label || data?.schemaName ? ` — ${data?.label || data?.schemaName}` : ''}</Title2>
      </div>

      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{formatApiError(error)}</MessageBarBody></AutoScrollMessageBar>}
      {isLoading && <div>{t('schemasSubmissions.common.loading')}</div>}

      {data && data.values.length === 0 && (
        <MessageBar intent="info">
          <MessageBarBody>
            {t('schemasSubmissions.schemaHistory.noNumericValues')}
          </MessageBarBody>
        </MessageBar>
      )}

      {data && data.values.length > 0 && (
        <div className={s.grid}>
          {data.values.map(v => (
            <ValueChartCard key={v.valueName} value={v} />
          ))}
        </div>
      )}
    </div>
  )
}

function ValueChartCard({ value }: { value: SchemaValueHistory }) {
  const s = useStyles()
  const { t } = useTranslation()
  const rows = useMemo(() => toChartRows(value), [value])
  const bandRects = useMemo(() => ragBandRects(value), [value])
  const yAxisLabel = value.unit ? `${value.label || value.valueName} (${value.unit})` : (value.label || value.valueName)

  return (
    <Card className={s.card}>
      <CardHeader
        className={s.cardHeader}
        header={
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <Text weight="semibold">{value.label || value.valueName}</Text>
            <Badge appearance="outline" size="small">{t(`schemasSubmissions.common.valueType.${value.type}`)}</Badge>
            <Badge appearance="outline" color="informative" size="small">{cadenceLabel(value.cadence, t)}</Badge>
          </div>
        }
        description={<span className={s.cardSub}>{t('schemasSubmissions.schemaHistory.bucketDescription', { count: rows.length })}</span>}
      />
      {rows.length === 0 ? (
        <div className={s.empty}>{t('schemasSubmissions.schemaHistory.noSamples')}</div>
      ) : (
        <ResponsiveContainer width="100%" height={240}>
          <ComposedChart data={rows} margin={{ top: 8, right: 16, bottom: 8, left: 8 }}>
            <CartesianGrid strokeDasharray="3 3" stroke={tokens.colorNeutralStroke2} />
            {bandRects.map(r => (
              // Open edges (undefined y1/y2) anchor to the axis extent, so half-open bands still shade.
              <ReferenceArea
                key={r.key}
                y1={r.y1}
                y2={r.y2}
                ifOverflow="extendDomain"
                fill={r.tone === 'green' ? tokens.colorPaletteGreenBackground2 : tokens.colorPaletteYellowBackground2}
                fillOpacity={0.3}
                strokeOpacity={0}
              />
            ))}
            <XAxis dataKey="period" tick={{ fontSize: 11 }} />
            <YAxis tick={{ fontSize: 11 }} />
            <Tooltip
              formatter={(_v, _n, item: { payload?: ChartRow }) => {
                const r = item.payload
                if (!r) return ['', '']
                return [`${r.min.toFixed(2)} / ${r.avg.toFixed(2)} / ${r.max.toFixed(2)} (n=${r.count})`, t('schemasSubmissions.schemaHistory.minAvgMax')]
              }}
              labelFormatter={(_l, items) => {
                const r = (items?.[0]?.payload) as ChartRow | undefined
                return r ? r.period : ''
              }}
            />
            <Line
              type="monotone"
              dataKey="avg"
              name={yAxisLabel}
              stroke={tokens.colorBrandStroke1}
              strokeWidth={2}
              dot={{ r: 3, fill: tokens.colorBrandStroke1 }}
            >
              <ErrorBar dataKey="errorRange" width={6} strokeWidth={1.5} stroke={tokens.colorBrandStroke2 ?? tokens.colorBrandStroke1} direction="y" />
            </Line>
          </ComposedChart>
        </ResponsiveContainer>
      )}
    </Card>
  )
}

function toChartRows(v: SchemaValueHistory): ChartRow[] {
  return v.buckets.map(b => ({
    period: formatPeriodLabel(b.periodStart, v.cadence),
    periodIso: b.periodStart,
    min: b.min,
    max: b.max,
    avg: b.average,
    count: b.count,
    errorRange: [b.average - b.min, b.max - b.average],
  }))
}
