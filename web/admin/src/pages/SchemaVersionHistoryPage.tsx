import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import {
  Badge, Button, Title2,
  Menu, MenuButton, MenuDivider, MenuItem, MenuList, MenuPopover, MenuTrigger,
  Table, TableBody, TableCell, TableHeader, TableHeaderCell, TableRow,
  MessageBarBody, MessageBarTitle,
  makeStyles, tokens,
} from '@fluentui/react-components'
import {
  ArrowClockwise20Regular, ArrowDownload20Regular, ArrowLeft20Regular,
  Delete20Regular, Eye20Regular, MoreHorizontal20Regular,
} from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { LocalizedTime } from '../components/LocalizedTime'
import { PeriodFilter } from '../components/PeriodFilter'
import { RowActions } from '../components/RowActions'
import { usePeriodFilter } from '../utils/usePeriodFilter'
import { useCsvExport, type ExportColumn } from '../utils/useCsvExport'
import {
  fetchAllSchemaVersionHistory, useDeleteSchemaVersionEntry, useDeleteSchemaVersionHistory,
  useCapabilities, useSchemaVersionHistory,
} from '../api/hooks'
import { formatApiError } from '../api/client'
import { confirmDelete } from '../utils/confirm'
import { formatDateTime } from '../utils/format'
import { clickableRowProps } from '../utils/a11y'
import type { SchemaVersionHistoryEntry } from '../api/types'
import { useTranslation } from 'react-i18next'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  header: { display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px' },
  headerLeft: { display: 'flex', alignItems: 'center', gap: '12px' },
  filters: { display: 'flex', alignItems: 'flex-end', gap: '12px', flexWrap: 'wrap' },
  table: { tableLayout: 'fixed', width: '100%' },
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' }, cursor: 'pointer' },
  truncate: { display: 'block', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' },
  colTime:    { width: '180px' },
  colVersion: { width: '110px' },
  colStatus:  { width: '120px' },
  colCount:   { width: '120px' },
  colActions: { width: '52px' },
  cellId:     { maxWidth: 0 },
  mono:       { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
})

/**
 * Admin page listing the saved version snapshots for a single schema (one per save). Mirrors the
 * audit "Changes" tab: a period filter, a three-dots menu (refresh / export / delete-all), and a
 * data table whose rows open the read-only point-in-time view. Deleting entries here is audited
 * server-side and never touches the live schema.
 */
export function SchemaVersionHistoryPage() {
  const s = useStyles()
  const { t } = useTranslation()
  const nav = useNavigate()
  const { name } = useParams<{ name: string }>()
  const { has } = useCapabilities()
  const isAdmin = has('schemas:manage')
  const queryClient = useQueryClient()

  const period = usePeriodFilter()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [pageError, setPageError] = useState<string | null>(null)

  const { data, isLoading, error } = useSchemaVersionHistory(name, { page, pageSize, from: period.from, to: period.to })
  const deleteEntry = useDeleteSchemaVersionEntry()
  const deleteAll = useDeleteSchemaVersionHistory()

  const items = data?.items ?? []
  const exportColumns: ExportColumn<SchemaVersionHistoryEntry>[] = [
    { header: t('schemasSubmissions.schemaVersions.changeDate'), value: h => h.changeDate },
    { header: t('schemasSubmissions.schemaVersions.author'), value: h => h.authorName ?? '' },
    { header: t('schemasSubmissions.schemaVersions.oldVersion'), value: h => h.oldVersion ?? '' },
    { header: t('schemasSubmissions.schemaVersions.newVersion'), value: h => h.newVersion },
    { header: t('schemasSubmissions.common.status'), value: h => h.enabled ? t('schemasSubmissions.common.published') : t('schemasSubmissions.common.draft') },
    { header: t('schemasSubmissions.common.submissions'), value: h => h.submissionCount },
  ]

  const csv = useCsvExport({
    filename: `${name}-version-history.csv`,
    columns: exportColumns,
    fetchAll: () => fetchAllSchemaVersionHistory(name!, { from: period.from, to: period.to }),
    onError: setPageError,
  })

  function onRefresh() {
    queryClient.invalidateQueries({ queryKey: ['schema-version-history', name] })
  }

  function viewEntry(entry: SchemaVersionHistoryEntry) {
    nav(`/schemas/${encodeURIComponent(name!)}/versions/${entry.id}`)
  }

  async function onDeleteEntry(entry: SchemaVersionHistoryEntry) {
    if (!confirmDelete(
      t('schemasSubmissions.schemaVersions.entry'),
      t('schemasSubmissions.schemaVersions.entryLabel', { version: entry.newVersion, date: formatDateTime(entry.changeDate) }),
      t('schemasSubmissions.schemaVersions.deleteEntryWarning'),
    )) return
    setPageError(null)
    try {
      await deleteEntry.mutateAsync({ name: name!, entryId: entry.id })
    } catch (e) {
      setPageError(formatApiError(e))
    }
  }

  async function onDeleteAll() {
    if (!confirmDelete(
      t('schemasSubmissions.schemaVersions.entireHistory'),
      name,
      t('schemasSubmissions.schemaVersions.deleteAllWarning'),
    )) return
    setPageError(null)
    try {
      await deleteAll.mutateAsync(name!)
      setPage(1)
    } catch (e) {
      setPageError(formatApiError(e))
    }
  }

  return (
    <div className={s.root}>
      <div className={s.header}>
        <div className={s.headerLeft}>
          <Button appearance="subtle" icon={<ArrowLeft20Regular />} onClick={() => nav('/schemas')}>{t('schemasSubmissions.common.back')}</Button>
          <Title2>{t('schemasSubmissions.schemaVersions.title', { name })}</Title2>
        </div>
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label={t('schemasSubmissions.common.moreActions')} />
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              <MenuItem icon={<ArrowClockwise20Regular />} onClick={onRefresh}>{t('schemasSubmissions.common.refresh')}</MenuItem>
              <MenuItem icon={<ArrowDownload20Regular />} disabled={csv.exporting} onClick={csv.exportList}>
                {csv.exporting ? t('schemasSubmissions.common.exporting') : t('schemasSubmissions.schemaVersions.exportCsv')}
              </MenuItem>
              {isAdmin && (
                <>
                  <MenuDivider />
                  <MenuItem
                    icon={<Delete20Regular />}
                    disabled={deleteAll.isPending}
                    onClick={onDeleteAll}
                    style={{ color: 'var(--colorPaletteRedForeground1)' }}
                  >
                    {t('schemasSubmissions.schemaVersions.deleteAll')}
                  </MenuItem>
                </>
              )}
            </MenuList>
          </MenuPopover>
        </Menu>
      </div>

      <div className={s.filters}>
        <PeriodFilter state={period} onChange={() => setPage(1)} />
      </div>

      {pageError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>{t('schemasSubmissions.common.actionFailed')}</MessageBarTitle>
            {pageError}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>{t('schemasSubmissions.common.loadFailed')}</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Table size="small" className={s.table}>
        <TableHeader>
          <TableRow>
            <TableHeaderCell className={s.colTime}>{t('schemasSubmissions.schemaVersions.changeDate')}</TableHeaderCell>
            <TableHeaderCell>{t('schemasSubmissions.schemaVersions.author')}</TableHeaderCell>
            <TableHeaderCell className={s.colVersion}>{t('schemasSubmissions.schemaVersions.oldVersion')}</TableHeaderCell>
            <TableHeaderCell className={s.colVersion}>{t('schemasSubmissions.schemaVersions.newVersion')}</TableHeaderCell>
            <TableHeaderCell className={s.colStatus}>{t('schemasSubmissions.common.status')}</TableHeaderCell>
            <TableHeaderCell className={s.colCount}>{t('schemasSubmissions.common.submissions')}</TableHeaderCell>
            <TableHeaderCell className={s.colActions} aria-label={t('schemasSubmissions.common.actions')} />
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={7}>{t('schemasSubmissions.common.loading')}</GridMessageRow>}
          {!isLoading && items.length === 0 && (
            <GridMessageRow colSpan={7}>{t('schemasSubmissions.schemaVersions.empty')}</GridMessageRow>
          )}
          {items.map(entry => (
            <TableRow
              key={entry.id}
              className={s.row}
              {...clickableRowProps(() => viewEntry(entry), t('schemasSubmissions.schemaVersions.viewVersion', { version: entry.newVersion }))}
            >
              <TableCell className={s.colTime}>
                <LocalizedTime className={s.truncate} value={entry.changeDate} />
              </TableCell>
              <TableCell className={s.cellId}>
                <span className={s.truncate}>{entry.authorName || '—'}</span>
              </TableCell>
              <TableCell className={s.colVersion}>{entry.oldVersion ?? '—'}</TableCell>
              <TableCell className={s.colVersion}>
                {entry.newVersion}
                {entry.versionBumped && <> <Badge appearance="tint" color="brand" size="small">{t('schemasSubmissions.schemaVersions.bumped')}</Badge></>}
              </TableCell>
              <TableCell className={s.colStatus}>
                <Badge appearance="outline" color={entry.enabled ? 'success' : 'subtle'}>
                  {entry.enabled ? t('schemasSubmissions.common.published') : t('schemasSubmissions.common.draft')}
                </Badge>
              </TableCell>
              <TableCell className={s.colCount}>{entry.submissionCount}</TableCell>
              <TableCell className={s.colActions} onClick={ev => ev.stopPropagation()}>
                <RowActions
                  ariaLabel={t('schemasSubmissions.schemaVersions.actionsForVersion', { version: entry.newVersion })}
                  actions={[
                    { key: 'view', label: t('schemasSubmissions.schemaVersions.viewThisVersion'), icon: <Eye20Regular />, onClick: () => viewEntry(entry) },
                    ...(isAdmin
                      ? [{
                          key: 'delete',
                          label: t('schemasSubmissions.schemaVersions.deleteThisEntry'),
                          icon: <Delete20Regular />,
                          destructive: true,
                          disabled: deleteEntry.isPending,
                          onClick: () => onDeleteEntry(entry),
                        }]
                      : []),
                  ]}
                />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <GridPager
        page={page}
        pageSize={pageSize}
        total={data?.total ?? 0}
        onPageChange={setPage}
        onPageSizeChange={(n) => { setPageSize(n); setPage(1) }}
      />
    </div>
  )
}
