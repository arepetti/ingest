import { useState } from 'react'
import {
  Badge, Body1, Button, Card, Checkbox, Dropdown, Drawer, DrawerBody, Field, Input, Option, Radio, RadioGroup, Spinner,
  Switch,
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow,
  Textarea, Title3,
  MessageBarBody, makeStyles, tokens,
} from '@fluentui/react-components'
import {
  Alert24Regular, CheckmarkCircle24Regular, ClipboardTaskListLtr24Regular, DocumentText24Regular,
  Key24Regular, Mail24Regular, PeopleTeam24Regular, PlugConnected24Regular, Settings24Regular,
} from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { DRAWER_EXPANDED_WIDTH, DrawerHeaderWithClose } from '../components/DrawerHeaderWithClose'
import { SectionedLayout } from '../components/SectionedLayout'
import type { LayoutSection } from '../components/SectionedLayout'
import { WebhooksSection } from '../components/WebhooksSection'
import { ApprovalRulesSection } from '../components/ApprovalRulesSection'
import { IntegrationsSection, TeamsConnectionSection } from '../components/IntegrationsSection'
import { clickableRowProps } from '../utils/a11y'
import {
  useCapabilities, useAccounts,
  useEmailSettings, useUpdateEmailSettings,
  useEmailTemplates, useUpdateEmailTemplate,
  useNotificationSettings, useUpdateNotificationSettings, useRunNotifications,
  useApprovalSettings, useUpdateApprovalSettings,
} from '../api/hooks'
import { accountHasCapability } from '../api/capabilities'
import { formatApiError } from '../api/client'
import { approverFromKey, approverKey, approverLabel, SERVICE_OWNER_KEY, SERVICE_OWNER_LABEL } from '../utils/approvers'
import type {
  Account, ApprovalMode, ApprovalPolicy, ApprovalSourceScope, ApproverRequirement,
  EmailSettings, EmailTemplate, NotificationSettings, NotificationRule,
} from '../api/types'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  card: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '20px' },
  cardWide: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '20px' },
  sectionTitle: { display: 'block', marginBottom: '2px' },
  help: { color: tokens.colorNeutralForeground3 },
  actions: { display: 'flex', gap: '12px', flexWrap: 'wrap', marginTop: '4px' },
  row: { display: 'flex', gap: '12px', flexWrap: 'wrap' },
  grow: { flex: 1, minWidth: '220px' },
  ruleBlock: {
    display: 'flex', flexDirection: 'column', gap: '6px',
    padding: '12px 14px', borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  ruleChildren: { display: 'flex', gap: '20px', flexWrap: 'wrap', paddingLeft: '6px' },
  mono: { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200 },
  table: { tableLayout: 'fixed', width: '100%' },
  tableRow: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  rowClickable: {
    cursor: 'pointer',
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '-2px' },
  },
  nameCell: { maxWidth: 0 },
  truncate: { display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  colSubject: { maxWidth: 0 },
  colFormat: { width: '120px' },
  drawer: { width: 'max(600px, 50vw)' },
  drawerForm: { display: 'flex', flexDirection: 'column', gap: '12px' },
  approverList: { display: 'flex', flexDirection: 'column', gap: '4px' },
  approverRow: {
    display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px',
    padding: '6px 10px', borderRadius: '6px', backgroundColor: tokens.colorNeutralBackground2,
  },
  approverName: { fontWeight: tokens.fontWeightSemibold },
})

// --- Approval (global default policy) ------------------------------------------------------

const approvalSourceLabels: Record<ApprovalSourceScope, string> = {
  Both: 'Both manual and API submissions',
  ManualOnly: 'Manual (web console) submissions only',
  ApiOnly: 'API submissions only',
}

function ApprovalSettingsSection() {
  const { data, isLoading } = useApprovalSettings()
  const { data: accountsPage } = useAccounts()
  if (isLoading || !data) return <Spinner label="Loading…" />
  const approvers = (accountsPage?.items ?? []).filter(a => accountHasCapability(a, 'submissions:approve') && !a.isDeleted)
  return <ApprovalSettingsForm initial={data} accounts={approvers} />
}

function ApprovalSettingsForm({ initial, accounts }: { initial: ApprovalPolicy; accounts: Account[] }) {
  const s = useStyles()
  const update = useUpdateApprovalSettings()
  // The global default may only be "no approval" or "approval required" — `UseGlobalDefault` is a
  // per-schema concept (a schema deferring to *this* policy) and is normalised to None here.
  const [mode, setMode] = useState<ApprovalMode>(initial.mode === 'Required' ? 'Required' : 'None')
  const [appliesToSources, setAppliesToSources] = useState<ApprovalSourceScope>(initial.appliesToSources ?? 'Both')
  const [approvers, setApprovers] = useState<ApprovalPolicy['approvers']>(initial.approvers ?? [])
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  const accountsById = new Map(accounts.map(a => [a.id, a]))
  const hasRequiredApprover = approvers.some(a => a.requirement === 'Required')

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

  async function onSave() {
    setError(null); setSaved(false)
    try {
      await update.mutateAsync({ mode, appliesToSources, approvers: mode === 'Required' ? approvers : [] })
      setSaved(true)
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  return (
    <Card className={s.card}>
      <div>
        <Title3 className={s.sectionTitle}>Default approval policy</Title3>
        <Body1 className={s.help}>
          Schemas set to “Use the global default” fall back to this policy. Changing it affects only
          new submissions; in-flight ones keep the approvers they were created with.
        </Body1>
      </div>

      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}
      {saved && <AutoScrollMessageBar intent="success"><MessageBarBody>Approval policy saved.</MessageBarBody></AutoScrollMessageBar>}

      <Field label="When a schema defers to the default">
        <RadioGroup value={mode} onChange={(_, d) => setMode(d.value as ApprovalMode)}>
          <Radio value="None" label="Don’t require approval" />
          <Radio value="Required" label="Require approval" />
        </RadioGroup>
      </Field>

      {mode === 'Required' && (
        <>
          <Field label="Applies to">
            <Dropdown
              value={approvalSourceLabels[appliesToSources]}
              selectedOptions={[appliesToSources]}
              onOptionSelect={(_, d) => setAppliesToSources(d.optionValue as ApprovalSourceScope)}
            >
              {(Object.keys(approvalSourceLabels) as ApprovalSourceScope[]).map(sc => (
                <Option key={sc} value={sc}>{approvalSourceLabels[sc]}</Option>
              ))}
            </Dropdown>
          </Field>

          <Field
            label="Approvers"
            hint="Approver/Admin accounts who may review, and/or the service owner (the account that sent the submission). Mark at least one as Required."
            validationState={hasRequiredApprover ? 'none' : 'warning'}
            validationMessage={hasRequiredApprover ? undefined : 'Add at least one Required approver.'}
          >
            <Dropdown
              multiselect
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

          {approvers.length > 0 && (
            <div className={s.approverList}>
              {approvers.map(a => {
                const key = approverKey(a)
                return (
                  <div key={key} className={s.approverRow}>
                    <span className={s.approverName}>{approverLabel(a, accountsById)}</span>
                    <RadioGroup layout="horizontal" value={a.requirement} onChange={(_, d) => setRequirement(key, d.value as ApproverRequirement)}>
                      <Radio value="Required" label="Required" />
                      <Radio value="Optional" label="Optional" />
                    </RadioGroup>
                  </div>
                )
              })}
            </div>
          )}
        </>
      )}

      <div className={s.actions}>
        <Button appearance="primary" disabled={update.isPending} onClick={onSave}>
          {update.isPending ? 'Saving…' : 'Save'}
        </Button>
      </div>
    </Card>
  )
}

export function SettingsPage() {
  const { me, hasAny, isLoading } = useCapabilities()

  if (isLoading) return <Spinner label="Loading…" />

  const canConfigureSettings = hasAny('settings:read', 'settings:manage')
  const canConfigureNotifications = hasAny('notifications:read', 'notifications:manage')
  const canConfigureWebhooks = hasAny('webhooks:read', 'webhooks:manage')
  const canConfigureIntegrations = hasAny('integrations:read', 'integrations:manage')

  if (!canConfigureSettings && !canConfigureNotifications && !canConfigureWebhooks && !canConfigureIntegrations) {
    return (
      <AutoScrollMessageBar intent="error">
        <MessageBarBody>You don't have permission to manage any settings.</MessageBarBody>
      </AutoScrollMessageBar>
    )
  }

  const emailEnabled = me?.emailEnabled === true
  const webhooksEnabled = me?.webhooksEnabled === true
  const approvalEnabled = me?.approvalEnabled === true
  const integrationsEnabled = me?.integrationsEnabled === true

  const sections: LayoutSection[] = [
    { id: 'general', label: 'General', group: 'General', icon: <Settings24Regular />, render: () => <GeneralSettingsSection /> },
    ...(approvalEnabled && canConfigureSettings ? [
      { id: 'approval', label: 'Approval', group: 'Approvals', icon: <CheckmarkCircle24Regular />, render: () => <ApprovalSettingsSection /> },
      { id: 'rules', label: 'Rules', group: 'Approvals', icon: <ClipboardTaskListLtr24Regular />, render: () => <ApprovalRulesSection /> },
    ] as LayoutSection[] : []),
    ...(emailEnabled && canConfigureNotifications ? [
      { id: 'email', label: 'Email', group: 'Notifications', icon: <Mail24Regular />, render: () => <EmailSettingsSection /> },
      { id: 'templates', label: 'Email templates', group: 'Notifications', icon: <DocumentText24Regular />, render: () => <EmailTemplatesSection /> },
      { id: 'notifications', label: 'Notifications', group: 'Notifications', icon: <Alert24Regular />, render: () => <NotificationsSection /> },
    ] as LayoutSection[] : []),
    ...(webhooksEnabled && canConfigureWebhooks ? [
      { id: 'webhooks', label: 'Webhooks', group: 'Integrations', icon: <PlugConnected24Regular />, render: () => <WebhooksSection /> },
    ] as LayoutSection[] : []),
    ...(integrationsEnabled && canConfigureIntegrations ? [
      { id: 'integrations', label: 'Teams notifications', group: 'Integrations', icon: <PeopleTeam24Regular />, render: () => <IntegrationsSection /> },
      { id: 'teams-connection', label: 'Teams connection', group: 'Integrations', icon: <Key24Regular />, render: () => <TeamsConnectionSection /> },
    ] as LayoutSection[] : []),
  ]

  if (sections.length === 0) {
    return (
      <AutoScrollMessageBar intent="info">
        <MessageBarBody>
          No configurable settings are enabled. Turn on email, webhooks, integrations, or the
          approval workflow in the server configuration to manage them here.
        </MessageBarBody>
      </AutoScrollMessageBar>
    )
  }

  return <SectionedLayout title="Settings" sections={sections} />
}

// --- General (console preferences) --------------------------------------------------------

/**
 * Console-wide preferences. These are intentionally UI-only placeholders for now — the app ships
 * a single language and a single theme, so the controls exist to establish the section and aren't
 * wired to anything. Extra options (and persistence) will follow as more are actually supported.
 */
function GeneralSettingsSection() {
  const s = useStyles()
  const [language, setLanguage] = useState('en-US')
  const [theme, setTheme] = useState('light')

  return (
    <Card className={s.card}>
      <div>
        <Title3 className={s.sectionTitle}>General</Title3>
        <Body1 className={s.help}>
          Preferences for the admin console.
        </Body1>
      </div>

      <Field label="Language">
        <Dropdown
          value="English (US)"
          selectedOptions={[language]}
          onOptionSelect={(_, d) => setLanguage(d.optionValue as string)}
        >
          <Option value="en-US">English (US)</Option>
        </Dropdown>
      </Field>

      <Field label="Theme">
        <Dropdown
          value="Light"
          selectedOptions={[theme]}
          onOptionSelect={(_, d) => setTheme(d.optionValue as string)}
        >
          <Option value="light">Light</Option>
        </Dropdown>
      </Field>
    </Card>
  )
}

// --- Email (SMTP) settings ----------------------------------------------------------------

function EmailSettingsSection() {
  const { data, isLoading } = useEmailSettings()
  if (isLoading || !data) return <Spinner label="Loading…" />
  return <EmailSettingsForm initial={data} key={data.host + '|' + data.fromAddress} />
}

function EmailSettingsForm({ initial }: { initial: EmailSettings }) {
  const s = useStyles()
  const update = useUpdateEmailSettings()
  const [host, setHost] = useState(initial.host)
  const [port, setPort] = useState(String(initial.port || 587))
  const [useStartTls, setUseStartTls] = useState(initial.useStartTls)
  const [username, setUsername] = useState(initial.username ?? '')
  const [fromAddress, setFromAddress] = useState(initial.fromAddress)
  const [fromName, setFromName] = useState(initial.fromName ?? '')
  const [changePassword, setChangePassword] = useState(false)
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  async function onSave() {
    setError(null); setSaved(false)
    try {
      await update.mutateAsync({
        host: host.trim(),
        port: Number(port) || 0,
        useStartTls,
        username: username.trim() || null,
        fromAddress: fromAddress.trim(),
        fromName: fromName.trim() || null,
        updatePassword: changePassword,
        password: changePassword ? password : null,
      })
      setSaved(true)
      setChangePassword(false)
      setPassword('')
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  return (
    <Card className={s.card}>
      <div>
        <Title3 className={s.sectionTitle}>Email server (SMTP)</Title3>
        <Body1 className={s.help}>
          Where outgoing mail is sent from. Stored in the database; the password is encrypted at
          rest and never shown again.{' '}
          {initial.configured
            ? <Badge appearance="tint" color="success">Configured</Badge>
            : <Badge appearance="tint" color="warning">Not configured</Badge>}
        </Body1>
      </div>

      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}
      {saved && <AutoScrollMessageBar intent="success"><MessageBarBody>Settings saved.</MessageBarBody></AutoScrollMessageBar>}

      <div className={s.row}>
        <Field label="Host" required className={s.grow}>
          <Input value={host} onChange={(_, d) => setHost(d.value)} placeholder="smtp.example.org" />
        </Field>
        <Field label="Port" required>
          <Input type="number" value={port} onChange={(_, d) => setPort(d.value)} style={{ width: '110px' }} />
        </Field>
      </div>

      <Switch label="Use TLS (STARTTLS)" checked={useStartTls} onChange={(_, d) => setUseStartTls(d.checked)} />

      <div className={s.row}>
        <Field label="From address" required className={s.grow}>
          <Input value={fromAddress} onChange={(_, d) => setFromAddress(d.value)} placeholder="ingest@example.org" />
        </Field>
        <Field label="From name" className={s.grow}>
          <Input value={fromName} onChange={(_, d) => setFromName(d.value)} placeholder="Ingest" />
        </Field>
      </div>

      <Field label="Username" className={s.grow}>
        <Input value={username} onChange={(_, d) => setUsername(d.value)} placeholder="(blank = anonymous relay)" />
      </Field>

      <Checkbox
        label={initial.hasPassword ? 'Change the stored password' : 'Set a password'}
        checked={changePassword}
        onChange={(_, d) => setChangePassword(!!d.checked)}
      />
      {changePassword && (
        <Field label="Password" hint="Leave blank to clear the stored password.">
          <Input type="password" value={password} onChange={(_, d) => setPassword(d.value)} />
        </Field>
      )}

      <div className={s.actions}>
        <Button appearance="primary" disabled={update.isPending} onClick={onSave}>
          {update.isPending ? 'Saving…' : 'Save'}
        </Button>
      </div>
    </Card>
  )
}

// --- Email templates ----------------------------------------------------------------------

function EmailTemplatesSection() {
  const s = useStyles()
  const { data, isLoading } = useEmailTemplates()
  const [editingKey, setEditingKey] = useState<string | null>(null)
  const [expanded, setExpanded] = useState(false)

  if (isLoading || !data) return <Spinner label="Loading…" />

  const editing = data.find(t => t.key === editingKey) ?? null
  function close() { setEditingKey(null); setExpanded(false) }

  return (
    <>
      <Card className={s.cardWide}>
        <div>
          <Title3 className={s.sectionTitle}>Email templates</Title3>
          <Body1 className={s.help}>
            Liquid templates used to build notification emails. Select one to edit its subject and body.
          </Body1>
        </div>

        {data.length === 0 ? (
          <AutoScrollMessageBar intent="info"><MessageBarBody>No templates yet.</MessageBarBody></AutoScrollMessageBar>
        ) : (
          <Table size="small" className={s.table}>
            <TableHeader>
              <TableRow>
                <TableHeaderCell>Template</TableHeaderCell>
                <TableHeaderCell className={s.colSubject}>Subject</TableHeaderCell>
                <TableHeaderCell className={s.colFormat}>Format</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map(t => (
                <TableRow
                  key={t.key}
                  className={`${s.tableRow} ${s.rowClickable}`}
                  {...clickableRowProps(() => { setEditingKey(t.key); setExpanded(false) }, `Edit template ${t.name}`)}
                >
                  <TableCell className={s.nameCell}>
                    <strong className={s.truncate}>{t.name}</strong>
                  </TableCell>
                  <TableCell className={s.colSubject}>
                    <span className={s.truncate}>{t.subject}</span>
                  </TableCell>
                  <TableCell className={s.colFormat}>
                    <Badge appearance="outline" color={t.htmlBody ? 'brand' : 'informative'}>
                      {t.htmlBody ? 'HTML + text' : 'Text'}
                    </Badge>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Card>

      <Drawer
        type="overlay"
        separator
        open={!!editing}
        onOpenChange={(_, d) => { if (!d.open) close() }}
        position="end"
        className={s.drawer}
        style={expanded ? { width: DRAWER_EXPANDED_WIDTH } : undefined}
      >
        <DrawerHeaderWithClose
          title={editing ? `Edit template — ${editing.name}` : 'Edit template'}
          onClose={close}
          expanded={expanded}
          onToggleExpand={() => setExpanded(e => !e)}
        />
        <DrawerBody>
          {editing && (
            <div className={s.drawerForm}>
              <TemplateEditor template={editing} key={editing.key} />
            </div>
          )}
        </DrawerBody>
      </Drawer>
    </>
  )
}

function TemplateEditor({ template }: { template: EmailTemplate }) {
  const s = useStyles()
  const update = useUpdateEmailTemplate()
  const [subject, setSubject] = useState(template.subject)
  const [textBody, setTextBody] = useState(template.textBody)
  const [htmlBody, setHtmlBody] = useState(template.htmlBody ?? '')
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  async function onSave() {
    setError(null); setSaved(false)
    try {
      await update.mutateAsync({
        key: template.key,
        req: {
          name: template.name,
          description: template.description ?? null,
          subject,
          textBody,
          htmlBody: htmlBody.trim() || null,
        },
      })
      setSaved(true)
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  return (
    <>
      {template.description && <Body1 className={s.help}>{template.description}</Body1>}
      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}
      {saved && <AutoScrollMessageBar intent="success"><MessageBarBody>Template saved.</MessageBarBody></AutoScrollMessageBar>}

      <Field label="Subject" required>
        <Input value={subject} onChange={(_, d) => setSubject(d.value)} />
      </Field>
      <Field label="Text body" required hint="Plain-text fallback. Liquid is supported.">
        <Textarea value={textBody} onChange={(_, d) => setTextBody(d.value)} rows={8} resize="vertical" />
      </Field>
      <Field label="HTML body" hint="Optional. Leave blank to send text only.">
        <Textarea value={htmlBody} onChange={(_, d) => setHtmlBody(d.value)} rows={8} resize="vertical" />
      </Field>
      <div className={s.actions}>
        <Button appearance="primary" disabled={update.isPending} onClick={onSave}>
          {update.isPending ? 'Saving…' : 'Save template'}
        </Button>
      </div>
    </>
  )
}

// --- Notifications ------------------------------------------------------------------------

function NotificationsSection() {
  const { data, isLoading } = useNotificationSettings()
  if (isLoading || !data) return <Spinner label="Loading…" />
  return <NotificationsForm initial={data} />
}

function NotificationsForm({ initial }: { initial: NotificationSettings }) {
  const s = useStyles()
  const update = useUpdateNotificationSettings()
  const run = useRunNotifications()
  const { data: accountsPage } = useAccounts()
  const { me } = useCapabilities()
  const approvalEnabled = !!me?.approvalEnabled

  const [upcoming, setUpcoming] = useState<NotificationRule>(initial.upcoming)
  const [missed, setMissed] = useState<NotificationRule>(initial.missed)
  const [warnings, setWarnings] = useState<NotificationRule>(initial.warnings)
  const [pendingApproval, setPendingApproval] = useState<NotificationRule>(initial.pendingApproval)
  const [approved, setApproved] = useState<NotificationRule>(initial.approved)
  const [rejected, setRejected] = useState<NotificationRule>(initial.rejected)
  const [draftSaved, setDraftSaved] = useState<NotificationRule>(initial.draftSaved)
  const [leadHours, setLeadHours] = useState(String(initial.upcomingLeadHours))
  const [recipients, setRecipients] = useState<string[]>(initial.adminRecipientAccountIds ?? [])
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [runResult, setRunResult] = useState<string | null>(null)

  // Eligible admin/operator recipients are accounts with a role above Service that have an email.
  const eligible = (accountsPage?.items ?? [])
    .filter(a => (a.role === 'Admin' || a.role === 'Operator') && !!a.email && !a.isDeleted)

  async function onSave() {
    setError(null); setSaved(false)
    try {
      await update.mutateAsync({
        upcoming, missed, warnings,
        pendingApproval, approved, rejected,
        draftSaved,
        upcomingLeadHours: Number(leadHours) || 24,
        adminRecipientAccountIds: recipients,
      })
      setSaved(true)
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  async function onRun() {
    setError(null); setRunResult(null)
    try {
      const r = await run.mutateAsync()
      setRunResult(`Queued ${r.totalQueued} email(s): ${r.upcomingQueued} upcoming, ${r.missedQueued} missed, ${r.warningsQueued} warnings.`)
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  return (
    <Card className={s.card}>
      <div>
        <Title3 className={s.sectionTitle}>Notifications</Title3>
        <Body1 className={s.help}>
          Choose which events generate emails and who receives them. Each trigger can notify the
          service&apos;s own contact email, the recipient list below, or both.
        </Body1>
      </div>

      {error && <AutoScrollMessageBar intent="error"><MessageBarBody>{error}</MessageBarBody></AutoScrollMessageBar>}
      {saved && <AutoScrollMessageBar intent="success"><MessageBarBody>Notification settings saved.</MessageBarBody></AutoScrollMessageBar>}
      {runResult && <AutoScrollMessageBar intent="success"><MessageBarBody>{runResult}</MessageBarBody></AutoScrollMessageBar>}

      <RuleEditor title="Upcoming submission reminder" rule={upcoming} onChange={setUpcoming} />
      {upcoming.enabled && (
        <Field label="Lead time (hours before the window closes)">
          <Input type="number" value={leadHours} onChange={(_, d) => setLeadHours(d.value)} style={{ width: '120px' }} />
        </Field>
      )}
      <RuleEditor title="Missed submission alert" rule={missed} onChange={setMissed} />
      <RuleEditor title="Submission with warnings notice" rule={warnings} onChange={setWarnings} />
      <RuleEditor
        title="Draft saved nudge"
        rule={draftSaved}
        onChange={setDraftSaved}
        hint="Sent on every draft save (no dedupe) to nudge collaborators. The email shows a relative path you paste after your console's address — it isn't a clickable link."
      />

      {approvalEnabled && (
        <>
          <RuleEditor
            title="Submission pending approval notice"
            rule={pendingApproval}
            onChange={setPendingApproval}
            hint="The submission's designated approvers are always emailed; these switches add the submitter and/or admin list."
          />
          <RuleEditor title="Submission approved notice" rule={approved} onChange={setApproved} />
          <RuleEditor title="Submission rejected notice" rule={rejected} onChange={setRejected} />
        </>
      )}

      <Field label="Admin / operator recipient list" hint="Accounts (with an email) that receive the copy when a trigger has 'admin list' on.">
        <Dropdown
          multiselect
          placeholder={eligible.length === 0 ? 'No eligible accounts (operators/admins need an email)' : 'Select recipients…'}
          selectedOptions={recipients}
          value={recipients
            .map(id => eligible.find(a => a.id === id))
            .filter(Boolean)
            .map(a => a!.label || a!.name)
            .join(', ')}
          onOptionSelect={(_, d) => setRecipients(d.selectedOptions)}
        >
          {eligible.map(a => (
            <Option key={a.id} value={a.id} text={a.label || a.name}>
              {(a.label || a.name)} — {a.email}
            </Option>
          ))}
        </Dropdown>
      </Field>

      <div className={s.actions}>
        <Button appearance="primary" disabled={update.isPending} onClick={onSave}>
          {update.isPending ? 'Saving…' : 'Save'}
        </Button>
        <Button disabled={run.isPending} onClick={onRun}>
          {run.isPending ? 'Running…' : 'Run now'}
        </Button>
      </div>
    </Card>
  )
}

function RuleEditor({ title, rule, onChange, hint }: {
  title: string
  rule: NotificationRule
  onChange: (r: NotificationRule) => void
  hint?: string
}) {
  const s = useStyles()
  return (
    <div className={s.ruleBlock}>
      <Switch label={title} checked={rule.enabled} onChange={(_, d) => onChange({ ...rule, enabled: d.checked })} />
      {rule.enabled && (
        <div className={s.ruleChildren}>
          {hint && <Body1 className={s.help}>{hint}</Body1>}
          <Checkbox
            label="Notify the service account"
            checked={rule.notifyServiceAccount}
            onChange={(_, d) => onChange({ ...rule, notifyServiceAccount: !!d.checked })}
          />
          <Checkbox
            label="Notify the admin/operator list"
            checked={rule.notifyAdminList}
            onChange={(_, d) => onChange({ ...rule, notifyAdminList: !!d.checked })}
          />
        </div>
      )}
    </div>
  )
}
