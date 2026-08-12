import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Avatar, Badge, MessageBarBody, MessageBarTitle,
  Menu, MenuButton, MenuDivider, MenuItem, MenuList, MenuPopover, MenuTrigger,
  Table, TableBody, TableCell, TableCellLayout, TableHeader, TableHeaderCell, TableRow,
  Title2, Tooltip, Toolbar,
  makeStyles, tokens,
} from '@fluentui/react-components'
import {
  ArrowClockwise20Regular, ArrowUpload20Regular, Delete20Regular, DocumentText20Regular, MoreHorizontal20Regular, Open20Regular,
} from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { LocalizedTime } from '../components/LocalizedTime'
import { RowActions } from '../components/RowActions'
import { GridMessageRow, GridPager, DEFAULT_PAGE_SIZE } from '../components/GridPager'
import { useCapabilities, useDeleteReport, useReports, useUploadReport } from '../api/hooks'
import { formatApiError } from '../api/client'
import { pickTextFile } from '../utils/download'
import { clickableRowProps } from '../utils/a11y'
import type { Report } from '../api/types'
import { useTranslation } from 'react-i18next'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  toolbar: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  // Same fixed-layout trick the other grids use so long labels truncate instead of pushing
  // the action menu off-screen.
  table: { tableLayout: 'fixed', width: '100%' },
  row: { '& > td': { paddingTop: '10px', paddingBottom: '10px' } },
  nameCell: { maxWidth: 0 },
  truncate: {
    display: 'block',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  colType:    { width: '120px' },
  colTargets: { maxWidth: 0 },
  colCreated:   { width: '110px' },
  colCreatedBy: { width: '140px' },
  colActions: { width: '80px', textAlign: 'right' },
  actionsHeader: { textAlign: 'right' },
  actionsCell:   { textAlign: 'right' },
  rowClickable: {
    cursor: 'pointer',
    ':focus-visible': { outline: `2px solid ${tokens.colorStrokeFocus2}`, outlineOffset: '-2px' },
  },
  // The targets cell hosts a horizontal chip strip; allow it to scroll within its column
  // instead of wrapping to a second line which would inflate row height across the grid.
  chipStrip: {
    display: 'flex',
    gap: '4px',
    alignItems: 'center',
    overflowX: 'hidden',
    whiteSpace: 'nowrap',
  },
})

export function ReportsPage() {
  const s = useStyles()
  const { t } = useTranslation()
  const nav = useNavigate()
  const { has } = useCapabilities()
  const isAdmin = has('reports:manage')

  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const { data, isLoading, error, refetch } = useReports({ page, pageSize })
  const upload = useUploadReport()
  const del = useDeleteReport()

  const [pageError, setPageError] = useState<string | null>(null)

  const items = data?.items ?? []

  async function onUpload() {
    setPageError(null)
    try {
      const file = await pickTextFile()
      if (!file.content.trim()) throw new Error(t('reports.validation.emptyFile'))
      await upload.mutateAsync(file)
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e)
      // Silently swallow the picker-cancelled case; everything else is a real error.
      if (msg === 'No file selected.') return
      setPageError(formatApiError(e))
    }
  }

  async function onDelete(r: Report) {
    if (!window.confirm(t('reports.confirmDelete', { target: r.label || r.name }))) return
    setPageError(null)
    try {
      await del.mutateAsync(r.id)
    } catch (e) {
      setPageError(formatApiError(e))
    }
  }

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <Title2>{t('reports.title')}</Title2>
        <Toolbar>
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <MenuButton appearance="subtle" icon={<MoreHorizontal20Regular />} aria-label={t('reports.actions.more')} />
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                <MenuItem icon={<ArrowClockwise20Regular />} onClick={() => refetch()}>{t('reports.actions.refresh')}</MenuItem>
                {isAdmin && (
                  <>
                    <MenuDivider />
                    <MenuItem icon={<ArrowUpload20Regular />} onClick={onUpload}>{t('reports.actions.upload')}</MenuItem>
                  </>
                )}
              </MenuList>
            </MenuPopover>
          </Menu>
        </Toolbar>
      </div>

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>{t('reports.messages.loadFailed')}</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {pageError && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>{t('reports.messages.actionFailed')}</MessageBarTitle>
            {pageError}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      <Table size="small" className={s.table}>
        <TableHeader>
          <TableRow>
            <TableHeaderCell>{t('reports.fields.name')}</TableHeaderCell>
            <TableHeaderCell className={s.colType}>{t('reports.fields.type')}</TableHeaderCell>
            <TableHeaderCell className={s.colTargets}>{t('reports.fields.targets')}</TableHeaderCell>
            <TableHeaderCell className={s.colCreated}>{t('reports.fields.created')}</TableHeaderCell>
            <TableHeaderCell className={s.colCreatedBy}>{t('reports.fields.createdBy')}</TableHeaderCell>
            <TableHeaderCell className={`${s.colActions} ${s.actionsHeader}`}>{t('reports.fields.actions')}</TableHeaderCell>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <GridMessageRow colSpan={6}>{t('reports.messages.loading')}</GridMessageRow>}
          {!isLoading && items.length === 0 && (
            <GridMessageRow colSpan={6}>{t(isAdmin ? 'reports.messages.emptyAdmin' : 'reports.messages.empty')}</GridMessageRow>
          )}
          {items.map(r => (
            <TableRow
              key={r.id}
              className={`${s.row} ${s.rowClickable}`}
              {...clickableRowProps(() => nav(`/reports/${encodeURIComponent(r.name)}`), t('reports.aria.open', { name: r.label || r.name }))}
            >
              <TableCell className={s.nameCell}>
                <TableCellLayout
                  media={<Avatar name={r.label || r.name} icon={<DocumentText20Regular />} color="forest" size={32} />}
                  description={r.description ? (
                    <Tooltip content={r.description} relationship="description">
                      <span>{r.description}</span>
                    </Tooltip>
                  ) : undefined}
                >
                  <Tooltip content={r.label || r.name} relationship="label">
                    <strong className={s.truncate}>{r.label || r.name}</strong>
                  </Tooltip>
                </TableCellLayout>
              </TableCell>
              <TableCell>
                <Badge appearance="outline" color={r.type === 'Single' ? 'informative' : 'brand'}>
                  {t(r.type === 'Single' ? 'reports.types.single' : 'reports.types.aggregate')}
                </Badge>
              </TableCell>
              <TableCell className={s.colTargets}>
                <TargetChips report={r} />
              </TableCell>
              <TableCell className={s.colCreated}>
                <Tooltip content={<LocalizedTime value={r.createdAt} />} relationship="label">
                  <LocalizedTime className={s.truncate} value={r.createdAt} dateOnly />
                </Tooltip>
              </TableCell>
              <TableCell className={s.colCreatedBy}>
                <Tooltip content={r.createdBy || '—'} relationship="label">
                  <span className={s.truncate}>{r.createdBy || '—'}</span>
                </Tooltip>
              </TableCell>
              <TableCell className={s.actionsCell} onClick={e => e.stopPropagation()}>
                <RowActions
                  ariaLabel={t('reports.aria.actionsFor', { name: r.name })}
                  actions={[
                    { key: 'view', label: t('reports.actions.view'), icon: <Open20Regular />, onClick: () => nav(`/reports/${encodeURIComponent(r.name)}`) },
                    ...(isAdmin
                      ? [{ key: 'delete', label: t('reports.actions.delete'), icon: <Delete20Regular />, destructive: true, onClick: () => onDelete(r) }]
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

const useChipStyles = makeStyles({
  // Strip with a fade-out so we always hint that more chips exist past the right edge instead
  // of clipping mid-text.
  strip: { display: 'flex', gap: '4px', overflow: 'hidden', whiteSpace: 'nowrap' },
})

function TargetChips({ report }: { report: Report }) {
  const s = useChipStyles()
  const { t } = useTranslation()
  if (!report.targetSchemaNames || report.targetSchemaNames.length === 0) {
    return <Badge appearance="tint" color="success">{t('reports.targets.global')}</Badge>
  }
  const visible = report.targetSchemaNames.slice(0, 4)
  const overflow = report.targetSchemaNames.length - visible.length
  return (
    <div className={s.strip}>
      {visible.map(name => (
        <Badge key={name} appearance="outline" color="subtle">{name}</Badge>
      ))}
      {overflow > 0 && (
        <Tooltip content={report.targetSchemaNames.slice(visible.length).join(', ')} relationship="description">
          <Badge appearance="ghost" color="subtle">+{overflow}</Badge>
        </Tooltip>
      )}
    </div>
  )
}
