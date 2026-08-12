import { useState } from 'react'
import {
  Badge, Button, Dropdown, Field, Input, Option, Textarea, makeStyles, tokens,
} from '@fluentui/react-components'
import { Add20Regular } from '@fluentui/react-icons'
import type { Schema, SchemaValue, SchemaValueType } from '../api/types'
import { ValueLabel } from './ValueLabel'
import { cadenceLabel } from '../utils/cadence'
import { walkLayout, type RenderItem } from '../utils/layout'
import { fromLocalInput, toLocalInput } from '../utils/datetimeLocal'
import type { RowState, ValueRow } from '../utils/sampleRules'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'

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

  // Decide up-front which value rows should hide their top border (the first visible value, the
  // first child after a section start, or any row carrying its own caption). Computed in a single
  // pass outside the render closure so we don't reassign render-scope variables while mapping.
  const borderlessByIndex = computeBorderless(items, rowsByName, statesByName)

  return (
    <>
      {items.map((item, idx) => {
        if (item.kind === 'section-end') return null
        if (item.kind === 'section-start') {
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
        return (
          <div key={item.value.name} style={item.depth > 0 ? { paddingLeft: `${item.depth * 8}px` } : undefined}>
            {caption && <h2 className={s.valueCaption}>{caption}</h2>}
            <SchemaValueRow
              row={row}
              first={borderlessByIndex.get(idx) ?? false}
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

/**
 * One pass over the laid-out items deciding which value rows hide their top border, keyed by the
 * item's index. A row is borderless when it's the first visible value, the first child right after a
 * section start, or it carries its own caption (the caption already provides visual separation).
 */
function computeBorderless(
  items: RenderItem[],
  rowsByName: Map<string, ValueRow>,
  statesByName: Map<string, RowState>,
): Map<number, boolean> {
  const out = new Map<number, boolean>()
  let visibleSoFar = 0
  let suppressNextBorder = false
  items.forEach((item, idx) => {
    if (item.kind === 'section-end') return
    if (item.kind === 'section-start') { suppressNextBorder = true; return }
    const row = rowsByName.get(item.value.name)
    const state = statesByName.get(item.value.name)
    if (!row || !state) return
    const caption = item.value.caption?.trim() || ''
    out.set(idx, visibleSoFar === 0 || !!caption || suppressNextBorder)
    suppressNextBorder = false
    visibleSoFar++
  })
  return out
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
  const { t } = useTranslation()
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
          <Badge appearance="outline" color="informative" size="small">{friendlyTypeLabel(def.type, t)}</Badge>
          <Badge appearance="outline" color="informative" size="small">{cadenceLabel(def.cadence, t)}</Badge>
          {def.required && !calculated && <Badge appearance="outline" color="severe" size="small">{t('schemasSubmissions.sampleFields.required')}</Badge>}
          {!def.required && !calculated && <Badge appearance="outline" color="subtle" size="small">{t('schemasSubmissions.sampleFields.optional')}</Badge>}
          {calculated && <Badge appearance="outline" color="informative" size="small">{t('schemasSubmissions.sampleFields.calculated')}</Badge>}
          {inert && <Badge appearance="outline" color="subtle" size="small">{t('schemasSubmissions.sampleFields.disabled')}</Badge>}
        </div>
        <span className={s.valueLabelMeta}>{valueHint(def, t)}</span>
        {state.warning && (
          <span className={s.warningInline}>{state.warning}</span>
        )}
      </div>

      <Field label={t('schemasSubmissions.sampleFields.value', { unit: def.unit ? ` (${def.unit})` : '' })}>
        <SampleValueInput
          valueDef={def}
          value={row.value}
          displayValue={calculated ? (state.disabled ? null : ruleVariables?.[row.name]) : undefined}
          onChange={v => onChange({ value: v })}
          disabled={inert}
        />
      </Field>

      {!calculated && (showNotes ? (
        <Field label={t('schemasSubmissions.sampleFields.note')}>
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
            {t('schemasSubmissions.sampleFields.addNotes')}
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
  const { t } = useTranslation()
  if (displayValue !== undefined) {
    return (
      <Input
        disabled
        readOnly
        value={formatDisplayValue(displayValue, valueDef, t)}
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
          value={value === true ? t('schemasSubmissions.common.yes') : value === false ? t('schemasSubmissions.common.no') : ''}
          onOptionSelect={(_, d) => {
            const v = d.optionValue
            onChange(v === 'true' ? true : v === 'false' ? false : null)
          }}
        >
          <Option value="">{t('schemasSubmissions.sampleFields.notProvided')}</Option>
          <Option value="true">{t('schemasSubmissions.common.yes')}</Option>
          <Option value="false">{t('schemasSubmissions.common.no')}</Option>
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

function formatDisplayValue(value: unknown, def: SchemaValue, t: TFunction): string {
  if (value === null || value === undefined) return ''
  switch (def.type) {
    case 'Boolean': return value === true ? t('schemasSubmissions.common.yes') : value === false ? t('schemasSubmissions.common.no') : ''
    case 'Date': return typeof value === 'string' ? value : String(value)
    default: return String(value)
  }
}

/**
 * Map a wire-level `SchemaValueType` to the friendlier wording shown to operators in the
 * submission editor. The schema editor still shows the raw type — that audience cares about
 * the precise wire shape, this audience doesn't.
 */
function friendlyTypeLabel(type: SchemaValueType, t: TFunction): string {
  switch (type) {
    case 'String':  return t('schemasSubmissions.common.valueType.String')
    case 'Integer': return t('schemasSubmissions.common.valueType.Integer')
    case 'Number':  return t('schemasSubmissions.common.valueType.Number')
    case 'Date':    return t('schemasSubmissions.common.valueType.Date')
    case 'Boolean': return t('schemasSubmissions.common.valueType.Boolean')
  }
}

function valueHint(v: SchemaValue, t: TFunction): string {
  const bits: string[] = []
  if (v.type === 'Number' || v.type === 'Integer') {
    if (v.min != null) bits.push(t('schemasSubmissions.sampleFields.min', { value: v.min }))
    if (v.max != null) bits.push(t('schemasSubmissions.sampleFields.max', { value: v.max }))
  }
  if (v.type === 'String') {
    if (v.minLength != null) bits.push(t('schemasSubmissions.sampleFields.minLength', { value: v.minLength }))
    if (v.maxLength != null) bits.push(t('schemasSubmissions.sampleFields.maxLength', { value: v.maxLength }))
    if (v.regexPattern) bits.push(t('schemasSubmissions.sampleFields.regex', { value: v.regexPattern }))
  }
  if (v.type === 'Date') {
    if (v.minDate) bits.push(t('schemasSubmissions.sampleFields.from', { value: v.minDate }))
    if (v.maxDate) bits.push(t('schemasSubmissions.sampleFields.to', { value: v.maxDate }))
  }
  return bits.join(' · ')
}
