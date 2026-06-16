import { useMemo, useState } from 'react'
import {
  Badge, Body1, Button, Checkbox, Dropdown, Field, Input, MessageBar, MessageBarBody,
  MessageBarTitle, Option, Tooltip, makeStyles, tokens,
} from '@fluentui/react-components'
import { Copy20Regular } from '@fluentui/react-icons'
import { Wizard, WizardResultHeader, type WizardStep } from './Wizard'
import { useCreateAccount, useRotateApiKey } from '../api/hooks'
import { formatApiError } from '../api/client'
import type { Account, AccountKind, AccountRole, CreateAccountRequest, GeneratedApiKey } from '../api/types'

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: '12px' },
  twoCol: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', alignItems: 'start' },
  hint: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  roleRow: { display: 'flex', alignItems: 'center', gap: '8px' },
  key: {
    fontFamily: tokens.fontFamilyMonospace, backgroundColor: tokens.colorNeutralBackground3,
    padding: '12px', borderRadius: '4px', wordBreak: 'break-all',
  },
  keyRow: { display: 'flex', alignItems: 'flex-start', gap: '8px' },
  summary: { display: 'flex', flexDirection: 'column', gap: '4px' },
})

const KIND_HINTS: Record<AccountKind, string> = {
  Application: 'API-only credential (cannot log in to the UI).',
  User: 'Interactive account (can log in to the UI and call APIs).',
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** A date `years`/`days` from today, formatted for a native date input (YYYY-MM-DD). */
function dateInputOffset(years: number, days = 0): string {
  const d = new Date()
  d.setFullYear(d.getFullYear() + years)
  d.setDate(d.getDate() + days)
  return d.toISOString().slice(0, 10)
}

interface OnboardResult {
  account: Account
  /** The freshly issued key plaintext, or null when no key was generated. */
  plaintext: string | null
}

/**
 * Reusable "onboard a new account" wizard built on the generic {@link Wizard}. Configured by
 * `role` (Service / Operator / Admin), it walks through account details, an optional API key, and
 * a result screen that surfaces the one-time key plaintext. The same component backs the dashboard
 * "Onboard new" tasks for both Services and Operators — point it at a different role to reuse it.
 *
 * Note: a future "both User and Application" mode (creating two linked accounts with distinct keys)
 * can be layered on by extending the Kind step; today the wizard creates a single account.
 */
export function OnboardAccountWizard({
  open, onClose, role, title,
  defaultKind = 'Application',
  lockKind = false,
  defaultGenerateKey = true,
}: {
  open: boolean
  onClose: () => void
  /** Role the new account is created with (fixed for the task). */
  role: AccountRole
  /** Dialog title, e.g. "Onboard a new service". */
  title: string
  /** Initial Kind selection. */
  defaultKind?: AccountKind
  /** When true, the Kind can't be changed (the task dictates it). */
  lockKind?: boolean
  /** Whether the "generate an API key now" box starts checked. */
  defaultGenerateKey?: boolean
}) {
  const s = useStyles()
  const create = useCreateAccount()
  const rotate = useRotateApiKey()

  const [name, setName] = useState('')
  const [label, setLabel] = useState('')
  const [email, setEmail] = useState('')
  const [kind, setKind] = useState<AccountKind>(defaultKind)
  const [generateKey, setGenerateKey] = useState(defaultGenerateKey)
  const [expiry, setExpiry] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<OnboardResult | null>(null)
  const [copied, setCopied] = useState(false)

  const minExpiry = dateInputOffset(0, 1)
  const maxExpiry = dateInputOffset(2)

  const trimmedName = name.trim()
  const trimmedEmail = email.trim()
  const detailsValid = trimmedName.length > 0 && EMAIL_RE.test(trimmedEmail)

  // Reset all local state when the dialog closes so the next launch is clean.
  function handleClose() {
    onClose()
    setName(''); setLabel(''); setEmail('')
    setKind(defaultKind); setGenerateKey(defaultGenerateKey); setExpiry('')
    setError(null); setResult(null); setCopied(false)
  }

  async function onFinish() {
    setError(null)
    try {
      const req: CreateAccountRequest = {
        name: trimmedName,
        label: label.trim() || null,
        email: trimmedEmail,
        kind,
        role,
        enabled: true,
      }
      const account = await create.mutateAsync(req)

      let plaintext: string | null = null
      if (generateKey) {
        const expiresAt = expiry ? new Date(`${expiry}T23:59:59.000Z`).toISOString() : null
        const generated: GeneratedApiKey = await rotate.mutateAsync({ accountId: account.id, expiresAt })
        plaintext = generated.plaintext
      }
      setResult({ account, plaintext })
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  async function copyKey() {
    if (!result?.plaintext) return
    try {
      await navigator.clipboard.writeText(result.plaintext)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    } catch { /* clipboard may be blocked; the value is selectable above */ }
  }

  const steps = useMemo<WizardStep[]>(() => [
    {
      id: 'details',
      title: 'Account details',
      description: `Create the new ${role.toLowerCase()} account.`,
      canProceed: detailsValid,
      content: (
        <div className={s.form}>
          <div className={s.roleRow}>
            <span className={s.hint}>Role</span>
            <Badge appearance="tint" color="brand">{role}</Badge>
          </div>
          <div className={s.twoCol}>
            <Field label="Name" required hint="Stable machine-style identifier, globally unique.">
              <Input value={name} onChange={(_, d) => setName(d.value)} placeholder="e.g. acme_logistics" />
            </Field>
            <Field label="Label">
              <Input value={label} onChange={(_, d) => setLabel(d.value)} placeholder="Friendly display name" />
            </Field>
          </div>
          <Field
            label="Email"
            required
            validationState={trimmedEmail === '' || EMAIL_RE.test(trimmedEmail) ? 'none' : 'error'}
            validationMessage={trimmedEmail === '' || EMAIL_RE.test(trimmedEmail) ? undefined : 'Enter a valid email address.'}
          >
            <Input type="email" value={email} onChange={(_, d) => setEmail(d.value)} placeholder="contact@example.com" />
          </Field>
          <Field label="Kind" hint={KIND_HINTS[kind]}>
            <Dropdown
              disabled={lockKind}
              value={kind}
              selectedOptions={[kind]}
              onOptionSelect={(_, d) => setKind((d.optionValue as AccountKind) ?? kind)}
            >
              <Option value="Application">Application</Option>
              <Option value="User">User</Option>
            </Dropdown>
          </Field>
        </div>
      ),
    },
    {
      id: 'key',
      title: 'API key',
      description: 'Optionally issue an API key so the account can call the ingest API right away.',
      content: (
        <div className={s.form}>
          <Checkbox
            label="Generate an API key now"
            checked={generateKey}
            onChange={(_, d) => setGenerateKey(!!d.checked)}
          />
          {kind === 'Application' && !generateKey && (
            <MessageBar intent="warning">
              <MessageBarBody>
                An Application account can only authenticate with an API key. Without one it won't be
                able to submit data until a key is issued later.
              </MessageBarBody>
            </MessageBar>
          )}
          {generateKey && (
            <Field label="Key expiry (optional)" hint="Leave blank for a key that never expires. Maximum two years from today.">
              <Input type="date" value={expiry} min={minExpiry} max={maxExpiry} onChange={(_, d) => setExpiry(d.value)} />
            </Field>
          )}
        </div>
      ),
    },
  ], [s, role, name, label, email, kind, generateKey, expiry, detailsValid, trimmedEmail, lockKind, minExpiry, maxExpiry])

  const busy = create.isPending || rotate.isPending

  const resultView = result ? (
    <>
      <WizardResultHeader>
        {role} “{result.account.label || result.account.name}” created
      </WizardResultHeader>
      <div className={s.summary}>
        <Body1><strong>Name:</strong> {result.account.name}</Body1>
        <Body1><strong>Kind:</strong> {result.account.kind} · <strong>Role:</strong> {result.account.role}</Body1>
        {result.account.email && <Body1><strong>Email:</strong> {result.account.email}</Body1>}
      </div>
      {result.plaintext ? (
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>Copy this API key now — it will not be shown again.</MessageBarTitle>
            <div className={s.keyRow}>
              <div className={s.key}>{result.plaintext}</div>
              <Tooltip content={copied ? 'Copied!' : 'Copy to clipboard'} relationship="label">
                <Button appearance="subtle" icon={<Copy20Regular />} onClick={copyKey} aria-label="Copy API key" />
              </Tooltip>
            </div>
          </MessageBarBody>
        </MessageBar>
      ) : (
        <Body1 className={s.hint}>No API key was generated. You can issue one later from the account's API keys dialog.</Body1>
      )}
    </>
  ) : undefined

  return (
    <Wizard
      open={open}
      title={title}
      steps={steps}
      onClose={handleClose}
      onFinish={onFinish}
      finishLabel="Create account"
      busy={busy}
      error={error}
      result={resultView}
    />
  )
}
