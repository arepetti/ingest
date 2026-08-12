import { Dropdown, Input, Option, makeStyles, tokens } from '@fluentui/react-components'
import { useTranslation } from 'react-i18next'
import type { Interval } from '../../utils/period'
import type { PeriodFilterState } from '../../utils/usePeriodFilter'

const useStyles = makeStyles({
  field: { display: 'flex', flexDirection: 'column', gap: '4px' },
  label: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
  dropdown: { minWidth: '200px' },
})

const intervals: Interval[] = ['all', 'lastDay', 'lastWeek', 'lastMonth', 'custom']

export function AnalyticsPeriodFilter({
  state,
  onChange,
}: {
  state: PeriodFilterState
  onChange?: () => void
}) {
  const s = useStyles()
  const { t } = useTranslation()
  const intervalLabel = (interval: Interval) => t(`analytics.periodFilter.intervals.${interval}`)

  return (
    <>
      <div className={s.field}>
        <span className={s.label}>{t('analytics.common.period')}</span>
        <Dropdown
          className={s.dropdown}
          selectedOptions={[state.interval]}
          value={intervalLabel(state.interval)}
          onOptionSelect={(_, d) => {
            state.setInterval((d.optionValue as Interval) ?? 'all')
            onChange?.()
          }}
        >
          {intervals.map(interval => (
            <Option key={interval} value={interval}>{intervalLabel(interval)}</Option>
          ))}
        </Dropdown>
      </div>
      {state.interval === 'custom' && (
        <>
          <div className={s.field}>
            <span className={s.label}>{t('analytics.periodFilter.from')}</span>
            <Input
              type="datetime-local"
              value={state.customFrom}
              onChange={(_, v) => { state.setCustomFrom(v.value); onChange?.() }}
            />
          </div>
          <div className={s.field}>
            <span className={s.label}>{t('analytics.periodFilter.to')}</span>
            <Input
              type="datetime-local"
              value={state.customTo}
              onChange={(_, v) => { state.setCustomTo(v.value); onChange?.() }}
            />
          </div>
        </>
      )}
    </>
  )
}
