import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  Badge, Button, Dropdown, Field, Input, MessageBarBody, MessageBarTitle,
  Option, Spinner, Text, Title2, Toolbar, ToolbarButton,
  makeStyles, tokens,
} from '@fluentui/react-components'
import {
  ArrowDownload20Regular, ArrowLeft20Regular, ArrowMaximize20Regular, ArrowMinimize20Regular,
  Open20Regular, Play20Regular,
} from '@fluentui/react-icons'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { formatApiError } from '../api/client'
import {
  useReport, useRenderReport, useSchemas, useSubmissions, useMySubmissions, useMe,
} from '../api/hooks'
import { downloadText } from '../utils/download'
import type { RenderReportRequest, ReportRenderResponse } from '../api/types'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '16px' },
  header: { display: 'flex', alignItems: 'center', gap: '12px' },
  meta: { color: tokens.colorNeutralForeground3, fontSize: '13px' },
  // Filter bar: row of fields that wraps onto multiple lines on narrow screens. Each field has
  // its own minimum width so labels don't compress on top of the input.
  filters: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: '12px',
    alignItems: 'flex-end',
    padding: '12px 16px',
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: '6px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  // Each individual field has a sensible minimum width so the bar wraps cleanly when the
  // viewport is narrow.
  filterField: { minWidth: '200px', flex: '1 1 200px' },
  filterActions: { display: 'flex', alignItems: 'center', gap: '8px', marginLeft: 'auto' },

  // The viewer body: a sandboxed iframe rendering the produced HTML. Sized to fill the
  // remaining vertical space and bordered to set itself apart from the page chrome. We use
  // `srcdoc` rather than data:URLs because it avoids the same-origin warnings and keeps the
  // iframe sandboxable.
  viewer: {
    width: '100%',
    minHeight: '60vh',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px',
    backgroundColor: '#fff',
  },
  viewerExpanded: {
    position: 'fixed',
    inset: '0',
    width: '100vw',
    height: '100vh',
    zIndex: 50,
    backgroundColor: tokens.colorNeutralBackground1,
    padding: '12px',
    boxSizing: 'border-box',
    display: 'flex',
    flexDirection: 'column',
    gap: '8px',
  },
  expandedToolbar: { display: 'flex', alignItems: 'center', gap: '8px' },
})

// Period presets — same vocabulary as the submissions page so users don't have to relearn it
// when switching between "browse data" and "render report".
type Period = 'thisMonth' | 'lastWeek' | 'lastMonth' | 'lastYear' | 'custom'

const periodLabels: Record<Period, string> = {
  thisMonth: 'This month',
  lastWeek:  'Last 7 days',
  lastMonth: 'Last 30 days',
  lastYear:  'Last year',
  custom:    'Custom range',
}

function periodRange(period: Period, customFrom: string, customTo: string): { from?: string; to?: string } {
  const now = new Date()
  switch (period) {
    case 'thisMonth': {
      const start = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1))
      return { from: start.toISOString(), to: now.toISOString() }
    }
    case 'lastWeek':  return { from: addDays(now, -7).toISOString(),   to: now.toISOString() }
    case 'lastMonth': return { from: addDays(now, -30).toISOString(),  to: now.toISOString() }
    case 'lastYear':  return { from: addDays(now, -365).toISOString(), to: now.toISOString() }
    case 'custom': {
      return {
        from: customFrom ? new Date(customFrom).toISOString() : undefined,
        to:   customTo   ? new Date(customTo).toISOString()   : undefined,
      }
    }
  }
}

function addDays(d: Date, days: number): Date {
  const r = new Date(d)
  r.setDate(r.getDate() + days)
  return r
}

export function ReportViewPage() {
  const s = useStyles()
  const { name } = useParams<{ name: string }>()
  const { data: me } = useMe()
  const isService = me?.role === 'Service'

  const { data: report, isLoading, error } = useReport(name)
  const render = useRenderReport()

  const [period, setPeriod] = useState<Period>('thisMonth')
  const [customFrom, setCustomFrom] = useState('')
  const [customTo, setCustomTo] = useState('')
  const [schemaName, setSchemaName] = useState<string | undefined>(undefined)
  const [submissionId, setSubmissionId] = useState<string | undefined>(undefined)
  const [rendered, setRendered] = useState<ReportRenderResponse | null>(null)
  const [expanded, setExpanded] = useState(false)

  // When the report definition lands, pre-select a target schema so the filter bar has
  // something useful selected before the user clicks anything.
  useEffect(() => {
    if (!report) return
    // Seeding local UI state once the async report definition lands; not derivable during render.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (report.targetSchemaNames.length === 1) setSchemaName(report.targetSchemaNames[0])
  }, [report])

  // Render automatically the first time we have enough information (aggregate or single-target
  // reports with no submission needed). Subsequent renders need an explicit click so we don't
  // hammer the server every time the user tweaks the date input.
  const range = useMemo(() => periodRange(period, customFrom, customTo), [period, customFrom, customTo])

  // Source the schema and submission catalogues from the same hooks the rest of the SPA uses.
  // For Service-role callers we route through the "my" submissions hook so we don't trigger a
  // 403 against the admin endpoint.
  const schemasQuery = useSchemas(undefined, !isService)
  const adminSubmissionsQuery = useSubmissions(
    { page: 1, pageSize: 200, from: range.from, to: range.to },
    !isService && report?.type === 'Single',
  )
  const mySubmissionsQuery = useMySubmissions(
    { page: 1, pageSize: 200, from: range.from, to: range.to },
    !!isService && report?.type === 'Single',
  )
  const submissions = useMemo(
    () => (isService ? mySubmissionsQuery.data : adminSubmissionsQuery.data)?.items ?? [],
    [isService, mySubmissionsQuery.data, adminSubmissionsQuery.data],
  )

  // Filter submission options to those that mention the chosen schema. When the report is
  // global (no targets) we accept any submission.
  const submissionOptions = useMemo(() => {
    if (!report) return []
    const matches = (s: typeof submissions[number]) => {
      if (!schemaName) return true
      return s.samples.some(sa => sa.schemaName === schemaName)
    }
    return submissions
      .filter(matches)
      .map(s => ({
        id: s.id,
        text: `${s.serviceName ?? s.serviceAccountId} — ${new Date(s.submittedAt).toLocaleString()} (${s.samples.length} samples)`,
      }))
  }, [report, submissions, schemaName])

  const canRender = useMemo(() => {
    if (!report) return false
    // Aggregate with multiple targets needs a schema picked first.
    if (report.type === 'Aggregate') {
      const noSchemaPicked = !schemaName
      const hasMultiple = (report.targetSchemaNames?.length ?? 0) !== 1
      if (noSchemaPicked && hasMultiple) return false
    }
    // Single always needs a submission.
    if (report.type === 'Single' && !submissionId) return false
    return true
  }, [report, schemaName, submissionId])

  async function onRender() {
    if (!report || !canRender) return
    const req: RenderReportRequest = {
      schemaName: schemaName ?? null,
      submissionId: report.type === 'Single' ? submissionId ?? null : null,
      from: range.from ?? null,
      to: range.to ?? null,
    }
    try {
      const res = await render.mutateAsync({ name: report.name, req })
      setRendered(res)
    } catch {
      setRendered(null)
    }
  }

  return (
    <div className={s.root}>
      <div className={s.header}>
        <Button as="a" appearance="subtle" icon={<ArrowLeft20Regular />}>
          <Link to="/reports">Back</Link>
        </Button>
        <Title2>{report?.label || report?.name || 'Report'}</Title2>
        {report && (
          <Badge appearance="outline" color={report.type === 'Single' ? 'informative' : 'brand'}>
            {report.type}
          </Badge>
        )}
      </div>

      {report?.description && <Text className={s.meta}>{report.description}</Text>}

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Could not load the report</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {isLoading && <Spinner label="Loading report…" />}

      {report && (
        <div className={s.filters}>
          {/* Schema picker: only meaningful when there's more than one option. Hidden for
              single-target reports because the choice is forced. */}
          {(report.targetSchemaNames.length !== 1) && (
            <Field label="Schema" className={s.filterField}>
              <Dropdown
                value={schemaName ?? ''}
                selectedOptions={schemaName ? [schemaName] : []}
                onOptionSelect={(_, d) => setSchemaName(d.optionValue || undefined)}
                placeholder={report.targetSchemaNames.length === 0 ? 'Pick a schema (global report)…' : 'Pick a schema…'}
              >
                {(report.targetSchemaNames.length > 0
                  ? report.targetSchemaNames
                  : (schemasQuery.data?.items ?? []).map(sc => sc.name)
                ).map(n => (
                  <Option key={n} value={n} text={n}>{n}</Option>
                ))}
              </Dropdown>
            </Field>
          )}

          <Field label="Period" className={s.filterField}>
            <Dropdown
              value={periodLabels[period]}
              selectedOptions={[period]}
              onOptionSelect={(_, d) => setPeriod((d.optionValue as Period) ?? 'thisMonth')}
            >
              {(Object.keys(periodLabels) as Period[]).map(p => (
                <Option key={p} value={p} text={periodLabels[p]}>{periodLabels[p]}</Option>
              ))}
            </Dropdown>
          </Field>

          {period === 'custom' && (
            <>
              <Field label="From" className={s.filterField}>
                <Input type="datetime-local" value={customFrom} onChange={(_, v) => setCustomFrom(v.value)} />
              </Field>
              <Field label="To" className={s.filterField}>
                <Input type="datetime-local" value={customTo} onChange={(_, v) => setCustomTo(v.value)} />
              </Field>
            </>
          )}

          {report.type === 'Single' && (
            <Field label="Submission" className={s.filterField}>
              <Dropdown
                value={submissionId
                  ? (submissionOptions.find(o => o.id === submissionId)?.text ?? submissionId)
                  : ''}
                selectedOptions={submissionId ? [submissionId] : []}
                onOptionSelect={(_, d) => setSubmissionId(d.optionValue || undefined)}
                placeholder={submissionOptions.length === 0 ? 'No submissions match the filters' : 'Pick a submission…'}
              >
                {submissionOptions.map(o => (
                  <Option key={o.id} value={o.id} text={o.text}>{o.text}</Option>
                ))}
              </Dropdown>
            </Field>
          )}

          <div className={s.filterActions}>
            <Button
              appearance="primary"
              icon={<Play20Regular />}
              disabled={!canRender || render.isPending}
              onClick={onRender}
            >
              {render.isPending ? 'Rendering…' : 'Render'}
            </Button>
          </div>
        </div>
      )}

      {render.error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>Render failed</MessageBarTitle>
            {formatApiError(render.error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {rendered && (
        <ReportFrame
          rendered={rendered}
          expanded={expanded}
          onToggleExpand={() => setExpanded(e => !e)}
        />
      )}
    </div>
  )
}

function ReportFrame({
  rendered, expanded, onToggleExpand,
}: { rendered: ReportRenderResponse; expanded: boolean; onToggleExpand: () => void }) {
  const s = useStyles()
  // Save the exact HTML we're showing to the user as a standalone file. Done entirely
  // client-side — the server already shipped the bytes, no need for a download endpoint.
  // Filename pattern: <reportName>-YYYYMMDDhhmm.html, sanitised so anything weird in a
  // hand-rolled report name (it shouldn't happen — names are validated server-side — but
  // belts and braces) doesn't escape into the OS save dialog.
  const onDownload = () => {
    const slug = (rendered.reportName || 'report').replace(/[^A-Za-z0-9._-]+/g, '_')
    const stamp = formatTimestampForFile(new Date())
    downloadText(`${slug}-${stamp}.html`, rendered.html, 'text/html;charset=utf-8')
  }

  // `sandbox=""` strips every privilege — no script execution, no top-level navigation, no
  // form submission, no plugins. Reports render their template-produced HTML purely as
  // formatted text, which is exactly the threat model we want for admin-uploaded templates.
  const frame = (
    <iframe
      title={`Report: ${rendered.reportLabel || rendered.reportName}`}
      srcDoc={rendered.html}
      sandbox=""
      className={s.viewer}
      style={expanded ? { flex: 1, minHeight: 0, height: 'auto' } : undefined}
    />
  )

  if (expanded) {
    return (
      <div className={s.viewerExpanded}>
        <Toolbar className={s.expandedToolbar}>
          <ToolbarButton icon={<ArrowMinimize20Regular />} onClick={onToggleExpand}>
            Collapse
          </ToolbarButton>
          <ToolbarButton icon={<ArrowDownload20Regular />} onClick={onDownload}>
            Download
          </ToolbarButton>
          <Text>{rendered.reportLabel || rendered.reportName}</Text>
        </Toolbar>
        {frame}
      </div>
    )
  }

  return (
    <div>
      <Toolbar style={{ marginBottom: 8 }}>
        <ToolbarButton icon={<ArrowMaximize20Regular />} onClick={onToggleExpand}>
          Expand
        </ToolbarButton>
        <ToolbarButton
          icon={<Open20Regular />}
          onClick={() => {
            // Pop the rendered HTML into a standalone window — handy for printing or sharing.
            const w = window.open('', '_blank', 'noopener,noreferrer')
            if (w) { w.document.write(rendered.html); w.document.close() }
          }}
        >
          Open in new tab
        </ToolbarButton>
        <ToolbarButton icon={<ArrowDownload20Regular />} onClick={onDownload}>
          Download
        </ToolbarButton>
      </Toolbar>
      {frame}
    </div>
  )
}

/** Compact, filesystem-safe `YYYYMMDDhhmm` stamp for download filenames. */
function formatTimestampForFile(d: Date): string {
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}${pad(d.getHours())}${pad(d.getMinutes())}`
}
