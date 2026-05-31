import type { ReactNode } from 'react'
import {
  Button, Dropdown, Option, TableCell, TableRow,
  makeStyles, tokens,
} from '@fluentui/react-components'

/** Page-size choices shared by every grid. */
export const PAGE_SIZE_OPTIONS = [10, 25, 50, 100]

/** Default page size grids start on. */
export const DEFAULT_PAGE_SIZE = 25

const useStyles = makeStyles({
  root: {
    display: 'flex',
    gap: '16px',
    alignItems: 'center',
    justifyContent: 'flex-end',
    flexWrap: 'wrap',
  },
  group: { display: 'flex', gap: '8px', alignItems: 'center' },
  label: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  info: { color: tokens.colorNeutralForeground2, fontSize: tokens.fontSizeBase200 },
  sizeDropdown: { minWidth: '76px' },
})

/**
 * Shared pagination footer for the admin grids. Owns the rows-per-page selector, the
 * "x–y of N" summary, and the prev/next buttons; the page/size state itself lives in the parent
 * so it can flow into the data hook's query key. Changing the page size is the caller's cue to
 * reset back to page 1 (see `onPageSizeChange`).
 */
export function GridPager({
  page, pageSize, total,
  onPageChange, onPageSizeChange,
  pageSizeOptions = PAGE_SIZE_OPTIONS,
}: {
  page: number
  pageSize: number
  total: number
  onPageChange: (page: number) => void
  onPageSizeChange: (pageSize: number) => void
  pageSizeOptions?: number[]
}) {
  const s = useStyles()
  const totalPages = Math.max(1, Math.ceil(total / pageSize))
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(total, page * pageSize)

  return (
    <div className={s.root}>
      <div className={s.group}>
        <span className={s.label}>Rows per page</span>
        <Dropdown
          className={s.sizeDropdown}
          size="small"
          selectedOptions={[String(pageSize)]}
          value={String(pageSize)}
          onOptionSelect={(_, d) => { if (d.optionValue) onPageSizeChange(Number(d.optionValue)) }}
        >
          {pageSizeOptions.map(n => <Option key={n} value={String(n)}>{String(n)}</Option>)}
        </Dropdown>
      </div>
      <span className={s.info}>{total === 0 ? '0 results' : `${from}–${to} of ${total}`}</span>
      <div className={s.group}>
        <Button size="small" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>Previous</Button>
        <span className={s.info}>Page {page} of {totalPages}</span>
        <Button size="small" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>Next</Button>
      </div>
    </div>
  )
}

const useRowStyles = makeStyles({
  cell: { color: tokens.colorNeutralForeground3, paddingTop: '16px', paddingBottom: '16px', textAlign: 'center' },
})

/**
 * A single full-width table row used for loading / empty / error placeholders so every grid
 * renders these states consistently. `colSpan` must match the grid's column count.
 */
export function GridMessageRow({ colSpan, children }: { colSpan: number; children: ReactNode }) {
  const s = useRowStyles()
  return (
    <TableRow>
      <TableCell colSpan={colSpan} className={s.cell}>{children}</TableCell>
    </TableRow>
  )
}
