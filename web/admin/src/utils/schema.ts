import type { Schema, SchemaValue, UpsertSchemaRequest } from '../api/types'

/**
 * C-style identifier rule mirrored on the server (`SchemaService.ValidateStructure`). A schema
 * value name must work as a plain NCalc identifier (so it can be referenced directly in rules)
 * and as a C# / JavaScript identifier (so it shows up cleanly across the stack). It also has to
 * stay out of the `<name>.minimum` / `<name>.maximum` bound namespace, which means no `.` etc.
 */
export const VALUE_NAME_RE = /^[A-Za-z_][A-Za-z0-9_]*$/

export function isValidValueName(name: string | null | undefined): boolean {
  return !!name && VALUE_NAME_RE.test(name)
}

export function emptySchema(): UpsertSchemaRequest {
  return {
    name: '',
    label: '',
    description: '',
    notes: '',
    modifiable: true,
    enabled: true,
    submissionValidations: [],
    isGlobal: true,
    serviceIds: [],
    values: [],
    layout: [],
    version: 1,
  }
}

export function emptyValue(): SchemaValue {
  return {
    name: '',
    label: '',
    description: '',
    notes: '',
    caption: '',
    type: 'Number',
    unit: '',
    cadence: 'Weekly',
    required: true,
    modifiable: true,
    enabled: true,
    min: null,
    max: null,
    minDate: null,
    maxDate: null,
    minLength: null,
    maxLength: null,
    regexPattern: '',
    valueValidation: '',
    enabledIf: '',
    visibleIf: '',
    warning: '',
    sinceVersion: null,
  }
}

/**
 * Convert a loaded `Schema` into the upsert payload the server accepts. Audit / server-managed
 * fields aren't part of the upsert contract; strip them so the server doesn't reject the body
 * (and so the version-timestamp logic stays server-side).
 */
export function toRequest(s: Schema): UpsertSchemaRequest {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars -- destructured solely to drop the audit/server-managed fields from `rest`.
  const { id, createdAt, createdBy, modifiedAt, modifiedBy, versionModifiedAt, ...rest } = s
  return rest
}
