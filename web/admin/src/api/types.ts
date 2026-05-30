/**
 * 'User' = interactive account: can log in to the admin UI and call APIs.
 * 'Application' = automated credential: API-only, blocked from the UI sign-in path.
 * The role (Service/Operator/Admin) determines what the account can DO and is orthogonal to the kind.
 */
export type AccountKind = 'User' | 'Application'
export type AccountRole = 'Service' | 'Operator' | 'Admin'
export type SchemaValueType = 'String' | 'Integer' | 'Number' | 'Date' | 'Boolean'
export type Cadence =
  | 'Daily'
  | 'Weekly'
  | 'Fortnightly'
  | 'Monthly'
  | 'Quarterly'
  | 'SemiAnnually'
  | 'Yearly'

export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface Account {
  id: string
  name: string
  label?: string | null
  description?: string | null
  kind: AccountKind
  role: AccountRole
  enabled: boolean
  createdAt: string
  createdBy?: string | null
  modifiedAt: string
  modifiedBy?: string | null
  isDeleted: boolean
}

export interface CreateAccountRequest {
  name: string
  label?: string | null
  description?: string | null
  kind: AccountKind
  role: AccountRole
  enabled?: boolean
}

export interface UpdateAccountRequest {
  label?: string | null
  description?: string | null
  role: AccountRole
  enabled: boolean
}

export interface ApiKey {
  id: string
  accountId: string
  keyId: string
  createdAt: string
  expiresAt?: string | null
  revokedAt?: string | null
}

export interface GeneratedApiKey {
  key: ApiKey
  plaintext: string
}

export interface SchemaValue {
  name: string
  label?: string | null
  description?: string | null
  notes?: string | null
  /**
   * Optional UI-only heading rendered above this value in the submission editor and the
   * read-only submission view (think <h2>). Plays no role server-side or in validation.
   */
  caption?: string | null
  type: SchemaValueType
  unit?: string | null
  cadence: Cadence
  required: boolean
  modifiable: boolean
  enabled: boolean
  min?: number | null
  max?: number | null
  minDate?: string | null
  maxDate?: string | null
  minLength?: number | null
  maxLength?: number | null
  regexPattern?: string | null
  valueValidation?: string | null
  /** Expression deciding whether the value is "enabled" in context. Falsy = discard + warning. */
  enabledIf?: string | null
  /** Expression deciding whether the value is "visible" in context. Falsy = discard + warning. */
  visibleIf?: string | null
  /** Expression that, when truthy/non-empty, surfaces a non-blocking warning on submission. */
  warning?: string | null
  /**
   * Optional schema version in which this value was first introduced. Null is treated as 1
   * ("always present"). The SPA uses it together with the parent schema's `version` and
   * `versionModifiedAt` to render a time-limited "New" badge next to the value's label.
   */
  sinceVersion?: number | null
}

/** One node in a schema's UI-only layout tree. Discriminated by `kind`. */
export type SchemaLayoutNode =
  | { kind: 'value'; valueName: string }
  | {
      kind: 'section'
      /** Section heading, shown above the children. Required + non-empty for section nodes. */
      caption: string
      /** Optional sub-heading, rendered as a small paragraph under the caption. */
      description?: string | null
      /** Children: ordered list of values and/or nested subsections. */
      items: SchemaLayoutNode[]
    }

export interface Schema {
  id: string
  name: string
  label?: string | null
  description?: string | null
  notes?: string | null
  modifiable: boolean
  enabled: boolean
  submissionValidations: string[]
  isGlobal: boolean
  serviceIds: string[]
  values: SchemaValue[]
  /**
   * Optional UI-only layout tree grouping `values` into sections and nested subsections.
   * Submissions always travel as a flat list — the server ignores this field for submission
   * acceptance and only validates referential integrity (every value-ref resolves, no
   * duplicates, sections have a caption).
   */
  layout?: SchemaLayoutNode[] | null
  /** Schema version. Defaults to 1. Monotonic across updates (server rejects downgrades). */
  version: number
  /**
   * ISO-8601 timestamp of the last `version` change. Server-managed; null on legacy documents
   * that have never had their version bumped. Anchors the time window in which the SPA renders
   * the "New" tag (one cadence period of each value).
   */
  versionModifiedAt?: string | null
  createdAt: string
  createdBy?: string | null
  modifiedAt: string
  modifiedBy?: string | null
}

export type UpsertSchemaRequest = Omit<Schema, 'id' | 'createdAt' | 'modifiedAt' | 'versionModifiedAt'>

export interface Sample {
  schemaName: string
  valueName: string
  value: unknown
  timestamp: string
  note?: string | null
}

export interface Submission {
  id: string
  serviceAccountId: string
  serviceName?: string | null
  samples: Sample[]
  submittedAt: string
  replacedAt?: string | null
  createdAt: string
  createdBy?: string | null
  modifiedAt: string
  modifiedBy?: string | null
  isDeleted: boolean
}

/** Body shape for admin-on-behalf-of submission create/replace. */
export interface AdminSubmissionInput {
  serviceAccountId: string
  samples: SampleInput[]
}

/** Response returned by every submission create/replace endpoint. */
export interface SubmissionWriteResponse {
  id: string
  /** Non-blocking warnings (fired Warning rules, EnabledIf/VisibleIf discards). Always present; empty when none. */
  warnings: string[]
}

export interface SampleInput {
  schemaName: string
  valueName: string
  /** JSON-typed value matching the value's declared SchemaValueType (string, number, boolean, ISO date string, ...). */
  value: unknown
  timestamp: string
  note?: string | null
}

export interface SchemaValueStatus {
  valueName: string
  label?: string | null
  cadence: Cadence
  required: boolean
  enabled: boolean
  periodStart: string
  periodEnd: string
  lastSubmissionId?: string | null
  lastTimestamp?: string | null
  satisfied: boolean
}

export interface SchemaStatus {
  schemaName: string
  label?: string | null
  enabled: boolean
  values: SchemaValueStatus[]
}

export interface ServiceStatus {
  serviceId: string
  serviceName: string
  period: string
  schemas: SchemaStatus[]
}

/**
 * One row of the "missing submissions" dashboard report — a service that hasn't yet submitted
 * every required value of a given cadence for one of its schemas inside the current window.
 */
export interface MissingSubmissionEntry {
  serviceId: string
  serviceName: string
  serviceLabel?: string | null
  schemaName: string
  schemaLabel?: string | null
  /** Required-and-enabled values of this cadence the service still owes for the current window. */
  missingRequiredCount: number
  /** Denominator: total required-and-enabled values of this cadence on the schema. */
  totalRequiredCount: number
}

/** A per-cadence bucket of the missing-submissions report. The server omits empty buckets. */
export interface MissingByCadence {
  cadence: Cadence
  periodStart: string
  periodEnd: string
  entries: MissingSubmissionEntry[]
}

export interface Me {
  id: string
  name: string
  label?: string | null
  role: AccountRole
  kind: AccountKind
}

export interface HistoryBucket {
  periodStart: string
  periodEnd: string
  min: number
  max: number
  average: number
  count: number
}

export interface SchemaValueHistory {
  valueName: string
  label?: string | null
  type: SchemaValueType
  cadence: Cadence
  unit?: string | null
  buckets: HistoryBucket[]
}

export interface SchemaHistory {
  schemaName: string
  label?: string | null
  values: SchemaValueHistory[]
}

/**
 * Data envelope shape a Liquid report expects. `Single` renders one specific submission,
 * `Aggregate` renders the per-value bucketed history of a schema over a date range.
 */
export type ReportType = 'Single' | 'Aggregate'

export interface Report {
  id: string
  name: string
  label?: string | null
  description?: string | null
  type: ReportType
  /** Schemas the report applies to. Empty list means "global" — the viewer can pick any schema. */
  targetSchemaNames: string[]
  createdAt: string
  createdBy?: string | null
  modifiedAt: string
  modifiedBy?: string | null
}

/** Body shape for `POST /api/reports/{name}/render`. Every field is optional; server defaults apply. */
export interface RenderReportRequest {
  /** Schema to scope the report to. Required for multi-target reports; ignored for single-target. */
  schemaName?: string | null
  /** Submission to render. Required for Single-type reports; ignored otherwise. */
  submissionId?: string | null
  /** Inclusive lower bound of the time window. Defaults to the start of the current calendar month. */
  from?: string | null
  /** Exclusive upper bound. Defaults to "now". */
  to?: string | null
}

/** Response shape returned by the render endpoint. */
export interface ReportRenderResponse {
  /** Rendered HTML — drop straight into an iframe via `srcdoc`. */
  html: string
  reportName: string
  reportLabel?: string | null
  type: ReportType
  schemaName?: string | null
  submissionId?: string | null
  from: string
  to: string
}
