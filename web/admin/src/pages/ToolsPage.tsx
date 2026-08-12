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
import { formatApiError, localizeDiagnostics } from '../api/client'
import { downloadFromUrl, pickTextFile } from '../utils/download'
import type { AccountsImportResult, BackupImportResult } from '../api/types'
import { useTranslation } from 'react-i18next'

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
  const { t } = useTranslation()
  const { has, isLoading } = useCapabilities()

  if (isLoading) return <Spinner label={t('tools.loading')} />
  if (!has('backup:read')) {
    return (
      <AutoScrollMessageBar intent="error">
        <MessageBarBody>{t('tools.noPermission')}</MessageBarBody>
      </AutoScrollMessageBar>
    )
  }

  const sections: LayoutSection[] = [
    { id: 'backup', label: t('tools.dataBackup.title'), group: t('tools.navigationGroup'), icon: <DatabaseArrowDownRegular fontSize={24} />, render: () => <BackupRestoreSection canRestore={has('backup:manage')} /> },
    { id: 'config-backup', label: t('tools.configBackup.title'), group: t('tools.navigationGroup'), icon: <SettingsRegular fontSize={24} />, render: () => <ConfigBackupRestoreSection canRestore={has('backup:manage')} /> },
    ...(has('accounts:read')
      ? [{ id: 'accounts-backup', label: t('tools.accountsBackup.title'), group: t('tools.navigationGroup'), icon: <PeopleRegular fontSize={24} />, render: () => <AccountsBackupSection canImport={has('accounts:manage')} /> }]
      : []),
  ]

  return <SectionedLayout title={t('tools.title')} sections={sections} />
}

function BackupRestoreSection({ canRestore }: { canRestore: boolean }) {
  const s = useStyles()
  const { t } = useTranslation()
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
      if (!/no file selected/i.test(msg)) setError(t('tools.dataBackup.readError', { error: msg }))
      return
    }

    const ok = window.confirm(t('tools.dataBackup.confirmRestore'))
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
        <Title3 className={s.sectionTitle}>{t('tools.dataBackup.title')}</Title3>
        <Body1 className={s.help}>
          {t('tools.dataBackup.description')}
        </Body1>
      </div>

      <div className={s.warn}>
        {t('tools.dataBackup.warningBeforeSmall')} <strong>{t('tools.dataBackup.small')}</strong>{' '}
        {t('tools.dataBackup.warningBeforeNot')} <strong>{t('tools.dataBackup.notPrimary')}</strong>{' '}
        {t('tools.dataBackup.warningBeforeCommand')}<code>mongodump</code>{' '}
        {t('tools.dataBackup.warningBeforeReplace')} <strong>{t('tools.dataBackup.replacesAll')}</strong>{' '}
        {t('tools.dataBackup.warningAfterReplace')}
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {result && (
        <AutoScrollMessageBar intent="success">
          <MessageBarBody>
            {t('tools.restoreComplete')}
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
          {t(busy ? 'tools.preparing' : 'tools.dataBackup.download')}
        </Button>
        {canRestore && (
          <Button
            icon={importer.isPending ? <Spinner size="tiny" /> : <ArrowUpload20Regular />}
            disabled={busy || importer.isPending}
            onClick={onImport}
          >
            {t(importer.isPending ? 'tools.restoring' : 'tools.restoreFromFile')}
          </Button>
        )}
      </div>
    </Card>
  )
}

function ConfigBackupRestoreSection({ canRestore }: { canRestore: boolean }) {
  const s = useStyles()
  const { t } = useTranslation()
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
      if (!/no file selected/i.test(msg)) setError(t('tools.configBackup.readError', { error: msg }))
      return
    }

    const ok = window.confirm(t('tools.configBackup.confirmRestore'))
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
        <Title3 className={s.sectionTitle}>{t('tools.configBackup.title')}</Title3>
        <Body1 className={s.help}>
          {t('tools.configBackup.description')}
        </Body1>
      </div>

      <div className={s.warn}>
        {t('tools.configBackup.warningBeforeReplace')} <strong>{t('tools.configBackup.replacesAll')}</strong>{' '}
        {t('tools.configBackup.warningBeforePepper')}{' '}<code>ApiKey:Pepper</code>
        {t('tools.configBackup.warningAfterPepper')}
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {result && (
        <AutoScrollMessageBar intent="success">
          <MessageBarBody>
            {t('tools.restoreComplete')}
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
          {t(busy ? 'tools.preparing' : 'tools.configBackup.download')}
        </Button>
        {canRestore && (
          <Button
            icon={importer.isPending ? <Spinner size="tiny" /> : <ArrowUpload20Regular />}
            disabled={busy || importer.isPending}
            onClick={onImport}
          >
            {t(importer.isPending ? 'tools.restoring' : 'tools.restoreFromFile')}
          </Button>
        )}
      </div>
    </Card>
  )
}

function AccountsBackupSection({ canImport }: { canImport: boolean }) {
  const s = useStyles()
  const { t } = useTranslation()
  const importer = useImportAccountsBackup()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<AccountsImportResult | null>(null)
  const localizedErrors = result ? localizeDiagnostics(result.errorDetails, result.errors) : []

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
      if (!/no file selected/i.test(msg)) setError(t('tools.accountsBackup.readError', { error: msg }))
      return
    }

    const ok = window.confirm(t('tools.accountsBackup.confirmImport'))
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
        <Title3 className={s.sectionTitle}>{t('tools.accountsBackup.title')}</Title3>
        <Body1 className={s.help}>
          {t('tools.accountsBackup.description')}
        </Body1>
      </div>

      <div className={s.warn}>
        <strong>{t('tools.accountsBackup.keysNeverExported')}</strong>{' '}
        {t('tools.accountsBackup.warningBeforeNoKey')} <strong>{t('tools.accountsBackup.noKey')}</strong>{' '}
        {t('tools.accountsBackup.warningAfterNoKey')}
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {result && (
        <AutoScrollMessageBar intent={localizedErrors.length > 0 ? 'warning' : 'success'}>
          <MessageBarBody>
            {t('tools.accountsBackup.importCompleteBeforeCreated')}{' '}
            <Text weight="semibold">{result.created}</Text>{' '}
            {t('tools.accountsBackup.created')},{' '}
            <Text weight="semibold">{result.updated}</Text>{' '}
            {t('tools.accountsBackup.updated')}.
            {localizedErrors.length > 0 && (
              <ul className={s.counts}>
                {localizedErrors.map((msg, i) => <li key={i}>{msg}</li>)}
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
          {t(busy ? 'tools.preparing' : 'tools.accountsBackup.download')}
        </Button>
        {canImport && (
          <Button
            icon={importer.isPending ? <Spinner size="tiny" /> : <ArrowUpload20Regular />}
            disabled={busy || importer.isPending}
            onClick={onImport}
          >
            {t(importer.isPending ? 'tools.importing' : 'tools.importFromFile')}
          </Button>
        )}
      </div>
    </Card>
  )
}
