import { useEffect, useState, type ReactNode } from 'react'
import {
  Button, Dialog, DialogActions, DialogBody, DialogContent, DialogSurface, DialogTitle,
  MessageBar, MessageBarBody, Spinner, makeStyles, tokens,
} from '@fluentui/react-components'
import { CheckmarkCircle20Filled } from '@fluentui/react-icons'
import { useTranslation } from 'react-i18next'

const useStyles = makeStyles({
  surface: { minWidth: '560px', maxWidth: '640px' },
  stepper: { display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '6px', marginBottom: '16px' },
  step: { display: 'flex', alignItems: 'center', gap: '6px', fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
  stepActive: { color: tokens.colorBrandForeground1, fontWeight: 600 },
  stepDone: { color: tokens.colorNeutralForeground2 },
  stepDot: {
    display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
    width: '20px', height: '20px', borderRadius: '50%', fontSize: '11px', fontWeight: 600,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  stepDotActive: { border: `1px solid ${tokens.colorBrandStroke1}`, color: tokens.colorBrandForeground1 },
  stepDotDone: { backgroundColor: tokens.colorBrandBackground, color: tokens.colorNeutralForegroundOnBrand, border: `1px solid ${tokens.colorBrandBackground}` },
  sep: { color: tokens.colorNeutralForeground4 },
  body: { display: 'flex', flexDirection: 'column', gap: '12px', minHeight: '160px' },
  stepTitle: { fontWeight: 600 },
  stepDescription: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  resultBody: { display: 'flex', flexDirection: 'column', gap: '12px', alignItems: 'stretch' },
  resultHead: { display: 'flex', alignItems: 'center', gap: '8px', color: tokens.colorStatusSuccessForeground1, fontWeight: 600 },
})

/** One step in a {@link Wizard}. */
export interface WizardStep {
  /** Stable key. */
  id: string
  /** Short heading shown in the stepper and above the body. */
  title: string
  /** Optional one-line description under the step title. */
  description?: string
  /** The step's body content. */
  content: ReactNode
  /** When false, the Next/Finish button is disabled. Defaults to true. */
  canProceed?: boolean
}

/**
 * Generic multi-step wizard rendered inside a Fluent `Dialog`. It owns nothing domain-specific:
 * callers pass an ordered list of {@link WizardStep}s (each supplying its own body and a
 * `canProceed` gate) plus an async `onFinish`. The component handles the stepper header, the
 * Back/Next/Finish flow, the busy state while finishing, an error bar, and a terminal "done"
 * screen when `result` is provided. Reuse it for any "fill a few steps, then do something" task.
 */
export function Wizard({
  open, title, steps, onClose, onFinish,
  finishLabel, busy = false, error, result,
}: {
  open: boolean
  title: string
  steps: WizardStep[]
  onClose: () => void
  onFinish: () => void | Promise<void>
  /** Label for the button on the last step. */
  finishLabel?: string
  /** When true, the footer shows a spinner and disables navigation (e.g. while `onFinish` runs). */
  busy?: boolean
  /** Optional error message rendered under the body. */
  error?: string | null
  /** When set, replaces the step body with a terminal success view; only a Close button remains. */
  result?: ReactNode
}) {
  const s = useStyles()
  const { t } = useTranslation()
  const [index, setIndex] = useState(0)

  // Reset to the first step whenever the wizard (re)opens so a second run starts clean.
  useEffect(() => {
    // Resetting on open is the effect's whole purpose.
    /* eslint-disable-next-line react-hooks/set-state-in-effect */
    if (open) setIndex(0)
  }, [open])

  const isLast = index === steps.length - 1
  const step = steps[index]
  const canProceed = step?.canProceed !== false
  const showingResult = result != null

  function next() {
    if (isLast) { void onFinish(); return }
    setIndex(i => Math.min(i + 1, steps.length - 1))
  }
  function back() {
    setIndex(i => Math.max(i - 1, 0))
  }

  return (
    <Dialog open={open} onOpenChange={(_, d) => { if (!d.open) onClose() }}>
      <DialogSurface className={s.surface}>
        <DialogBody>
          <DialogTitle>{title}</DialogTitle>
          <DialogContent>
            {!showingResult && (
              <div className={s.stepper} aria-hidden>
                {steps.map((st, i) => {
                  const state = i === index ? 'active' : i < index ? 'done' : 'todo'
                  return (
                    <span key={st.id} className={s.step}>
                      {i > 0 && <span className={s.sep}>›</span>}
                      <span
                        className={`${s.stepDot} ${state === 'active' ? s.stepDotActive : ''} ${state === 'done' ? s.stepDotDone : ''}`}
                      >
                        {state === 'done' ? '✓' : i + 1}
                      </span>
                      <span className={state === 'active' ? s.stepActive : state === 'done' ? s.stepDone : undefined}>
                        {st.title}
                      </span>
                    </span>
                  )
                })}
              </div>
            )}

            {showingResult ? (
              <div className={s.resultBody}>{result}</div>
            ) : (
              <div className={s.body}>
                <div>
                  <div className={s.stepTitle}>{step?.title}</div>
                  {step?.description && <div className={s.stepDescription}>{step.description}</div>}
                </div>
                {step?.content}
              </div>
            )}

            {error && (
              <MessageBar intent="error" style={{ marginTop: 12 }}>
                <MessageBarBody>{error}</MessageBarBody>
              </MessageBar>
            )}
          </DialogContent>
          <DialogActions>
            {showingResult ? (
              <Button appearance="primary" onClick={onClose}>{t('shell.common.close')}</Button>
            ) : (
              <>
                <Button appearance="secondary" onClick={onClose} disabled={busy}>
                  {t('shell.common.cancel')}
                </Button>
                {index > 0 && (
                  <Button appearance="secondary" onClick={back} disabled={busy}>
                    {t('shell.wizard.back')}
                  </Button>
                )}
                <Button
                  appearance="primary"
                  onClick={next}
                  disabled={!canProceed || busy}
                  icon={busy ? <Spinner size="tiny" /> : undefined}
                >
                  {isLast ? (finishLabel ?? t('shell.wizard.finish')) : t('shell.wizard.next')}
                </Button>
              </>
            )}
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

/** Header for the wizard's terminal success screen — a green check plus a headline. */
export function WizardResultHeader({ children }: { children: ReactNode }) {
  const s = useStyles()
  return (
    <div className={s.resultHead}>
      <CheckmarkCircle20Filled />
      <span>{children}</span>
    </div>
  )
}
