import { Dropdown, Input, Option, makeStyles, tokens } from '@fluentui/react-components'
import { intervalLabel, type Interval } from '../utils/period'
import type { PeriodFilterState } from '../utils/usePeriodFilter'
import { useTranslation } from 'react-i18next'

const useStyles = makeStyles({
  field: { display: 'flex', flexDirection: 'column', gap: '4px' },
  label: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
  dropdown: { minWidth: '200px' },
})

/**
 * "Filter by period" control: a relative-range dropdown (all / last day / week / month) plus a pair
 * of datetime inputs shown only for a custom range. Renders as bare fields so it drops into an
 * existing filters row. `onChange` fires whenever the selection changes (e.g. to reset paging).
 */
export function PeriodFilter({ state, onChange }: { state: PeriodFilterState; onChange?: () => void }) {
  const s = useStyles()
  const { t } = useTranslation()
  const intervals: Interval[] = ['all', 'lastDay', 'lastWeek', 'lastMonth', 'custom']

  return (
    <>
      <div className={s.field}>
        <span className={s.label}>{t('shell.period.label')}</span>
        <Dropdown
          className={s.dropdown}
          selectedOptions={[state.interval]}
          value={intervalLabel(state.interval, t)}
          onOptionSelect={(_, d) => {
            state.setInterval((d.optionValue as Interval) ?? 'all')
            onChange?.()
          }}
        >
          {intervals.map(k => (
            <Option key={k} value={k}>{intervalLabel(k, t)}</Option>
          ))}
        </Dropdown>
      </div>
      {state.interval === 'custom' && (
        <>
          <div className={s.field}>
            <span className={s.label}>{t('shell.period.from')}</span>
            <Input
              type="datetime-local"
              value={state.customFrom}
              onChange={(_, v) => { state.setCustomFrom(v.value); onChange?.() }}
            />
          </div>
          <div className={s.field}>
            <span className={s.label}>{t('shell.period.to')}</span>
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
