import {
  Button, Input, Textarea, Tooltip, makeStyles, tokens,
} from '@fluentui/react-components'
import {
  Add20Regular, Delete16Regular, DocumentBulletList20Regular,
  Folder20Regular, ReOrderDotsVertical20Regular,
} from '@fluentui/react-icons'
import { useCallback, useMemo, useState } from 'react'
import type { Schema, SchemaLayoutNode, SchemaValue } from '../api/types'
import { unassignedValues, validateLayout } from '../utils/layout'

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: '12px' },
  toolbar: { display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center' },
  tray: {
    padding: '12px',
    border: `1px dashed ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px',
    minHeight: '60px',
    backgroundColor: tokens.colorNeutralBackground3,
  },
  trayHeader: {
    color: tokens.colorNeutralForeground3,
    fontWeight: 600,
    fontSize: '12px',
    textTransform: 'uppercase',
    marginBottom: '6px',
  },
  chip: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    padding: '4px 8px',
    margin: '2px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '14px',
    background: tokens.colorNeutralBackground1,
    cursor: 'grab',
    userSelect: 'none',
    fontSize: '12px',
  },
  chipDragging: { opacity: 0.5 },
  tree: {
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
    padding: '12px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: '6px',
    minHeight: '120px',
  },
  emptyTree: {
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
    textAlign: 'center',
    padding: '24px',
  },
  node: {
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
    padding: '6px 8px',
    borderRadius: '4px',
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    background: tokens.colorNeutralBackground1,
  },
  nodeHeader: { display: 'flex', alignItems: 'center', gap: '6px' },
  nodeHandle: { cursor: 'grab', color: tokens.colorNeutralForeground3, flexShrink: 0 },
  nodeIcon: { color: tokens.colorNeutralForeground2, flexShrink: 0 },
  nodeTitle: { fontWeight: 600, flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  nodeChildren: { display: 'flex', flexDirection: 'column', gap: '4px', paddingLeft: '24px', marginTop: '4px' },
  dropZone: {
    minHeight: '8px',
    borderRadius: '4px',
    transition: 'background 80ms, min-height 80ms',
  },
  dropZoneActive: {
    background: tokens.colorBrandBackground2,
    minHeight: '16px',
  },
  sectionInputs: { display: 'flex', flexDirection: 'column', gap: '4px', marginTop: '4px' },
  issues: {
    display: 'flex',
    flexDirection: 'column',
    gap: '4px',
    fontSize: '12px',
  },
  issueError:   { color: tokens.colorPaletteRedForeground1 },
  issueWarning: { color: tokens.colorPaletteDarkOrangeForeground1 },
})

/**
 * Drag-and-drop editor for a schema's UI-only layout tree.
 *
 *  - The left tray lists values that haven't been placed yet (drag a chip onto a drop zone
 *    in the tree to insert it).
 *  - The tree mirrors `schema.layout`; sections accept children, values are leaves.
 *  - Sections are added via the "Add section" button (root) or the inline "+" inside an
 *    existing section.
 *  - Browser-native HTML5 DnD keeps the dependency footprint minimal — fine for the size of
 *    tree an admin will realistically build.
 *
 * The component is "controlled" — every edit produces a new layout via `onChange`.
 */
export function LayoutTreeEditor({
  schema,
  onChange,
}: {
  schema: Schema
  onChange: (next: SchemaLayoutNode[]) => void
}) {
  const s = useStyles()
  const layout = useMemo<SchemaLayoutNode[]>(() => schema.layout ?? [], [schema.layout])
  const unassigned = useMemo(() => unassignedValues(schema), [schema])
  const issues = useMemo(() => validateLayout(schema), [schema])

  // The dragged payload carries the source: either an unassigned value (by name) or a path
  // into the existing tree (so we can splice it out before reinserting).
  const [dragging, setDragging] = useState<DragPayload | null>(null)

  const set = useCallback((next: SchemaLayoutNode[]) => onChange(next), [onChange])

  const addSection = () => set([...layout, { kind: 'section', caption: 'New section', items: [] }])
  const addSectionInside = (path: number[]) => {
    set(insertAt(layout, [...path, indexOfLastChild(layout, path) + 1], { kind: 'section', caption: 'New section', items: [] }))
  }
  const removeAt = (path: number[]) => set(removeAtPath(layout, path))
  const updateSection = (path: number[], patch: Partial<Extract<SchemaLayoutNode, { kind: 'section' }>>) =>
    set(mapAtPath(layout, path, (n) => n.kind === 'section' ? { ...n, ...patch } : n))

  const handleDrop = (target: DropTarget) => {
    if (!dragging) return
    setDragging(null)
    if (dragging.kind === 'unassigned') {
      const node: SchemaLayoutNode = { kind: 'value', valueName: dragging.valueName }
      set(insertAt(layout, target.path, node))
      return
    }
    // Moving a node already in the tree: splice it out first, but adjust the target path if
    // the removal shifts indices ahead of the insertion site.
    const adjusted = adjustForRemoval(target.path, dragging.path)
    const without = removeAtPath(layout, dragging.path)
    const moved = nodeAtPath(layout, dragging.path)
    if (!moved) return
    set(insertAt(without, adjusted, moved))
  }

  return (
    <div className={s.root}>
      <div className={s.toolbar}>
        <Button appearance="primary" size="small" icon={<Add20Regular />} onClick={addSection}>
          Add section
        </Button>
      </div>

      <div className={s.tray}>
        <div className={s.trayHeader}>Unassigned values ({unassigned.length})</div>
        {unassigned.length === 0 && (
          <span style={{ color: tokens.colorNeutralForeground3 }}>
            Every value is placed in the layout.
          </span>
        )}
        {unassigned.map((v) => <UnassignedChip key={v.name} value={v} onDragStart={() => setDragging({ kind: 'unassigned', valueName: v.name })} onDragEnd={() => setDragging(null)} dragging={dragging?.kind === 'unassigned' && dragging.valueName === v.name} />)}
      </div>

      <div className={s.tree}>
        {layout.length === 0 && (
          <div className={s.emptyTree}>
            Drag values from above to place them, or add a section. Values that aren't placed
            here will still render first in submission forms (under no heading).
          </div>
        )}
        <DropZone
          path={[0]}
          active={isActiveDropZone(dragging)}
          onDrop={handleDrop}
        />
        {layout.map((node, i) => (
          <NodeRenderer
            key={i}
            node={node}
            path={[i]}
            onRemove={() => removeAt([i])}
            onUpdate={(patch) => updateSection([i], patch)}
            onAddSection={() => addSectionInside([i])}
            onDropAtChild={handleDrop}
            dragging={dragging}
            setDragging={setDragging}
            allValuesByName={new Map(schema.values.map(v => [v.name, v]))}
          />
        ))}
      </div>

      {issues.length > 0 && (
        <div className={s.issues}>
          {issues.map((iss, i) => (
            <div key={i} className={iss.severity === 'error' ? s.issueError : s.issueWarning}>
              {iss.severity === 'error' ? 'Error: ' : 'Note: '}{iss.message}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

// ── Dragging types ──────────────────────────────────────────────────────────────────────────

type DragPayload =
  | { kind: 'unassigned'; valueName: string }
  | { kind: 'node'; path: number[] }

interface DropTarget {
  /**
   * Insertion path. The last element is the position **before which** the new node should be
   * inserted in its parent's `items` list (or in the root list when the path has length 1).
   */
  path: number[]
}

// ── Subcomponents ───────────────────────────────────────────────────────────────────────────

function UnassignedChip({
  value,
  onDragStart,
  onDragEnd,
  dragging,
}: {
  value: SchemaValue
  onDragStart: () => void
  onDragEnd: () => void
  dragging: boolean
}) {
  const s = useStyles()
  return (
    <span
      className={`${s.chip}${dragging ? ` ${s.chipDragging}` : ''}`}
      draggable
      onDragStart={(e) => { e.dataTransfer.effectAllowed = 'move'; onDragStart() }}
      onDragEnd={onDragEnd}
      title={value.description ?? ''}
    >
      <DocumentBulletList20Regular />
      {value.label || value.name}
    </span>
  )
}

function NodeRenderer({
  node, path, onRemove, onUpdate, onAddSection, onDropAtChild, dragging, setDragging, allValuesByName,
}: {
  node: SchemaLayoutNode
  path: number[]
  onRemove: () => void
  onUpdate: (patch: Partial<Extract<SchemaLayoutNode, { kind: 'section' }>>) => void
  onAddSection: () => void
  onDropAtChild: (target: DropTarget) => void
  dragging: DragPayload | null
  setDragging: (d: DragPayload | null) => void
  allValuesByName: Map<string, SchemaValue>
}) {
  const s = useStyles()
  const isDraggingMe = dragging?.kind === 'node' && samePath(dragging.path, path)
  const onMyDragStart = () => setDragging({ kind: 'node', path })
  const onMyDragEnd = () => setDragging(null)

  if (node.kind === 'value') {
    const def = allValuesByName.get(node.valueName)
    return (
      <div
        className={s.node}
        style={isDraggingMe ? { opacity: 0.5 } : undefined}
        draggable
        onDragStart={(e) => { e.dataTransfer.effectAllowed = 'move'; onMyDragStart() }}
        onDragEnd={onMyDragEnd}
      >
        <div className={s.nodeHeader}>
          <ReOrderDotsVertical20Regular className={s.nodeHandle} />
          <DocumentBulletList20Regular className={s.nodeIcon} />
          <span className={s.nodeTitle}>{def?.label || node.valueName}</span>
          <Tooltip content="Remove from layout" relationship="label">
            <Button appearance="subtle" size="small" icon={<Delete16Regular />} onClick={onRemove} aria-label="Remove from layout" />
          </Tooltip>
        </div>
      </div>
    )
  }

  // section node
  const childrenPathBase = path
  return (
    <div
      className={s.node}
      style={isDraggingMe ? { opacity: 0.5 } : undefined}
      draggable
      onDragStart={(e) => { e.stopPropagation(); e.dataTransfer.effectAllowed = 'move'; onMyDragStart() }}
      onDragEnd={onMyDragEnd}
    >
      <div className={s.nodeHeader}>
        <ReOrderDotsVertical20Regular className={s.nodeHandle} />
        <Folder20Regular className={s.nodeIcon} />
        <span className={s.nodeTitle}>{node.caption || '(untitled section)'}</span>
        <Tooltip content="Add subsection inside" relationship="label">
          <Button appearance="subtle" size="small" icon={<Add20Regular />} onClick={onAddSection} aria-label="Add subsection" />
        </Tooltip>
        <Tooltip content="Remove section" relationship="label">
          <Button appearance="subtle" size="small" icon={<Delete16Regular />} onClick={onRemove} aria-label="Remove section" />
        </Tooltip>
      </div>
      <div className={s.sectionInputs}>
        <Input
          aria-label="Section caption"
          placeholder="Section heading"
          value={node.caption}
          onChange={(_, v) => onUpdate({ caption: v.value })}
        />
        <Textarea
          aria-label="Section description"
          placeholder="Optional sub-heading (rendered under the caption)"
          rows={2}
          value={node.description ?? ''}
          onChange={(_, v) => onUpdate({ description: v.value || null })}
        />
      </div>

      <div className={s.nodeChildren}>
        <DropZone
          path={[...childrenPathBase, 0]}
          active={isActiveDropZone(dragging)}
          onDrop={onDropAtChild}
        />
        {(node.items ?? []).map((child, i) => (
          <Wrapped
            key={i}
            node={child}
            path={[...childrenPathBase, i]}
            onDropAfter={onDropAtChild}
            onRemove={() => onUpdate({ items: (node.items ?? []).filter((_, j) => j !== i) })}
            onUpdateChild={(patch) => onUpdate({
              items: (node.items ?? []).map((c, j) => j === i
                ? (c.kind === 'section' ? { ...c, ...patch } : c)
                : c),
            })}
            onAddSectionInsideChild={() => onUpdate({
              items: (node.items ?? []).map((c, j) => j === i && c.kind === 'section'
                ? { ...c, items: [...(c.items ?? []), { kind: 'section', caption: 'New section', items: [] }] }
                : c),
            })}
            onDropAtChild={onDropAtChild}
            dragging={dragging}
            setDragging={setDragging}
            allValuesByName={allValuesByName}
            dropAfterPath={[...childrenPathBase, i + 1]}
          />
        ))}
      </div>
    </div>
  )
}

function Wrapped(props: {
  node: SchemaLayoutNode
  path: number[]
  onDropAfter: (t: DropTarget) => void
  onRemove: () => void
  onUpdateChild: (patch: Partial<Extract<SchemaLayoutNode, { kind: 'section' }>>) => void
  onAddSectionInsideChild: () => void
  onDropAtChild: (t: DropTarget) => void
  dragging: DragPayload | null
  setDragging: (d: DragPayload | null) => void
  allValuesByName: Map<string, SchemaValue>
  dropAfterPath: number[]
}) {
  return (
    <>
      <NodeRenderer
        node={props.node}
        path={props.path}
        onRemove={props.onRemove}
        onUpdate={props.onUpdateChild}
        onAddSection={props.onAddSectionInsideChild}
        onDropAtChild={props.onDropAtChild}
        dragging={props.dragging}
        setDragging={props.setDragging}
        allValuesByName={props.allValuesByName}
      />
      <DropZone
        path={props.dropAfterPath}
        active={isActiveDropZone(props.dragging)}
        onDrop={props.onDropAfter}
      />
    </>
  )
}

function DropZone({
  path, active, onDrop,
}: {
  path: number[]
  active: boolean
  onDrop: (target: DropTarget) => void
}) {
  const s = useStyles()
  const [over, setOver] = useState(false)
  return (
    <div
      className={`${s.dropZone}${over || active ? ` ${s.dropZoneActive}` : ''}`}
      onDragOver={(e) => { e.preventDefault(); e.dataTransfer.dropEffect = 'move'; setOver(true) }}
      onDragLeave={() => setOver(false)}
      onDrop={(e) => { e.preventDefault(); setOver(false); onDrop({ path }) }}
    />
  )
}

// ── Pure helpers ────────────────────────────────────────────────────────────────────────────

function isActiveDropZone(dragging: DragPayload | null): boolean {
  // Reserved for future "preview where the drop will land" hint. We just rely on the per-zone
  // hover state for now. (A target path will be threaded back in when that lands.)
  return !!dragging && false
}

function samePath(a: number[], b: number[]): boolean {
  if (a.length !== b.length) return false
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false
  return true
}

function nodeAtPath(layout: SchemaLayoutNode[], path: number[]): SchemaLayoutNode | undefined {
  if (path.length === 0) return undefined
  let arr = layout
  for (let i = 0; i < path.length - 1; i++) {
    const n = arr[path[i]]
    if (!n || n.kind !== 'section') return undefined
    arr = n.items ?? []
  }
  return arr[path[path.length - 1]]
}

function insertAt(layout: SchemaLayoutNode[], path: number[], node: SchemaLayoutNode): SchemaLayoutNode[] {
  if (path.length === 1) {
    const copy = layout.slice()
    copy.splice(path[0], 0, node)
    return copy
  }
  return layout.map((n, idx) => {
    if (idx !== path[0]) return n
    if (n.kind !== 'section') return n
    return { ...n, items: insertAt(n.items ?? [], path.slice(1), node) }
  })
}

function removeAtPath(layout: SchemaLayoutNode[], path: number[]): SchemaLayoutNode[] {
  if (path.length === 1) return layout.filter((_, i) => i !== path[0])
  return layout.map((n, idx) => {
    if (idx !== path[0]) return n
    if (n.kind !== 'section') return n
    return { ...n, items: removeAtPath(n.items ?? [], path.slice(1)) }
  })
}

function mapAtPath(
  layout: SchemaLayoutNode[],
  path: number[],
  fn: (n: SchemaLayoutNode) => SchemaLayoutNode,
): SchemaLayoutNode[] {
  if (path.length === 0) return layout
  return layout.map((n, idx) => {
    if (idx !== path[0]) return n
    if (path.length === 1) return fn(n)
    if (n.kind !== 'section') return n
    return { ...n, items: mapAtPath(n.items ?? [], path.slice(1), fn) }
  })
}

function indexOfLastChild(layout: SchemaLayoutNode[], path: number[]): number {
  const n = nodeAtPath(layout, path)
  if (n && n.kind === 'section') return (n.items?.length ?? 1) - 1
  return -1
}

/**
 * When dragging a node already in the tree, removing it from its original position can shift
 * the indices of the target insertion path. Bump the relevant component down by one if the
 * removal happens earlier in the same parent.
 */
function adjustForRemoval(target: number[], removed: number[]): number[] {
  if (removed.length > target.length) return target.slice()
  // Same parent up to removed.length - 1.
  for (let i = 0; i < removed.length - 1; i++) {
    if (removed[i] !== target[i]) return target.slice()
  }
  const out = target.slice()
  if (removed[removed.length - 1] < (target[removed.length - 1] ?? Infinity)) {
    out[removed.length - 1] = (target[removed.length - 1] ?? 0) - 1
  }
  return out
}
