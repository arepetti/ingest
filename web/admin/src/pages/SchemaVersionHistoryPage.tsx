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

const EXPORT_COLUMNS: ExportColumn<SchemaVersionHistoryEntry>[] = [
  { header: 'Change date', value: h => h.changeDate },
  { header: 'Author', value: h => h.authorName ?? '' },
  { header: 'Old version', value: h => h.oldVersion ?? '' },
  { header: 'New version', value: h => h.newVersion },
  { header: 'Status', value: h => (h.enabled ? 'Published' : 'Draft') },
  { header: 'Submissions', value: h => h.submissionCount },
]

/**
 * Admin page listing the saved version snapshots for a single schema (one per save). Mirrors the
 * audit "Changes" tab: a period filter, a three-dots menu (refresh / export / delete-all), and a
 * data table whose rows open the read-only point-in-time view. Deleting entries here is audited
 * server-side and never touches the live schema.
 */
export function SchemaVersionHistoryPage() {
  const s = useStyles()
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

  const csv = useCsvExport({
    filename: `${name}-version-history.csv`,
    columns: EXPORT_COLUMNS,
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
    if (!confirmDelete('version-history entry', `v${entry.newVersion} from ${formatDateTime(entry.changeDate)}`,
      'This removes the snapshot from the history only. The current schema is unaffected. This cannot be undone.')) return
    setPageError(null)
    try {
      await deleteEntry.mutateAsync({ name: name!, entryId: entry.id })
    } catch (e) {
      setPageError(formatApiError(e))
    }
  }

  async function onDeleteAll() {
    if (!confirmDelete('entire version history', name,
      'This removes every saved snapshot for this schema. The current schema is unaffected. This cannot be undone.')) return
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
          <Button appearance="subtle" icon={<ArrowLeft20Regular />} onClick={() => nav('/schemas')}>Back</Button>
          <Title2>Version history — {name}</Title2>
        </div>
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label="More actions" />
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              <MenuItem icon={<ArrowClockwise20Regular />} onClick={onRefresh}>Refresh</MenuItem>
              <MenuItem icon={<ArrowDownload20Regular />} disabled={csv.exporting} onClick={csv.exportList}>
                {csv.exporting ? 'Exporting…' : 'Export CSV'}
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
                    Delete all history
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
            <MessageBarTitle>Could not complete the action</MessageBarTitle>
            {pageError}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Failed to load</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Table size="small" className={s.table}>
        <TableHeader>
          <TableRow>
            <TableHeaderCell className={s.colTime}>Change date</TableHeaderCell>
            <TableHeaderCell>Author</TableHeaderCell>
            <TableHeaderCell className={s.colVersion}>Old version</TableHeaderCell>
            <TableHeaderCell className={s.colVersion}>New version</TableHeaderCell>
            <TableHeaderCell className={s.colStatus}>Status</TableHeaderCell>
            <TableHeaderCell className={s.colCount}>Submissions</TableHeaderCell>
            <TableHeaderCell className={s.colActions} aria-label="Actions" />
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={7}>Loading…</GridMessageRow>}
          {!isLoading && items.length === 0 && (
            <GridMessageRow colSpan={7}>No version history recorded.</GridMessageRow>
          )}
          {items.map(entry => (
            <TableRow
              key={entry.id}
              className={s.row}
              {...clickableRowProps(() => viewEntry(entry), `View version ${entry.newVersion}`)}
            >
              <TableCell className={s.colTime}>
                <span className={s.truncate}>{formatDateTime(entry.changeDate)}</span>
              </TableCell>
              <TableCell className={s.cellId}>
                <span className={s.truncate}>{entry.authorName || '—'}</span>
              </TableCell>
              <TableCell className={s.colVersion}>{entry.oldVersion ?? '—'}</TableCell>
              <TableCell className={s.colVersion}>
                {entry.newVersion}
                {entry.versionBumped && <> <Badge appearance="tint" color="brand" size="small">bumped</Badge></>}
              </TableCell>
              <TableCell className={s.colStatus}>
                <Badge appearance="outline" color={entry.enabled ? 'success' : 'subtle'}>
                  {entry.enabled ? 'Published' : 'Draft'}
                </Badge>
              </TableCell>
              <TableCell className={s.colCount}>{entry.submissionCount}</TableCell>
              <TableCell className={s.colActions} onClick={ev => ev.stopPropagation()}>
                <RowActions
                  ariaLabel={`Actions for version ${entry.newVersion}`}
                  actions={[
                    { key: 'view', label: 'View this version', icon: <Eye20Regular />, onClick: () => viewEntry(entry) },
                    ...(isAdmin
                      ? [{
                          key: 'delete',
                          label: 'Delete this entry',
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
