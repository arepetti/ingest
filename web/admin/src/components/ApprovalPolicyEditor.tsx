import { useMemo } from 'react'
import {
  Dropdown, Field, MessageBar, MessageBarBody, MessageBarTitle, Option, Radio, RadioGroup,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { approverFromKey, approverKey, approverLabel, SERVICE_OWNER_KEY } from '../utils/approvers'
import type {
  Account, ApprovalMode, ApprovalPolicy, ApprovalSourceScope, ApproverRequirement,
} from '../api/types'
import { useTranslation } from 'react-i18next'

const useStyles = makeStyles({
  sectionLabel: { fontWeight: tokens.fontWeightSemibold, marginTop: '4px' },
  approverList: { display: 'flex', flexDirection: 'column', gap: '4px' },
  approverRow: {
    display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px',
    padding: '6px 10px', borderRadius: '6px', backgroundColor: tokens.colorNeutralBackground2,
  },
  approverName: { fontWeight: tokens.fontWeightSemibold },
})

/**
 * Reusable approval-policy editor. A policy has a mode (none / defer to global / required), the
 * source scope it applies to, and — when `Required` — a set of designated approvers each marked
 * Required or Optional. At least one Required approver is needed; the server enforces this and we
 * surface a hint when the rule isn't met yet. Shared by the schema editor and the approval-rules
 * drawer so the two stay consistent.
 */
export function ApprovalPolicyEditor({
  policy, accounts, onChange, disabled, heading, modifiableWarning = false,
}: {
  policy: ApprovalPolicy | null
  accounts: Account[]
  onChange: (patch: Partial<ApprovalPolicy>) => void
  disabled?: boolean
  /** Heading shown above the editor; pass `null` to omit it. */
  heading?: string | null
  /** Show the "this schema is modifiable" data-loss caution (only relevant in the schema editor). */
  modifiableWarning?: boolean
}) {
  const s = useStyles()
  const { t } = useTranslation()
  const approvalModeLabels: Record<ApprovalMode, string> = {
    None: t('schemasSubmissions.approval.mode.none'),
    UseGlobalDefault: t('schemasSubmissions.approval.mode.globalDefault'),
    Required: t('schemasSubmissions.approval.mode.required'),
  }
  const approvalSourceLabels: Record<ApprovalSourceScope, string> = {
    Both: t('schemasSubmissions.approval.source.both'),
    ManualOnly: t('schemasSubmissions.approval.source.manualOnly'),
    ApiOnly: t('schemasSubmissions.approval.source.apiOnly'),
  }
  const mode: ApprovalMode = policy?.mode ?? 'None'
  const appliesToSources: ApprovalSourceScope = policy?.appliesToSources ?? 'Both'
  const approvers = policy?.approvers ?? []
  const accountsById = useMemo(() => new Map(accounts.map(a => [a.id, a])), [accounts])
  const hasRequiredApprover = approvers.some(a => a.requirement === 'Required')

  function setApprovers(next: ApprovalPolicy['approvers']) {
    onChange({ approvers: next })
  }
  function toggleApprover(key: string, selected: boolean) {
    if (selected) {
      if (approvers.some(a => approverKey(a) === key)) return
      setApprovers([...approvers, approverFromKey(key)])
    } else {
      setApprovers(approvers.filter(a => approverKey(a) !== key))
    }
  }
  function setRequirement(key: string, requirement: ApproverRequirement) {
    setApprovers(approvers.map(a => approverKey(a) === key ? { ...a, requirement } : a))
  }

  return (
    <>
      {heading !== null && <div className={s.sectionLabel}>{heading ?? t('schemasSubmissions.approval.heading')}</div>}
      <Field
        label={t('schemasSubmissions.approval.modeLabel')}
        hint={t('schemasSubmissions.approval.modeHint')}
      >
        <Dropdown
          disabled={disabled}
          value={approvalModeLabels[mode]}
          selectedOptions={[mode]}
          onOptionSelect={(_, d) => onChange({ mode: d.optionValue as ApprovalMode })}
        >
          {(Object.keys(approvalModeLabels) as ApprovalMode[]).map(m => (
            <Option key={m} value={m}>{approvalModeLabels[m]}</Option>
          ))}
        </Dropdown>
      </Field>

      {mode !== 'None' && (
        <Field label={t('schemasSubmissions.approval.appliesTo')} hint={t('schemasSubmissions.approval.appliesToHint')}>
          <Dropdown
            disabled={disabled}
            value={approvalSourceLabels[appliesToSources]}
            selectedOptions={[appliesToSources]}
            onOptionSelect={(_, d) => onChange({ appliesToSources: d.optionValue as ApprovalSourceScope })}
          >
            {(Object.keys(approvalSourceLabels) as ApprovalSourceScope[]).map(sc => (
              <Option key={sc} value={sc}>{approvalSourceLabels[sc]}</Option>
            ))}
          </Dropdown>
        </Field>
      )}

      {mode === 'UseGlobalDefault' && (
        <MessageBar intent="info">
          <MessageBarBody>
            {t('schemasSubmissions.approval.globalDefaultHelp')}
          </MessageBarBody>
        </MessageBar>
      )}

      {mode === 'Required' && (
        <Field
          label={t('schemasSubmissions.approval.approvers')}
          hint={t('schemasSubmissions.approval.approversHint')}
          validationState={hasRequiredApprover ? 'none' : 'warning'}
          validationMessage={hasRequiredApprover ? undefined : t('schemasSubmissions.approval.requiredValidation')}
        >
          <Dropdown
            multiselect
            disabled={disabled}
            placeholder={t('schemasSubmissions.approval.selectApprovers')}
            selectedOptions={approvers.map(approverKey)}
            value={approvers.map(a => approverLabel(a, accountsById, t)).join(', ')}
            onOptionSelect={(_, d) => toggleApprover(d.optionValue!, d.selectedOptions.includes(d.optionValue!))}
          >
            <Option value={SERVICE_OWNER_KEY}>{t('schemasSubmissions.approval.serviceOwner')}</Option>
            {accounts.map(a => (
              <Option key={a.id} value={a.id}>{a.label || a.name}</Option>
            ))}
          </Dropdown>
        </Field>
      )}

      {mode === 'Required' && approvers.length > 0 && (
        <div className={s.approverList}>
          {approvers.map(a => {
            const key = approverKey(a)
            return (
              <div key={key} className={s.approverRow}>
                <span className={s.approverName}>{approverLabel(a, accountsById, t)}</span>
                <RadioGroup
                  layout="horizontal"
                  disabled={disabled}
                  value={a.requirement}
                  onChange={(_, d) => setRequirement(key, d.value as ApproverRequirement)}
                >
                  <Radio value="Required" label={t('schemasSubmissions.approval.requirement.required')} />
                  <Radio value="Optional" label={t('schemasSubmissions.approval.requirement.optional')} />
                </RadioGroup>
              </div>
            )
          })}
        </div>
      )}

      {modifiableWarning && mode !== 'None' && (
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>{t('schemasSubmissions.approval.modifiableTitle')}</MessageBarTitle>
            {t('schemasSubmissions.approval.modifiableHelp')}
          </MessageBarBody>
        </MessageBar>
      )}
    </>
  )
}
