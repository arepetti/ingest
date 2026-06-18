import { useState } from 'react'
import {
  Body1, Button, Card, MessageBarBody, Spinner, Text, Title3, makeStyles, tokens,
} from '@fluentui/react-components'
import {
  ArrowDownload20Regular, ArrowUpload20Regular, DatabaseArrowDownRegular, PeopleRegular, SettingsRegular,
} from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { SectionedLayout } from '../components/SectionedLayout'
import type { LayoutSection } from '../components/SectionedLayout'
import {
  accountsBackupExportUrl, backupExportUrl, configBackupExportUrl, useCapabilities,
  useImportAccountsBackup, useImportBackup, useImportConfigBackup,
} from '../api/hooks'
import { formatApiError } from '../api/client'
import { downloadFromUrl, pickTextFile } from '../utils/download'
import type { AccountsImportResult, BackupImportResult } from '../api/types'

const useStyles = makeStyles({
  card: { display: 'flex', flexDirection: 'column', gap: '12px', padding: '20px' },
  sectionTitle: { display: 'block', marginBottom: '2px' },
  help: { color: tokens.colorNeutralForeground3 },
  actions: { display: 'flex', gap: '12px', flexWrap: 'wrap', marginTop: '4px' },
  warn: {
    borderLeft: `3px solid ${tokens.colorPaletteDarkOrangeBorderActive}`,
    backgroundColor: tokens.colorNeutralBackground2,
    padding: '10px 14px',
    borderRadius: tokens.borderRadiusMedium,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  counts: { margin: '4px 0 0', paddingLeft: '18px' },
})

/**
 * Admin "Tools" hub: operational utilities that aren't really configuration. Today it hosts
 * Backup &amp; restore (moved off the Settings page); future maintenance tools slot in as extra
 * sections. Uses the same master-detail layout as Settings.
 */
export function ToolsPage() {
  const { has, isLoading } = useCapabilities()

  if (isLoading) return <Spinner label="Loading…" />
  if (!has('backup:read')) {
    return (
      <AutoScrollMessageBar intent="error">
        <MessageBarBody>You don't have permission to use these tools.</MessageBarBody>
      </AutoScrollMessageBar>
    )
  }

  const sections: LayoutSection[] = [
    { id: 'backup', label: 'Data backup', group: 'Backup & restore', icon: <DatabaseArrowDownRegular fontSize={24} />, render: () => <BackupRestoreSection canRestore={has('backup:manage')} /> },
    { id: 'config-backup', label: 'Configuration backup', group: 'Backup & restore', icon: <SettingsRegular fontSize={24} />, render: () => <ConfigBackupRestoreSection canRestore={has('backup:manage')} /> },
    ...(has('accounts:read')
      ? [{ id: 'accounts-backup', label: 'Accounts', group: 'Backup & restore', icon: <PeopleRegular fontSize={24} />, render: () => <AccountsBackupSection canImport={has('accounts:manage')} /> }]
      : []),
  ]

  return <SectionedLayout title="Tools" sections={sections} />
}

function BackupRestoreSection({ canRestore }: { canRestore: boolean }) {
  const s = useStyles()
  const importer = useImportBackup()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<BackupImportResult | null>(null)

  async function onExport() {
    setError(null)
    setBusy(true)
    try {
      const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')
      await downloadFromUrl(backupExportUrl(), `ingest-backup-${stamp}.json`)
    } catch (e) {
      setError(formatApiError(e))
    } finally {
      setBusy(false)
    }
  }

  async function onImport() {
    setError(null)
    setResult(null)
    let parsed: unknown
    try {
      const { content } = await pickTextFile('.json,application/json')
      parsed = JSON.parse(content)
    } catch (e) {
      // A cancelled picker rejects too; only surface real read/parse failures.
      const msg = e instanceof Error ? e.message : String(e)
      if (!/no file selected/i.test(msg)) setError(`Could not read the backup file: ${msg}`)
      return
    }

    const ok = window.confirm(
      'Restore from this backup?\n\n' +
      'This REPLACES all current data (accounts, keys, schemas, submissions, reports, audit log) ' +
      'with the contents of the file. It cannot be undone. Make sure you have a current backup first.',
    )
    if (!ok) return

    try {
      const res = await importer.mutateAsync(parsed)
      setResult(res)
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  return (
    <Card className={s.card}>
      <div>
        <Title3 className={s.sectionTitle}>Data backup</Title3>
        <Body1 className={s.help}>
          Export the entire registry (accounts, keys, schemas, submissions, reports, audit log) to a
          single JSON file, or restore it from one.
        </Body1>
      </div>

      <div className={s.warn}>
        This is a convenience tool for <strong>small</strong> deployments and moving data between
        environments — <strong>not</strong> the primary backup mechanism. For real backups, take a
        database-level snapshot (<code>mongodump</code> or your hosting provider&apos;s backup). A
        restore <strong>replaces all current data</strong> and is not transactional.
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {result && (
        <AutoScrollMessageBar intent="success">
          <MessageBarBody>
            Restore complete.
            <ul className={s.counts}>
              {Object.entries(result.restored).map(([name, n]) => (
                <li key={name}><Text weight="semibold">{name}</Text>: {n}</li>
              ))}
            </ul>
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <div className={s.actions}>
        <Button
          appearance="primary"
          icon={busy ? <Spinner size="tiny" /> : <ArrowDownload20Regular />}
          disabled={busy || importer.isPending}
          onClick={onExport}
        >
          {busy ? 'Preparing…' : 'Download backup'}
        </Button>
        {canRestore && (
          <Button
            icon={importer.isPending ? <Spinner size="tiny" /> : <ArrowUpload20Regular />}
            disabled={busy || importer.isPending}
            onClick={onImport}
          >
            {importer.isPending ? 'Restoring…' : 'Restore from file…'}
          </Button>
        )}
      </div>
    </Card>
  )
}

function ConfigBackupRestoreSection({ canRestore }: { canRestore: boolean }) {
  const s = useStyles()
  const importer = useImportConfigBackup()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<BackupImportResult | null>(null)

  async function onExport() {
    setError(null)
    setBusy(true)
    try {
      const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')
      await downloadFromUrl(configBackupExportUrl(), `ingest-config-${stamp}.json`)
    } catch (e) {
      setError(formatApiError(e))
    } finally {
      setBusy(false)
    }
  }

  async function onImport() {
    setError(null)
    setResult(null)
    let parsed: unknown
    try {
      const { content } = await pickTextFile('.json,application/json')
      parsed = JSON.parse(content)
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e)
      if (!/no file selected/i.test(msg)) setError(`Could not read the configuration file: ${msg}`)
      return
    }

    const ok = window.confirm(
      'Restore from this configuration backup?\n\n' +
      'This REPLACES all current configuration (approval policy & rules, email & notification ' +
      'settings and templates, webhooks, integrations and the Teams connection) with the contents ' +
      'of the file. It cannot be undone.\n\n' +
      'Encrypted secrets (SMTP password, webhook secrets, Teams bot secret) only work if this ' +
      'server uses the same ApiKey:Pepper as the one that produced the file; otherwise re-enter them ' +
      'afterwards. A stored secret is kept when the file omits it.',
    )
    if (!ok) return

    try {
      const res = await importer.mutateAsync(parsed)
      setResult(res)
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  return (
    <Card className={s.card}>
      <div>
        <Title3 className={s.sectionTitle}>Configuration backup</Title3>
        <Body1 className={s.help}>
          Export all configuration (approval policy &amp; rules, email &amp; notification settings and
          templates, webhooks, integrations and the Teams connection) to a single JSON file, or
          restore it from one — to copy configuration between environments or recover after a disaster.
        </Body1>
      </div>

      <div className={s.warn}>
        Restoring <strong>replaces all current configuration</strong> and is not transactional.
        Encrypted secrets are included as ciphertext and only decrypt on a server using the same{' '}
        <code>ApiKey:Pepper</code>; on a different deployment, re-enter them after the restore. A
        stored secret is preserved when the file omits it.
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {result && (
        <AutoScrollMessageBar intent="success">
          <MessageBarBody>
            Restore complete.
            <ul className={s.counts}>
              {Object.entries(result.restored).map(([name, n]) => (
                <li key={name}><Text weight="semibold">{name}</Text>: {n}</li>
              ))}
            </ul>
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <div className={s.actions}>
        <Button
          appearance="primary"
          icon={busy ? <Spinner size="tiny" /> : <ArrowDownload20Regular />}
          disabled={busy || importer.isPending}
          onClick={onExport}
        >
          {busy ? 'Preparing…' : 'Download configuration'}
        </Button>
        {canRestore && (
          <Button
            icon={importer.isPending ? <Spinner size="tiny" /> : <ArrowUpload20Regular />}
            disabled={busy || importer.isPending}
            onClick={onImport}
          >
            {importer.isPending ? 'Restoring…' : 'Restore from file…'}
          </Button>
        )}
      </div>
    </Card>
  )
}

function AccountsBackupSection({ canImport }: { canImport: boolean }) {
  const s = useStyles()
  const importer = useImportAccountsBackup()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<AccountsImportResult | null>(null)

  async function onExport() {
    setError(null)
    setBusy(true)
    try {
      const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')
      await downloadFromUrl(accountsBackupExportUrl(), `ingest-accounts-${stamp}.json`)
    } catch (e) {
      setError(formatApiError(e))
    } finally {
      setBusy(false)
    }
  }

  async function onImport() {
    setError(null)
    setResult(null)
    let parsed: unknown
    try {
      const { content } = await pickTextFile('.json,application/json')
      parsed = JSON.parse(content)
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e)
      if (!/no file selected/i.test(msg)) setError(`Could not read the accounts file: ${msg}`)
      return
    }

    const ok = window.confirm(
      'Import accounts from this file?\n\n' +
      'Accounts are matched by name: existing ones are updated and new names are created. ' +
      'Accounts not in the file are left untouched.\n\n' +
      'API keys are NOT included in the file, so any account created by this import starts with no ' +
      'key — generate one for each afterwards.',
    )
    if (!ok) return

    try {
      const res = await importer.mutateAsync(parsed)
      setResult(res)
    } catch (e) {
      setError(formatApiError(e))
    }
  }

  return (
    <Card className={s.card}>
      <div>
        <Title3 className={s.sectionTitle}>Accounts</Title3>
        <Body1 className={s.help}>
          Export every account (name, label, role, permissions, SSO links, enabled state) to a single
          JSON file, or import one to create and update accounts — handy for cloning or seeding an
          environment.
        </Body1>
      </div>

      <div className={s.warn}>
        <strong>API keys are never exported.</strong> They aren&apos;t stored in a recoverable form,
        so an imported account starts with <strong>no key</strong> and must have one re-generated
        before it can authenticate. Import is non-destructive — accounts missing from the file are
        left as they are.
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {result && (
        <AutoScrollMessageBar intent={result.errors.length > 0 ? 'warning' : 'success'}>
          <MessageBarBody>
            Import complete: <Text weight="semibold">{result.created}</Text> created,{' '}
            <Text weight="semibold">{result.updated}</Text> updated.
            {result.errors.length > 0 && (
              <ul className={s.counts}>
                {result.errors.map((msg, i) => <li key={i}>{msg}</li>)}
              </ul>
            )}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <div className={s.actions}>
        <Button
          appearance="primary"
          icon={busy ? <Spinner size="tiny" /> : <ArrowDownload20Regular />}
          disabled={busy || importer.isPending}
          onClick={onExport}
        >
          {busy ? 'Preparing…' : 'Download accounts'}
        </Button>
        {canImport && (
          <Button
            icon={importer.isPending ? <Spinner size="tiny" /> : <ArrowUpload20Regular />}
            disabled={busy || importer.isPending}
            onClick={onImport}
          >
            {importer.isPending ? 'Importing…' : 'Import from file…'}
          </Button>
        )}
      </div>
    </Card>
  )
}
