import { useEffect, useMemo, useState } from 'react'
import {
  Button, Dialog, DialogActions, DialogBody, DialogContent, DialogSurface, DialogTitle,
  MessageBar, MessageBarBody, Spinner, Text, makeStyles, tokens,
} from '@fluentui/react-components'
import { ArrowClockwise20Regular, Dismiss24Regular } from '@fluentui/react-icons'
import type { Schema } from '../api/types'
import { formatApiError } from '../api/client'
import { buildDependencyGraph, type DependencyEdge, type DependencyEdgeKind, type DependencyGraph } from '../utils/schemaDependencies'

const useStyles = makeStyles({
  surface: {
    maxWidth: '96vw',
    width: '1180px',
    height: '88vh',
    display: 'flex',
    flexDirection: 'column',
  },
  body: { minHeight: 0, flex: 1 },
  content: { display: 'flex', flexDirection: 'column', gap: '12px', overflow: 'hidden', flex: 1, minHeight: 0 },
  centered: { flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '12px' },
  grid: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 3fr) minmax(220px, 1fr)',
    gap: '16px',
    flex: 1,
    minHeight: 0,
    '@media (max-width: 900px)': { gridTemplateColumns: '1fr' },
  },
  diagramWrap: {
    position: 'relative',
    overflow: 'auto',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  canvas: { position: 'relative' },
  nodeBox: {
    position: 'absolute',
    transform: 'translate(-50%, -50%)',
    boxSizing: 'border-box',
    padding: '6px 10px',
    borderRadius: tokens.borderRadiusCircular,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightMedium,
    whiteSpace: 'nowrap',
    maxWidth: '170px',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    textAlign: 'center',
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground1,
    boxShadow: tokens.shadow2,
    color: tokens.colorNeutralForeground1,
  },
  nodeBoxCalculated: {
    border: `1.5px dashed ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorBrandBackground2,
  },
  legendCol: { overflowY: 'auto', minHeight: 0, display: 'flex', flexDirection: 'column', gap: '10px' },
  legendTitle: { fontWeight: tokens.fontWeightSemibold, fontSize: tokens.fontSizeBase300 },
  legendList: { listStyle: 'none', margin: 0, padding: 0, display: 'flex', flexDirection: 'column', gap: '8px' },
  legendRow: { display: 'flex', alignItems: 'center', gap: '8px', fontSize: tokens.fontSizeBase200 },
  legendSwatch: { flexShrink: 0 },
  legendNodeSwatch: {
    flexShrink: 0,
    width: '18px',
    height: '18px',
    borderRadius: tokens.borderRadiusCircular,
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground1,
    boxSizing: 'border-box',
  },
  legendNodeSwatchCalculated: {
    border: `1.5px dashed ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorBrandBackground2,
  },
  muted: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  actions: { display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '4px' },
})

/** Color + dash pattern per connector kind. Each combo differs in both, so it isn't color-only. */
const EDGE_META: Record<DependencyEdgeKind, { color: string; dash?: string; width: number; label: string }> = {
  calculated: { color: tokens.colorPaletteBlueForeground2, width: 2.2, label: 'Calculated from' },
  visibleIf: { color: tokens.colorPaletteTealForeground2, dash: '7 4', width: 1.8, label: 'Visible when' },
  enabledIf: { color: tokens.colorPalettePurpleForeground2, dash: '2 3 8 3', width: 1.8, label: 'Enabled when' },
  validation: { color: tokens.colorPaletteCranberryForeground2, dash: '10 3', width: 1.8, label: 'Valid when' },
  warning: { color: tokens.colorPaletteMarigoldForeground2, dash: '1 4', width: 1.8, label: 'Warns when' },
  submissionValidation: { color: tokens.colorNeutralForeground3, dash: '1 3', width: 1.4, label: 'Cross-value validation (schema-level)' },
}
const EDGE_KIND_ORDER: DependencyEdgeKind[] = ['calculated', 'visibleIf', 'enabledIf', 'validation', 'warning', 'submissionValidation']

const EMPTY_GRAPH: DependencyGraph = { nodes: [], edges: [] }

interface Point { x: number; y: number }

type LoadState =
  | { status: 'loading' }
  | { status: 'ready'; graph: DependencyGraph }
  | { status: 'error'; message: string }

/** Evenly place `count` points clockwise around a circle, starting at 12 o'clock. */
function circleLayout(count: number, radius: number, center: number): Point[] {
  if (count <= 1) return count === 1 ? [{ x: center, y: center }] : []
  return Array.from({ length: count }, (_, i) => {
    const angle = (2 * Math.PI * i) / count - Math.PI / 2
    return { x: center + radius * Math.cos(angle), y: center + radius * Math.sin(angle) }
  })
}

/** Group edges connecting the same unordered pair of nodes, so parallel edges can fan out. */
function groupByPair(edges: DependencyEdge[]): Map<string, DependencyEdge[]> {
  const groups = new Map<string, DependencyEdge[]>()
  for (const e of edges) {
    const key = [e.from.toLowerCase(), e.to.toLowerCase()].sort().join('|')
    const g = groups.get(key)
    if (g) g.push(e); else groups.set(key, [e])
  }
  return groups
}

const NODE_TRIM = 58 // px pulled back from each node's center so lines don't run under the box
const MARGIN = 100

/**
 * "Dependencies" diagram for a single schema: every value as a node, with a colored/dashed
 * connector for each rule that references another value (calculated-from, visible/enabled-if,
 * validation, warning, and schema-level cross-value validations). References are resolved by the
 * server's real NCalc parser (see `schemaDependencies.ts`) — this is a real dependency walk, not
 * a client-side guess.
 */
export function SchemaDependencyGraphDialog({ schema, open, onClose }: { schema: Schema; open: boolean; onClose: () => void }) {
  const s = useStyles()
  const [state, setState] = useState<LoadState>({ status: 'loading' })
  const [generation, setGeneration] = useState(0)

  // Kick off a (re)load on the open→true transition, done as a render-time reset (rather than
  // as the first line of the effect below) so the effect itself never calls setState
  // synchronously — see SchemaPreviewDialog for the same convention. The dialog is modal, so the
  // schema can't meaningfully change underneath while it's open; "Retry" bumps the same counter.
  const [wasOpen, setWasOpen] = useState(false)
  if (open !== wasOpen) {
    setWasOpen(open)
    if (open) {
      setState({ status: 'loading' })
      setGeneration(g => g + 1)
    }
  }

  useEffect(() => {
    if (!open) return
    let cancelled = false
    buildDependencyGraph(schema)
      .then(graph => { if (!cancelled) setState({ status: 'ready', graph }) })
      .catch((e: unknown) => { if (!cancelled) setState({ status: 'error', message: formatApiError(e) }) })
    return () => { cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `generation` is the retry trigger; `schema` is stable while the modal is open.
  }, [open, generation])

  const graph = state.status === 'ready' ? state.graph : EMPTY_GRAPH

  const nodeSpacing = 130
  const radius = Math.max(150, (graph.nodes.length * nodeSpacing) / (2 * Math.PI))
  const size = radius * 2 + MARGIN * 2
  const center = size / 2
  const positions = useMemo(() => circleLayout(graph.nodes.length, radius, center), [graph.nodes.length, radius, center])
  const positionByName = useMemo(() => {
    const m = new Map<string, Point>()
    graph.nodes.forEach((n, i) => m.set(n.name.toLowerCase(), positions[i]))
    return m
  }, [graph.nodes, positions])

  const pairGroups = useMemo(() => groupByPair(graph.edges), [graph.edges])

  const title = `Dependencies: ${schema.label || schema.name || 'Untitled schema'}`
  const usedKinds = EDGE_KIND_ORDER.filter(k => graph.edges.some(e => e.kind === k))

  return (
    <Dialog open={open} onOpenChange={(_, d) => { if (!d.open) onClose() }}>
      <DialogSurface className={s.surface}>
        <DialogBody className={s.body}>
          <DialogTitle action={<Button appearance="subtle" aria-label="Close" icon={<Dismiss24Regular />} onClick={onClose} />}>
            {title}
          </DialogTitle>
          <DialogContent className={s.content}>
            {state.status === 'loading' && (
              <div className={s.centered}>
                <Spinner label="Walking this schema's rules for dependencies…" />
              </div>
            )}

            {state.status === 'error' && (
              <MessageBar intent="error">
                <MessageBarBody>
                  Couldn&apos;t build the dependency graph: {state.message}
                  <Button
                    appearance="transparent" icon={<ArrowClockwise20Regular />}
                    style={{ marginLeft: '8px' }}
                    onClick={() => { setState({ status: 'loading' }); setGeneration(g => g + 1) }}
                  >
                    Retry
                  </Button>
                </MessageBarBody>
              </MessageBar>
            )}

            {state.status === 'ready' && graph.nodes.length === 0 && (
              <MessageBar intent="warning">
                <MessageBarBody>This schema has no usable values yet — add one to see its dependencies.</MessageBarBody>
              </MessageBar>
            )}

            {state.status === 'ready' && graph.nodes.length > 0 && (
              <div className={s.grid}>
                <div className={s.diagramWrap}>
                  <div className={s.canvas} style={{ width: size, height: size }}>
                    <svg width={size} height={size} style={{ position: 'absolute', inset: 0 }}>
                      <defs>
                        {EDGE_KIND_ORDER.map(kind => (
                          <marker
                            key={kind} id={`dep-arrow-${kind}`} viewBox="0 0 10 10"
                            refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"
                          >
                            <path d="M0,0 L10,5 L0,10 z" fill={EDGE_META[kind].color} />
                          </marker>
                        ))}
                      </defs>
                      {graph.edges.map((edge, i) => {
                        const from = positionByName.get(edge.from.toLowerCase())
                        const to = positionByName.get(edge.to.toLowerCase())
                        if (!from || !to) return null
                        const pairKey = [edge.from.toLowerCase(), edge.to.toLowerCase()].sort().join('|')
                        const group = pairGroups.get(pairKey) ?? [edge]
                        const idxInGroup = group.indexOf(edge)
                        const fanOffset = (idxInGroup - (group.length - 1) / 2) * 18
                        const path = edgePath(from, to, fanOffset + 22, NODE_TRIM)
                        const meta = EDGE_META[edge.kind]
                        return (
                          <path
                            key={i} d={path} fill="none" stroke={meta.color} strokeWidth={meta.width}
                            strokeDasharray={meta.dash} strokeLinecap="round"
                            markerEnd={edge.directed ? `url(#dep-arrow-${edge.kind})` : undefined}
                          >
                            <title>{`${meta.label} — ${edge.from} → ${edge.to}\n${edge.expression}`}</title>
                          </path>
                        )
                      })}
                    </svg>
                    {graph.nodes.map((n, i) => (
                      <div
                        key={n.name}
                        className={n.calculated ? `${s.nodeBox} ${s.nodeBoxCalculated}` : s.nodeBox}
                        style={{ left: positions[i].x, top: positions[i].y }}
                        title={n.label}
                      >
                        {n.label}
                      </div>
                    ))}
                  </div>
                </div>

                <div className={s.legendCol}>
                  <span className={s.legendTitle}>Legend</span>
                  <ul className={s.legendList}>
                    <li className={s.legendRow}>
                      <span className={s.legendNodeSwatch} />
                      <span>Value</span>
                    </li>
                    <li className={s.legendRow}>
                      <span className={`${s.legendNodeSwatch} ${s.legendNodeSwatchCalculated}`} />
                      <span>Calculated value</span>
                    </li>
                  </ul>
                  <Text className={s.muted}>
                    A connector points from a referenced value to the one whose rule depends on it.
                  </Text>
                  <ul className={s.legendList}>
                    {(usedKinds.length > 0 ? usedKinds : EDGE_KIND_ORDER).map(kind => (
                      <li key={kind} className={s.legendRow}>
                        <svg width="32" height="12" className={s.legendSwatch}>
                          <line
                            x1="1" y1="6" x2="31" y2="6"
                            stroke={EDGE_META[kind].color} strokeWidth={EDGE_META[kind].width}
                            strokeDasharray={EDGE_META[kind].dash} strokeLinecap="round"
                          />
                        </svg>
                        <span>{EDGE_META[kind].label}</span>
                      </li>
                    ))}
                  </ul>
                  {graph.edges.length === 0 && (
                    <Text className={s.muted}>No dependencies detected among these values yet.</Text>
                  )}
                </div>
              </div>
            )}
          </DialogContent>
          <DialogActions className={s.actions}>
            <Button appearance="secondary" onClick={onClose}>Close</Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  )
}

/**
 * Quadratic-bezier path between two node centers, pulled back from each end and bowed sideways
 * for a chord-diagram look. Both the pull-back and the bow are scaled down for short chords
 * (neighbouring nodes on the circle) — applying the full fixed amounts would otherwise shrink a
 * close pair's connector down to an invisible sliver.
 */
function edgePath(from: Point, to: Point, bow: number, trim: number): string {
  const dx = to.x - from.x
  const dy = to.y - from.y
  const len = Math.hypot(dx, dy) || 1
  const ux = dx / len
  const uy = dy / len
  const t = Math.min(trim, len * 0.32)
  const b = bow * Math.min(1, len / 220)
  const start = { x: from.x + ux * t, y: from.y + uy * t }
  const end = { x: to.x - ux * t, y: to.y - uy * t }
  const mx = (start.x + end.x) / 2
  const my = (start.y + end.y) / 2
  const px = -uy
  const py = ux
  const cx = mx + px * b
  const cy = my + py * b
  return `M ${start.x} ${start.y} Q ${cx} ${cy} ${end.x} ${end.y}`
}
