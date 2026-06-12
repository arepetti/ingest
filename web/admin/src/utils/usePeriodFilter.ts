import { useMemo, useState } from 'react'
import { intervalRange, type Interval } from './period'

/** State + resolved range produced by {@link usePeriodFilter}, passed to the `PeriodFilter` control. */
export type PeriodFilterState = {
  interval: Interval
  setInterval: (value: Interval) => void
  customFrom: string
  setCustomFrom: (value: string) => void
  customTo: string
  setCustomTo: (value: string) => void
  /** Resolved lower bound (UTC ISO) for the current selection, or undefined. */
  from?: string
  /** Resolved upper bound (UTC ISO) for the current selection, or undefined. */
  to?: string
}

/** Owns the interval + custom-range state for a "filter by period" control and resolves it to from/to. */
export function usePeriodFilter(initial: Interval = 'all'): PeriodFilterState {
  const [interval, setInterval] = useState<Interval>(initial)
  const [customFrom, setCustomFrom] = useState('')
  const [customTo, setCustomTo] = useState('')
  const { from, to } = useMemo(
    () => intervalRange(interval, customFrom, customTo),
    [interval, customFrom, customTo],
  )
  return { interval, setInterval, customFrom, setCustomFrom, customTo, setCustomTo, from, to }
}
