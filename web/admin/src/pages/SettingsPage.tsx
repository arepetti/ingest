import { useState } from 'react'
import {
  Avatar, Badge, Body1, Button, Card, Checkbox, Dropdown, Drawer, DrawerBody, Field, Input, Option, Spinner,
  Switch,
  Table, TableBody, TableCell, TableCellLayout, TableHeader, TableHeaderCell, TableRow,
  Textarea, Title3,
  MessageBarBody, makeStyles, tokens,
} from '@fluentui/react-components'
import {
  Alert24Regular, DocumentText24Regular, Mail20Regular, Mail24Regular, PlugConnected24Regular,
} from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { DRAWER_EXPANDED_WIDTH, DrawerHeaderWithClose } from '../components/DrawerHeaderWithClose'
import { SectionedLayout } from '../components/SectionedLayout'
import type { LayoutSection } from '../components/SectionedLayout'
import { WebhooksSection } from '../components/WebhooksSection'
import { clickableRowProps } from '../utils/a11y'
import {
  useMe, useAccounts,
  useEmailSettings, useUpdateEmailSettings,
  useEmailTemplates, useUpdateEmailTemplate,
  useNotificationSettings, useUpdateNotificationSettings, useRunNotifications,
} from '../api/hooks'
import { formatApiError } from '../api/client'
import type {
  EmailSettings, EmailTemplate, NotificationSettings, NotificationRule,
} from '../api/types'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  card: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '20px', maxWidth: '760px' },
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
})

export function SettingsPage() {
  const { data: me, isLoading } = useMe()

  if (isLoading) return <Spinner label="Loading…" />
  if (me?.role !== 'Admin') {
    return (
      <AutoScrollMessageBar intent="error">
        <MessageBarBody>Settings are available to administrators only.</MessageBarBody>
      </AutoScrollMessageBar>
    )
  }

  const emailEnabled = me?.emailEnabled === true
  const webhooksEnabled = me?.webhooksEnabled === true

  const sections: LayoutSection[] = [
    ...(emailEnabled ? [
      { id: 'email', label: 'Email', icon: <Mail24Regular />, render: () => <EmailSettingsSection /> },
      { id: 'templates', label: 'Email templates', icon: <DocumentText24Regular />, render: () => <EmailTemplatesSection /> },
      { id: 'notifications', label: 'Notifications', icon: <Alert24Regular />, render: () => <NotificationsSection /> },
    ] as LayoutSection[] : []),
    ...(webhooksEnabled ? [
      { id: 'webhooks', label: 'Webhooks', icon: <PlugConnected24Regular />, render: () => <WebhooksSection /> },
    ] as LayoutSection[] : []),
  ]

  if (sections.length === 0) {
    return (
      <AutoScrollMessageBar intent="info">
        <MessageBarBody>
          No configurable settings are enabled. Turn on email or webhooks in the server
          configuration to manage them here.
        </MessageBarBody>
      </AutoScrollMessageBar>
    )
  }

  return <SectionedLayout title="Settings" sections={sections} />
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
                    <TableCellLayout
                      media={<Avatar name={t.name} icon={<Mail20Regular />} color="brand" size={32} />}
                    >
                      <strong className={s.truncate}>{t.name}</strong>
                    </TableCellLayout>
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

  const [upcoming, setUpcoming] = useState<NotificationRule>(initial.upcoming)
  const [missed, setMissed] = useState<NotificationRule>(initial.missed)
  const [warnings, setWarnings] = useState<NotificationRule>(initial.warnings)
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

function RuleEditor({ title, rule, onChange }: {
  title: string
  rule: NotificationRule
  onChange: (r: NotificationRule) => void
}) {
  const s = useStyles()
  return (
    <div className={s.ruleBlock}>
      <Switch label={title} checked={rule.enabled} onChange={(_, d) => onChange({ ...rule, enabled: d.checked })} />
      {rule.enabled && (
        <div className={s.ruleChildren}>
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
