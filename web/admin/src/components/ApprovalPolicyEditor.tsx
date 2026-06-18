import { useMemo } from 'react'
import {
  Dropdown, Field, MessageBar, MessageBarBody, MessageBarTitle, Option, Radio, RadioGroup,
  makeStyles, tokens,
} from '@fluentui/react-components'
import { approverFromKey, approverKey, approverLabel, SERVICE_OWNER_KEY, SERVICE_OWNER_LABEL } from '../utils/approvers'
import type {
  Account, ApprovalMode, ApprovalPolicy, ApprovalSourceScope, ApproverRequirement,
} from '../api/types'

const approvalModeLabels: Record<ApprovalMode, string> = {
  None: 'No approval required',
  UseGlobalDefault: 'Use the global default',
  Required: 'Approval required',
}

const approvalSourceLabels: Record<ApprovalSourceScope, string> = {
  Both: 'Both manual and API submissions',
  ManualOnly: 'Manual (web console) submissions only',
  ApiOnly: 'API submissions only',
}

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
  policy, accounts, onChange, disabled, heading = 'Approval', modifiableWarning = false,
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
      {heading && <div className={s.sectionLabel}>{heading}</div>}
      <Field
        label="Approval mode"
        hint="Submissions in scope are held as Pending until approved, and excluded from the OData feed and Explore until then. Defaults to no approval for backwards compatibility."
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
        <Field label="Applies to" hint="You can require approval for only manual entries, only API submissions, or both.">
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
            This follows the global default approval policy, configured in Settings → Approval.
          </MessageBarBody>
        </MessageBar>
      )}

      {mode === 'Required' && (
        <Field
          label="Approvers"
          hint="Pick who may review: the Approver/Admin accounts below, and/or the service owner (the account that sent the submission, so a service can sign off on its own data). Mark at least one as Required; the submission goes live once every Required approver has approved."
          validationState={hasRequiredApprover ? 'none' : 'warning'}
          validationMessage={hasRequiredApprover ? undefined : 'Add at least one Required approver.'}
        >
          <Dropdown
            multiselect
            disabled={disabled}
            placeholder="Select approvers"
            selectedOptions={approvers.map(approverKey)}
            value={approvers.map(a => approverLabel(a, accountsById)).join(', ')}
            onOptionSelect={(_, d) => toggleApprover(d.optionValue!, d.selectedOptions.includes(d.optionValue!))}
          >
            <Option value={SERVICE_OWNER_KEY}>{SERVICE_OWNER_LABEL}</Option>
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
                <span className={s.approverName}>{approverLabel(a, accountsById)}</span>
                <RadioGroup
                  layout="horizontal"
                  disabled={disabled}
                  value={a.requirement}
                  onChange={(_, d) => setRequirement(key, d.value as ApproverRequirement)}
                >
                  <Radio value="Required" label="Required" />
                  <Radio value="Optional" label="Optional" />
                </RadioGroup>
              </div>
            )
          })}
        </div>
      )}

      {modifiableWarning && mode !== 'None' && (
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>Heads up: this schema is modifiable</MessageBarTitle>
            Re-submitting data for a window that already has a submission replaces it and resets its
            approval status to Pending — even if it was previously approved. While it waits for
            re-approval it drops out of the OData feed and Explore. If you don’t want re-submissions to
            disturb approved data, mark the schema as not modifiable.
          </MessageBarBody>
        </MessageBar>
      )}
    </>
  )
}
