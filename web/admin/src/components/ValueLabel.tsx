import { Badge, Tooltip, tokens } from '@fluentui/react-components'
import { Info16Regular } from '@fluentui/react-icons'
import type { Schema, SchemaValue } from '../api/types'
import { isWithinOneCadenceOf } from '../utils/cadence'

/**
 * Render a schema-value label decorated with two small affordances:
 *
 *  - An "(i)" info icon when the value has a non-empty `description`. Hovering shows the full
 *    description in a Fluent tooltip. Used in places where the description isn't already on
 *    screen (submission editor row, submission view drawer, read-only schema view drawer).
 *  - A "New" badge when the value was introduced in the current schema version *and* we are
 *    still inside one cadence period of the version bump (so it disappears after the first
 *    submission window per value, instead of lingering forever).
 *
 * Both decorations are off by default if the data doesn't satisfy their conditions, so the
 * component is safe to drop in wherever a value's label is rendered.
 */
export function ValueLabel({
  value,
  schema,
  fallback,
  descriptionMode = 'icon',
  showRequired = false,
}: {
  value: SchemaValue
  /** Parent schema — needed for the version-bump anchor and the cadence per value. */
  schema: Pick<Schema, 'version' | 'versionModifiedAt'>
  /** Optional override for the displayed text (defaults to `label ?? name`). */
  fallback?: string
  /**
   * How to surface the value's `description`:
   *
   *  - `'icon'` (default): show a small "(i)" icon with the description as its tooltip.
   *    Used where the description isn't already on screen (submission view drawer,
   *    read-only schema view).
   *  - `'none'`: don't render the description here. Used in the submission editor where the
   *    description is rendered separately under the value label.
   */
  descriptionMode?: 'icon' | 'none'
  /**
   * When true and the value is `required`, render a red asterisk immediately after the label
   * (HTML-form convention). Off by default — only the submission editor turns it on, since
   * "required" is meaningless in read-only views and in the schema editor's value list.
   */
  showRequired?: boolean
}) {
  const text = (fallback ?? value.label ?? value.name).toString()
  const description = value.description?.trim() || ''
  const sinceVersion = value.sinceVersion ?? 1
  const showNew =
    schema.version > 1 &&
    sinceVersion === schema.version &&
    isWithinOneCadenceOf(schema.versionModifiedAt ?? null, value.cadence)

  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', minWidth: 0 }}>
      <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>
        {text}
        {showRequired && value.required && (
          <span
            aria-label="required"
            style={{ color: tokens.colorPaletteRedForeground1, marginLeft: '2px' }}
          >
            *
          </span>
        )}
      </span>
      {descriptionMode === 'icon' && description && (
        <Tooltip content={description} relationship="description" withArrow>
          <Info16Regular
            aria-label="Description"
            style={{ color: tokens.colorNeutralForeground3, cursor: 'help', flexShrink: 0 }}
          />
        </Tooltip>
      )}
      {showNew && (
        <Badge appearance="filled" color="brand" size="small" style={{ flexShrink: 0 }}>New</Badge>
      )}
    </span>
  )
}
