import { Dropdown, Input, Option, makeStyles, tokens } from '@fluentui/react-components'
import { INTERVAL_LABELS, type Interval } from '../utils/period'
import type { PeriodFilterState } from '../utils/usePeriodFilter'

const useStyles = makeStyles({
  field: { display: 'flex', flexDirection: 'column', gap: '4px' },
  label: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
  dropdown: { minWidth: '160px' },
})

/**
 * "Filter by period" control: a relative-range dropdown (all / last day / week / month) plus a pair
 * of datetime inputs shown only for a custom range. Renders as bare fields so it drops into an
 * existing filters row. `onChange` fires whenever the selection changes (e.g. to reset paging).
 */
export function PeriodFilter({ state, onChange }: { state: PeriodFilterState; onChange?: () => void }) {
  const s = useStyles()

  return (
    <>
      <div className={s.field}>
        <span className={s.label}>Period</span>
        <Dropdown
          className={s.dropdown}
          size="small"
          selectedOptions={[state.interval]}
          value={INTERVAL_LABELS[state.interval]}
          onOptionSelect={(_, d) => {
            state.setInterval((d.optionValue as Interval) ?? 'all')
            onChange?.()
          }}
        >
          {(Object.keys(INTERVAL_LABELS) as Interval[]).map(k => (
            <Option key={k} value={k}>{INTERVAL_LABELS[k]}</Option>
          ))}
        </Dropdown>
      </div>
      {state.interval === 'custom' && (
        <>
          <div className={s.field}>
            <span className={s.label}>From</span>
            <Input
              type="datetime-local"
              size="small"
              value={state.customFrom}
              onChange={(_, v) => { state.setCustomFrom(v.value); onChange?.() }}
            />
          </div>
          <div className={s.field}>
            <span className={s.label}>To</span>
            <Input
              type="datetime-local"
              size="small"
              value={state.customTo}
              onChange={(_, v) => { state.setCustomTo(v.value); onChange?.() }}
            />
          </div>
        </>
      )}
    </>
  )
}
