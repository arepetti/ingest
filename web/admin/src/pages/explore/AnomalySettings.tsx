import {
  Button, Dropdown, Option, Popover, PopoverSurface, PopoverTrigger, Switch, Text,
  Tooltip as FluentTooltip,
} from '@fluentui/react-components'
import { ChevronDown20Regular, Info16Regular } from '@fluentui/react-icons'
import { ANOMALY_THRESHOLDS, ANOMALY_WINDOWS, useExploreStyles } from './shared'

const ROBUST_HELP =
  'Robust mode resists a few extreme spikes skewing the baseline. Standard mode uses the mean and standard deviation.'

interface Tuning {
  window: number
  threshold: number
  robust: boolean
  onWindow: (n: number) => void
  onThreshold: (n: number) => void
  onRobust: (b: boolean) => void
}

/**
 * The anomaly detector's tuning controls (history window, sensitivity, robust toggle) as a row of
 * standalone fields. Used inline under the Anomalies board's filter dropdowns, and reused inside the
 * Trend view's popover.
 */
export function AnomalyFields({ window, threshold, robust, onWindow, onThreshold, onRobust, disabled }: Tuning & {
  disabled?: boolean
}) {
  const styles = useExploreStyles()
  return (
    <>
      <div className={styles.field}>
        <span className={styles.fieldLabel}>History window (periods)</span>
        <Dropdown
          className={styles.dropdown}
          disabled={disabled}
          selectedOptions={[String(window)]}
          value={String(window)}
          onOptionSelect={(_, d) => d.optionValue && onWindow(Number(d.optionValue))}
        >
          {ANOMALY_WINDOWS.map(w => <Option key={w} value={String(w)}>{String(w)}</Option>)}
        </Dropdown>
      </div>

      <div className={styles.field}>
        <span className={styles.fieldLabel}>Sensitivity (|z| threshold)</span>
        <Dropdown
          className={styles.dropdown}
          disabled={disabled}
          selectedOptions={[String(threshold)]}
          value={String(threshold)}
          onOptionSelect={(_, d) => d.optionValue && onThreshold(Number(d.optionValue))}
        >
          {ANOMALY_THRESHOLDS.map(t => <Option key={t} value={String(t)}>{String(t)}</Option>)}
        </Dropdown>
      </div>

      <div className={styles.field}>
        <span className={styles.fieldLabel}>
          Baseline mode
          <FluentTooltip relationship="description" content={ROBUST_HELP}>
            <Info16Regular className={styles.infoIcon} tabIndex={0} aria-label="What does Robust mode do?" />
          </FluentTooltip>
        </span>
        <Dropdown
          className={styles.dropdown}
          disabled={disabled}
          selectedOptions={[robust ? 'robust' : 'standard']}
          value={robust ? 'Robust (median / MAD)' : 'Standard (mean / SD)'}
          onOptionSelect={(_, d) => d.optionValue && onRobust(d.optionValue === 'robust')}
        >
          <Option value="standard">Standard (mean / SD)</Option>
          <Option value="robust">Robust (median / MAD)</Option>
        </Dropdown>
      </div>
    </>
  )
}

/**
 * The Trend view's "Anomalies" dropdown button: a popover with an enable/disable switch on top and
 * the detector tuning below (greyed out while off), so the Trend toolbar has one control for both
 * enabling and tuning the highlight.
 */
export function AnomalySettings({ enabled, onToggleEnabled, ...tuning }: Tuning & {
  enabled: boolean
  onToggleEnabled: (b: boolean) => void
}) {
  const styles = useExploreStyles()
  return (
    <Popover positioning="below-end" withArrow>
      <PopoverTrigger disableButtonEnhancement>
        <Button appearance="outline" size="medium" iconPosition="after" icon={<ChevronDown20Regular />}>
          Anomalies
        </Button>
      </PopoverTrigger>
      <PopoverSurface>
        <div className={styles.popover}>
          <Text weight="semibold">Anomaly detection</Text>
          <Switch
            label="Highlight anomalies"
            checked={enabled}
            onChange={(_, d) => onToggleEnabled(!!d.checked)}
          />
          {/* Tuning doesn't do anything while the highlight is off — grey it out. */}
          <AnomalyFields {...tuning} disabled={!enabled} />
        </div>
      </PopoverSurface>
    </Popover>
  )
}
