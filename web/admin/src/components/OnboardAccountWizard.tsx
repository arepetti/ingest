import { useMemo, useState } from 'react'
import {
  Badge, Body1, Button, Checkbox, Dropdown, Field, Input, MessageBar, MessageBarBody,
  MessageBarTitle, Option, Tooltip, makeStyles, tokens,
} from '@fluentui/react-components'
import { Copy20Regular } from '@fluentui/react-icons'
import { Wizard, WizardResultHeader, type WizardStep } from './Wizard'
import { useAccounts, useCreateAccount, useRotateApiKey } from '../api/hooks'
import { formatApiError } from '../api/client'
import type { Account, AccountKind, AccountRole, CreateAccountRequest, GeneratedApiKey } from '../api/types'
import { useTranslation } from 'react-i18next'

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
  const { t } = useTranslation()
  const create = useCreateAccount()
  const rotate = useRotateApiKey()
  // Per-service scope only applies to back-office roles (Admins are always unrestricted; Service
  // accounts only see their own data), so the roster + scope step are limited to those.
  const scopeApplies = role !== 'Admin' && role !== 'Service'
  const { data: serviceAccounts } = useAccounts({ role: 'Service' }, scopeApplies)

  const [name, setName] = useState('')
  const [label, setLabel] = useState('')
  const [email, setEmail] = useState('')
  const [kind, setKind] = useState<AccountKind>(defaultKind)
  const [generateKey, setGenerateKey] = useState(defaultGenerateKey)
  const [expiry, setExpiry] = useState('')
  const [keyDescription, setKeyDescription] = useState('')
  const [assignedServiceIds, setAssignedServiceIds] = useState<string[]>([])
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
    setKind(defaultKind); setGenerateKey(defaultGenerateKey); setExpiry(''); setKeyDescription('')
    setAssignedServiceIds([])
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
        // Empty = unrestricted; non-empty confines the account to those services. Only sent for
        // back-office roles (ignored server-side for Admin/Service anyway).
        ...(scopeApplies ? { assignedServiceIds } : {}),
      }
      const account = await create.mutateAsync(req)

      let plaintext: string | null = null
      if (generateKey) {
        const expiresAt = expiry ? new Date(`${expiry}T23:59:59.000Z`).toISOString() : null
        const generated: GeneratedApiKey = await rotate.mutateAsync({ accountId: account.id, expiresAt, description: keyDescription.trim() || null })
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
      title: t('accounts.onboarding.details.title'),
      description: t('accounts.onboarding.details.description', { role: t(`accounts.roles.${role}`) }),
      canProceed: detailsValid,
      content: (
        <div className={s.form}>
          <div className={s.roleRow}>
            <span className={s.hint}>{t('accounts.fields.role')}</span>
            <Badge appearance="tint" color="brand">{t(`accounts.roles.${role}`)}</Badge>
          </div>
          <div className={s.twoCol}>
            <Field label={t('accounts.fields.name')} required hint={t('accounts.onboarding.details.nameHint')}>
              <Input value={name} onChange={(_, d) => setName(d.value)} placeholder={t('accounts.onboarding.details.namePlaceholder')} />
            </Field>
            <Field label={t('accounts.fields.label')}>
              <Input value={label} onChange={(_, d) => setLabel(d.value)} placeholder={t('accounts.onboarding.details.labelPlaceholder')} />
            </Field>
          </div>
          <Field
            label={t('accounts.fields.email')}
            required
            validationState={trimmedEmail === '' || EMAIL_RE.test(trimmedEmail) ? 'none' : 'error'}
            validationMessage={trimmedEmail === '' || EMAIL_RE.test(trimmedEmail) ? undefined : t('accounts.validation.emailInvalid')}
          >
            <Input type="email" value={email} onChange={(_, d) => setEmail(d.value)} placeholder={t('accounts.onboarding.details.emailPlaceholder')} />
          </Field>
          <Field label={t('accounts.fields.kind')} hint={t(`accounts.onboarding.kindHints.${kind}`)}>
            <Dropdown
              disabled={lockKind}
              value={t(`accounts.kinds.${kind}`)}
              selectedOptions={[kind]}
              onOptionSelect={(_, d) => setKind((d.optionValue as AccountKind) ?? kind)}
            >
              <Option value="Application">{t('accounts.kinds.Application')}</Option>
              <Option value="User">{t('accounts.kinds.User')}</Option>
            </Dropdown>
          </Field>
        </div>
      ),
    },
    ...(scopeApplies ? [{
      id: 'scope',
      title: t('accounts.scope.title'),
      description: t('accounts.onboarding.scope.description', { role: t(`accounts.roles.${role}`) }),
      content: (
        <div className={s.form}>
          <Field
            label={t('accounts.onboarding.scope.visibleServices')}
            hint={t('accounts.onboarding.scope.hint')}
          >
            <Dropdown
              multiselect
              placeholder={t('accounts.scope.allServices')}
              selectedOptions={assignedServiceIds}
              value={
                assignedServiceIds.length === 0
                  ? t('accounts.scope.allServices')
                  : (serviceAccounts?.items ?? [])
                      .filter(a => assignedServiceIds.includes(a.id))
                      .map(a => a.label || a.name)
                      .join(', ')
              }
              onOptionSelect={(_, d) => setAssignedServiceIds(d.selectedOptions)}
            >
              {(serviceAccounts?.items ?? []).filter(a => a.enabled || assignedServiceIds.includes(a.id)).map(a => (
                <Option key={a.id} value={a.id}>{a.label || a.name}</Option>
              ))}
            </Dropdown>
          </Field>
        </div>
      ),
    } as WizardStep] : []),
    {
      id: 'key',
      title: t('accounts.onboarding.key.title'),
      description: t('accounts.onboarding.key.description'),
      content: (
        <div className={s.form}>
          <Checkbox
            label={t('accounts.onboarding.key.generateNow')}
            checked={generateKey}
            onChange={(_, d) => setGenerateKey(!!d.checked)}
          />
          {kind === 'Application' && !generateKey && (
            <MessageBar intent="warning">
              <MessageBarBody>
                {t('accounts.onboarding.key.applicationWarning')}
              </MessageBarBody>
            </MessageBar>
          )}
          {generateKey && (
            <>
              <Field label={t('accounts.onboarding.key.descriptionLabel')} hint={t('accounts.onboarding.key.descriptionHint')}>
                <Input value={keyDescription} maxLength={200} placeholder={t('accounts.keys.descriptionPlaceholder')} onChange={(_, d) => setKeyDescription(d.value)} />
              </Field>
              <Field label={t('accounts.onboarding.key.expiryLabel')} hint={t('accounts.keys.expiryHint')}>
                <Input type="date" value={expiry} min={minExpiry} max={maxExpiry} onChange={(_, d) => setExpiry(d.value)} />
              </Field>
            </>
          )}
        </div>
      ),
    },
  ], [s, t, role, name, label, email, kind, generateKey, expiry, keyDescription, detailsValid, trimmedEmail, lockKind, minExpiry, maxExpiry, scopeApplies, assignedServiceIds, serviceAccounts])

  const busy = create.isPending || rotate.isPending

  const resultView = result ? (
    <>
      <WizardResultHeader>
        {t('accounts.onboarding.result.created', {
          role: t(`accounts.roles.${role}`),
          name: result.account.label || result.account.name,
        })}
      </WizardResultHeader>
      <div className={s.summary}>
        <Body1><strong>{t('accounts.fields.name')}:</strong> {result.account.name}</Body1>
        <Body1>
          <strong>{t('accounts.fields.kind')}:</strong> {t(`accounts.kinds.${result.account.kind}`)} ·{' '}
          <strong>{t('accounts.fields.role')}:</strong> {t(`accounts.roles.${result.account.role}`)}
        </Body1>
        {result.account.email && <Body1><strong>{t('accounts.fields.email')}:</strong> {result.account.email}</Body1>}
        {scopeApplies && (
          <Body1>
            <strong>{t('accounts.scope.title')}:</strong>{' '}
            {assignedServiceIds.length === 0
              ? t('accounts.scope.unrestricted')
              : (serviceAccounts?.items ?? [])
                  .filter(a => assignedServiceIds.includes(a.id))
                  .map(a => a.label || a.name)
                  .join(', ')}
          </Body1>
        )}
      </div>
      {result.plaintext ? (
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>{t('accounts.onboarding.result.copyKey')}</MessageBarTitle>
            <div className={s.keyRow}>
              <div className={s.key}>{result.plaintext}</div>
              <Tooltip content={t(copied ? 'accounts.onboarding.result.copied' : 'accounts.onboarding.result.copyToClipboard')} relationship="label">
                <Button appearance="subtle" icon={<Copy20Regular />} onClick={copyKey} aria-label={t('accounts.onboarding.result.copyAria')} />
              </Tooltip>
            </div>
          </MessageBarBody>
        </MessageBar>
      ) : (
        <Body1 className={s.hint}>{t('accounts.onboarding.result.noKey')}</Body1>
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
      finishLabel={t('accounts.onboarding.createAccount')}
      busy={busy}
      error={error}
      result={resultView}
    />
  )
}
