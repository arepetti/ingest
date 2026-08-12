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
import { LocalizedTime } from '../components/LocalizedTime'
import { formatApiError } from '../api/client'
import {
  useReport, useRenderReport, useSchemas, useSubmissions, useMySubmissions, useCapabilities,
} from '../api/hooks'
import { downloadText } from '../utils/download'
import { formatDateTime } from '../utils/format'
import type { RenderReportRequest, ReportRenderResponse } from '../api/types'
import { Trans, useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'

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

function periodLabel(t: TFunction, period: Period): string {
  return t(`reports.view.periods.${period}`)
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
  const { t } = useTranslation()
  const { name } = useParams<{ name: string }>()
  const { has } = useCapabilities()
  // Without cross-service read the picker can only offer the caller's own submissions.
  const isService = !has('submissions:read')

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
        service: s.serviceName ?? s.serviceAccountId,
        submittedAt: s.submittedAt,
        count: s.samples.length,
        text: t('reports.view.submissionOption', {
          service: s.serviceName ?? s.serviceAccountId,
          date: formatDateTime(s.submittedAt),
          count: s.samples.length,
        }),
      }))
  }, [report, submissions, schemaName, t])

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
          <Link to="/reports">{t('reports.view.back')}</Link>
        </Button>
        <Title2>{report?.label || report?.name || t('reports.singular')}</Title2>
        {report && (
          <Badge appearance="outline" color={report.type === 'Single' ? 'informative' : 'brand'}>
            {t(report.type === 'Single' ? 'reports.types.single' : 'reports.types.aggregate')}
          </Badge>
        )}
      </div>

      {report?.description && <Text className={s.meta}>{report.description}</Text>}

      {error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>{t('reports.view.loadFailed')}</MessageBarTitle>
            {formatApiError(error)}
          </MessageBarBody>
        </AutoScrollMessageBar>
      )}

      {isLoading && <Spinner label={t('reports.view.loading')} />}

      {report && (
        <div className={s.filters}>
          {/* Schema picker: only meaningful when there's more than one option. Hidden for
              single-target reports because the choice is forced. */}
          {(report.targetSchemaNames.length !== 1) && (
            <Field label={t('reports.view.schema')} className={s.filterField}>
              <Dropdown
                value={schemaName ?? ''}
                selectedOptions={schemaName ? [schemaName] : []}
                onOptionSelect={(_, d) => setSchemaName(d.optionValue || undefined)}
                placeholder={t(report.targetSchemaNames.length === 0
                  ? 'reports.view.pickGlobalSchema'
                  : 'reports.view.pickSchema')}
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

          <Field label={t('reports.view.period')} className={s.filterField}>
            <Dropdown
              value={periodLabel(t, period)}
              selectedOptions={[period]}
              onOptionSelect={(_, d) => setPeriod((d.optionValue as Period) ?? 'thisMonth')}
            >
              {(['thisMonth', 'lastWeek', 'lastMonth', 'lastYear', 'custom'] as Period[]).map(p => (
                <Option key={p} value={p} text={periodLabel(t, p)}>{periodLabel(t, p)}</Option>
              ))}
            </Dropdown>
          </Field>

          {period === 'custom' && (
            <>
              <Field label={t('reports.view.from')} className={s.filterField}>
                <Input type="datetime-local" value={customFrom} onChange={(_, v) => setCustomFrom(v.value)} />
              </Field>
              <Field label={t('reports.view.to')} className={s.filterField}>
                <Input type="datetime-local" value={customTo} onChange={(_, v) => setCustomTo(v.value)} />
              </Field>
            </>
          )}

          {report.type === 'Single' && (
            <Field label={t('reports.view.submission')} className={s.filterField}>
              <Dropdown
                value={submissionId
                  ? (submissionOptions.find(o => o.id === submissionId)?.text ?? submissionId)
                  : ''}
                selectedOptions={submissionId ? [submissionId] : []}
                onOptionSelect={(_, d) => setSubmissionId(d.optionValue || undefined)}
                placeholder={t(submissionOptions.length === 0
                  ? 'reports.view.noMatchingSubmissions'
                  : 'reports.view.pickSubmission')}
              >
                {submissionOptions.map(o => (
                  <Option key={o.id} value={o.id} text={o.text}>
                    <Trans
                      i18nKey="reports.view.submissionOptionRich"
                      values={{ service: o.service, count: o.count }}
                      components={{ submittedAt: <LocalizedTime value={o.submittedAt} /> }}
                    />
                  </Option>
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
              {t(render.isPending ? 'reports.view.rendering' : 'reports.view.render')}
            </Button>
          </div>
        </div>
      )}

      {render.error && (
        <AutoScrollMessageBar intent="error">
          <MessageBarBody>
            <MessageBarTitle>{t('reports.view.renderFailed')}</MessageBarTitle>
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
  const { t } = useTranslation()
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
      title={t('reports.view.frameTitle', { name: rendered.reportLabel || rendered.reportName })}
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
            {t('reports.view.collapse')}
          </ToolbarButton>
          <ToolbarButton icon={<ArrowDownload20Regular />} onClick={onDownload}>
            {t('reports.view.download')}
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
          {t('reports.view.expand')}
        </ToolbarButton>
        <ToolbarButton
          icon={<Open20Regular />}
          onClick={() => {
            // Pop the rendered HTML into a standalone window — handy for printing or sharing.
            const w = window.open('', '_blank', 'noopener,noreferrer')
            if (w) { w.document.write(rendered.html); w.document.close() }
          }}
        >
          {t('reports.view.openNewTab')}
        </ToolbarButton>
        <ToolbarButton icon={<ArrowDownload20Regular />} onClick={onDownload}>
          {t('reports.view.download')}
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
