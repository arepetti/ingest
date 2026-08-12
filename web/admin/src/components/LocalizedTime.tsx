import { formatDate, formatDateTime } from '../utils/format'

interface LocalizedTimeProps {
  value?: string | null
  dateOnly?: boolean
  options?: Intl.DateTimeFormatOptions
  className?: string
}

/**
 * Renders a machine-readable timestamp with text formatted for the active UI locale.
 * Empty or invalid values use the standard em dash and do not emit misleading metadata.
 */
export function LocalizedTime({
  value,
  dateOnly = false,
  options,
  className,
}: LocalizedTimeProps) {
  if (!value || Number.isNaN(new Date(value).getTime())) return <span className={className}>—</span>

  return (
    <time className={className} dateTime={value}>
      {dateOnly ? formatDate(value, options) : formatDateTime(value, options)}
    </time>
  )
}
