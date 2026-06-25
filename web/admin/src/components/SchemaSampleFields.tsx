import { useState } from 'react'
import {
  Badge, Button, Dropdown, Field, Input, Option, Textarea, makeStyles, tokens,
} from '@fluentui/react-components'
import { Add20Regular } from '@fluentui/react-icons'
import type { Schema, SchemaValue, SchemaValueType } from '../api/types'
import { ValueLabel } from './ValueLabel'
import { cadenceLabel } from '../utils/cadence'
import { walkLayout } from '../utils/layout'
import type { RowState, ValueRow } from '../utils/sampleRules'

const useStyles = makeStyles({
  valueRow: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 1.6fr) minmax(0, 1.4fr) minmax(0, 1fr)',
    gap: '12px',
    alignItems: 'flex-start',
    padding: '12px 0',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  valueRowFirst: { borderTop: 'none' },
  // Section headings rendered as `<h2>` / `<h3>` per nesting depth. Tone is a bit lighter than
  // the per-value Caption so the hierarchy "Section > Caption > Field" reads top to bottom.
  sectionHeading: {
    margin: '20px 0 0',
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  subsectionHeading: {
    margin: '14px 0 0',
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  sectionDescription: {
    margin: '2px 0 6px',
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  // Schema-author-provided heading rendered above a value's row (think <h2>). Display-only.
  valueCaption: {
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase500,
    color: tokens.colorNeutralForeground1,
    margin: '16px 0 4px',
  },
  valueLabel: { display: 'flex', flexDirection: 'column', gap: '4px' },
  valueLabelTitle: { fontWeight: 600 },
  valueLabelMeta: { color: tokens.colorNeutralForeground3, fontSize: '12px' },
  badges: { display: 'flex', gap: '6px', flexWrap: 'wrap', marginTop: '4px' },
  // When the textarea is hidden, the "Add notes" button sits in the same column the textarea
  // would occupy. The top padding lines it up with the value input (i.e. below the field labels).
  notesPlaceholder: { paddingTop: '24px' },
  // Live warning text shown under the value label when a per-value Warning rule fires.
  warningInline: {
    color: tokens.colorPaletteDarkOrangeForeground1,
    fontSize: '12px',
    fontStyle: 'italic',
  },
})

/**
 * Controlled renderer for the per-value inputs of a submission/preview form. Walks the schema's
 * layout (folding away sections whose every descendant is hidden by VisibleIf), renders one row
 * per value with its captions/units/badges, and reflects the precomputed `rowStates` (hidden →
 * skipped, disabled → greyed, warning → inline note). State (the typed values and gating) is
 * owned by the caller and threaded in via `rows` / `rowStates` / `onPatchRow`, so the submission
 * editor and the schema-editor preview can share the exact same look and behaviour.
 */
export function SchemaSampleFields({
  schema, rows, rowStates, ruleVariables, readOnly = false, onPatchRow,
}: {
  schema: Schema
  rows: ValueRow[]
  rowStates: RowState[]
  ruleVariables?: Record<string, unknown>
  readOnly?: boolean
  onPatchRow: (name: string, patch: Partial<ValueRow>) => void
}) {
  const s = useStyles()

  // Index rows/states by value name so the layout walker can look them up.
  const rowsByName = new Map(rows.map(r => [r.name, r] as const))
  const statesByName = new Map(rowStates.map(st => [st.name, st] as const))

  // walkLayout drives the order + grouping. The predicate hides values whose VisibleIf rule
  // evaluates falsy, and the walker folds away sections whose every descendant is hidden — no
  // "Optional notes" heading sitting above nothing.
  const items = walkLayout(schema, {
    isValueVisible: (name) => !(statesByName.get(name)?.hidden ?? false),
  })

  // Track the first visible value globally so its top border is suppressed; a fresh section
  // start also suppresses the border on the first child for the same reason.
  let visibleSoFar = 0
  let suppressNextBorder = false

  return (
    <>
      {items.map((item, idx) => {
        if (item.kind === 'section-end') return null
        if (item.kind === 'section-start') {
          suppressNextBorder = true
          const HeadingTag = item.depth === 0 ? 'h2' : 'h3'
          const headingClass = item.depth === 0 ? s.sectionHeading : s.subsectionHeading
          return (
            <div key={`section-${idx}`}>
              <HeadingTag className={headingClass} style={{ paddingLeft: `${item.depth * 8}px` }}>
                {item.caption}
              </HeadingTag>
              {item.description && (
                <p className={s.sectionDescription} style={{ paddingLeft: `${item.depth * 8}px` }}>
                  {item.description}
                </p>
              )}
            </div>
          )
        }
        const row = rowsByName.get(item.value.name)
        const state = statesByName.get(item.value.name)
        if (!row || !state) return null
        const caption = item.value.caption?.trim() || ''
        const isFirstVisible = visibleSoFar === 0
        const borderless = isFirstVisible || !!caption || suppressNextBorder
        suppressNextBorder = false
        visibleSoFar++
        return (
          <div key={item.value.name} style={item.depth > 0 ? { paddingLeft: `${item.depth * 8}px` } : undefined}>
            {caption && <h2 className={s.valueCaption}>{caption}</h2>}
            <SchemaValueRow
              row={row}
              first={borderless}
              schemaEnabled={schema.enabled}
              schema={schema}
              state={state}
              ruleVariables={ruleVariables}
              readOnly={readOnly}
              onChange={patch => onPatchRow(row.name, patch)}
            />
          </div>
        )
      })}
    </>
  )
}

function SchemaValueRow({
  row, first, schemaEnabled, schema, state, ruleVariables, readOnly, onChange,
}: {
  row: ValueRow
  first: boolean
  schemaEnabled: boolean
  schema: Schema
  state: RowState
  ruleVariables?: Record<string, unknown>
  /** View mode: every input disabled, "Add notes" button suppressed when there's no existing note. */
  readOnly?: boolean
  onChange: (patch: Partial<ValueRow>) => void
}) {
  const s = useStyles()
  const def = row.def
  const calculated = def.kind === 'Calculated'
  // The row is "inert" (not editable) when the page is read-only, the schema/value is disabled,
  // an EnabledIf rule says so, or the value is calculated.
  const inert = !!readOnly || !schemaEnabled || !def.enabled || state.disabled || calculated
  // Keep the row compact by default. The textarea pops out either when the user opts in,
  // or when the row already carries a note (e.g. while editing an existing submission).
  const [notesOpened, setNotesOpened] = useState(false)
  const showNotes = notesOpened || !!row.note

  return (
    <div className={first ? `${s.valueRow} ${s.valueRowFirst}` : s.valueRow}>
      <div className={s.valueLabel}>
        <span className={s.valueLabelTitle}>
          <ValueLabel value={def} schema={schema} descriptionMode="none" showRequired />
        </span>
        {def.description && <span className={s.valueLabelMeta}>{def.description}</span>}
        <div className={s.badges}>
          {/* Same visual treatment as the cadence badge — both are neutral metadata pills. */}
          <Badge appearance="outline" color="informative" size="small">{friendlyTypeLabel(def.type)}</Badge>
          <Badge appearance="outline" color="informative" size="small">{cadenceLabel(def.cadence)}</Badge>
          {def.required && !calculated && <Badge appearance="outline" color="severe" size="small">required</Badge>}
          {!def.required && !calculated && <Badge appearance="outline" color="subtle" size="small">optional</Badge>}
          {calculated && <Badge appearance="outline" color="informative" size="small">calculated</Badge>}
          {inert && <Badge appearance="outline" color="subtle" size="small">disabled</Badge>}
        </div>
        <span className={s.valueLabelMeta}>{valueHint(def)}</span>
        {state.warning && (
          <span className={s.warningInline}>{state.warning}</span>
        )}
      </div>

      <Field label={`Value${def.unit ? ` (${def.unit})` : ''}`}>
        <SampleValueInput
          valueDef={def}
          value={row.value}
          displayValue={calculated ? (state.disabled ? null : ruleVariables?.[row.name]) : undefined}
          onChange={v => onChange({ value: v })}
          disabled={inert}
        />
      </Field>

      {!calculated && (showNotes ? (
        <Field label="Note">
          <Textarea
            value={row.note}
            onChange={(_, v) => onChange({ note: v.value })}
            disabled={inert}
            rows={2}
          />
        </Field>
      ) : readOnly ? (
        // No note on this sample and we're in view mode — leave the column blank rather than
        // showing a perpetually-disabled "Add notes" button.
        <div />
      ) : (
        <div className={s.notesPlaceholder}>
          <Button
            appearance="transparent"
            icon={<Add20Regular />}
            disabled={inert}
            onClick={() => setNotesOpened(true)}
          >
            Add notes
          </Button>
        </div>
      ))}
    </div>
  )
}

function SampleValueInput({
  valueDef, value, displayValue, onChange, disabled,
}: {
  valueDef: SchemaValue
  value: unknown
  /** When set (calculated values), show this read-only formatted value instead of the editable input. */
  displayValue?: unknown
  onChange: (v: unknown) => void
  disabled?: boolean
}) {
  if (displayValue !== undefined) {
    return (
      <Input
        disabled
        readOnly
        value={formatDisplayValue(displayValue, valueDef)}
      />
    )
  }
  switch (valueDef.type as SchemaValueType) {
    case 'Boolean':
      // Tri-state: "(empty)" means "not provided" — distinct from explicit false. We surface
      // the choices as "Yes" / "No" to operators because non-technical users don't think in
      // booleans; the underlying option values stay 'true'/'false' so the wire shape doesn't
      // change.
      return (
        <Dropdown
          disabled={disabled}
          selectedOptions={value === true ? ['true'] : value === false ? ['false'] : ['']}
          value={value === true ? 'Yes' : value === false ? 'No' : ''}
          onOptionSelect={(_, d) => {
            const v = d.optionValue
            onChange(v === 'true' ? true : v === 'false' ? false : null)
          }}
        >
          <Option value="">(not provided)</Option>
          <Option value="true">Yes</Option>
          <Option value="false">No</Option>
        </Dropdown>
      )
    case 'Integer':
      return (
        <Input
          disabled={disabled}
          type="number"
          value={value == null ? '' : String(value)}
          onChange={(_, v) => onChange(v.value === '' ? null : Math.trunc(Number(v.value)))}
        />
      )
    case 'Number':
      return (
        <Input
          disabled={disabled}
          type="number"
          value={value == null ? '' : String(value)}
          onChange={(_, v) => onChange(v.value === '' ? null : Number(v.value))}
        />
      )
    case 'Date':
      return (
        <Input
          disabled={disabled}
          type="datetime-local"
          value={typeof value === 'string' ? toLocalInput(value) : ''}
          onChange={(_, v) => onChange(v.value ? fromLocalInput(v.value) : null)}
        />
      )
    case 'String':
    default: {
      // Promote to a multi-line Textarea when the schema author hints that the value can be
      // long (MaxLength > 40 characters). Single-line Input stays the default — most string
      // values are short identifiers, codes, or names that don't benefit from the extra space.
      const multiline = valueDef.maxLength != null && valueDef.maxLength > 40
      const common = {
        disabled,
        value: value == null ? '' : String(value),
        onChange: (_: unknown, v: { value: string }) =>
          onChange(v.value === '' ? null : v.value),
      }
      return multiline
        ? <Textarea {...common} rows={3} />
        : <Input {...common} />
    }
  }
}

function formatDisplayValue(value: unknown, def: SchemaValue): string {
  if (value === null || value === undefined) return ''
  switch (def.type) {
    case 'Boolean': return value === true ? 'Yes' : value === false ? 'No' : ''
    case 'Date': return typeof value === 'string' ? value : String(value)
    default: return String(value)
  }
}

/**
 * Map a wire-level `SchemaValueType` to the friendlier wording shown to operators in the
 * submission editor. The schema editor still shows the raw type — that audience cares about
 * the precise wire shape, this audience doesn't.
 */
function friendlyTypeLabel(type: SchemaValueType): string {
  switch (type) {
    case 'String':  return 'Text'
    case 'Integer': return 'Whole number'
    case 'Number':  return 'Number'
    case 'Date':    return 'Date'
    case 'Boolean': return 'Yes/No'
  }
}

function valueHint(v: SchemaValue): string {
  const bits: string[] = []
  if (v.type === 'Number' || v.type === 'Integer') {
    if (v.min != null) bits.push(`min ${v.min}`)
    if (v.max != null) bits.push(`max ${v.max}`)
  }
  if (v.type === 'String') {
    if (v.minLength != null) bits.push(`min length ${v.minLength}`)
    if (v.maxLength != null) bits.push(`max length ${v.maxLength}`)
    if (v.regexPattern) bits.push(`regex: ${v.regexPattern}`)
  }
  if (v.type === 'Date') {
    if (v.minDate) bits.push(`from ${v.minDate}`)
    if (v.maxDate) bits.push(`to ${v.maxDate}`)
  }
  return bits.join(' · ')
}

// <input type="datetime-local"> wants 'YYYY-MM-DDTHH:mm' in local time; we round-trip via UTC ISO.
export function toLocalInput(iso: string): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

export function fromLocalInput(local: string): string {
  if (!local) return ''
  const d = new Date(local)
  return d.toISOString()
}
