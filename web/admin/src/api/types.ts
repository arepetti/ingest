import type { Capability } from './capabilities'

/**
 * 'User' = interactive account: can log in to the admin UI and call APIs.
 * 'Application' = automated credential: API-only, blocked from the UI sign-in path.
 * The role is a decorative template that seeds a default capability bundle; the effective
 * capability set (orthogonal to the kind) is what actually governs what the account can DO.
 */
export type AccountKind = 'User' | 'Application'
export type AccountRole = 'Service' | 'Operator' | 'Admin' | 'Approver'
export type SchemaValueType = 'String' | 'Integer' | 'Number' | 'Date' | 'Boolean'

/** Whether a schema value is submitted or computed from sibling values. */
export type SchemaValueKind = 'UserDefined' | 'Calculated'

// --- Approval workflow ---------------------------------------------------------------------

/** Approval lifecycle state of a submission. `NotRequired` is the legacy/never-gated default. */
export type ApprovalStatus = 'NotRequired' | 'Pending' | 'Approved' | 'Rejected'

/** Where a submission originated: programmatic API call vs. the web console. */
export type SubmissionSource = 'Api' | 'Manual'

/** How a schema (or the global default) decides whether submissions need approval. */
export type ApprovalMode = 'None' | 'UseGlobalDefault' | 'Required'

/** Which submission sources an approval policy applies to. */
export type ApprovalSourceScope = 'Both' | 'ManualOnly' | 'ApiOnly'

/** Whether a designated approver must approve or may approve. */
export type ApproverRequirement = 'Required' | 'Optional'

/** The decision a reviewer recorded. */
export type ApprovalDecision = 'Approved' | 'Rejected'

/**
 * What kind of approver a spec designates: a named account, or the dynamic "service owner" that
 * resolves per submission to the account that sent it (so the submitting service can review its own data).
 */
export type ApproverKind = 'Account' | 'ServiceOwner'

/** A designated approver in an approval policy. */
export interface ApproverSpec {
  accountId: string
  requirement: ApproverRequirement
  /** Defaults to `Account`; `ServiceOwner` ignores `accountId` and binds to the submitter. */
  kind?: ApproverKind
}

/** An approval policy (per-schema or the global default). */
export interface ApprovalPolicy {
  mode: ApprovalMode
  appliesToSources: ApprovalSourceScope
  approvers: ApproverSpec[]
}

/** A cross-cutting approval rule: require approval for a set of services and schemas (empty = all). */
export interface ApprovalRule {
  id: string
  label?: string | null
  enabled: boolean
  serviceIds: string[]
  schemaIds: string[]
  policy: ApprovalPolicy
  createdAt: string
  createdBy?: string | null
  modifiedAt: string
  modifiedBy?: string | null
}

/** Body for creating/updating an approval rule. */
export interface UpsertApprovalRuleRequest {
  label?: string | null
  enabled: boolean
  serviceIds: string[]
  schemaIds: string[]
  policy: ApprovalPolicy
}

/** One recorded approval/rejection decision on a submission. */
export interface SubmissionApproval {
  approverAccountId: string
  approverName?: string | null
  decision: ApprovalDecision
  decidedAt: string
  /** Optional note; carries the reject reason. */
  note?: string | null
}
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

/** An SSO identity link on an account (provider id + verified email). Relevant only when SSO is enabled. */
export interface ExternalLogin {
  provider: string
  email: string
}

export interface Account {
  id: string
  name: string
  label?: string | null
  description?: string | null
  /** Contact email used by the email/notification features. May be empty for legacy accounts. */
  email?: string | null
  kind: AccountKind
  role: AccountRole
  enabled: boolean
  createdAt: string
  createdBy?: string | null
  modifiedAt: string
  modifiedBy?: string | null
  isDeleted: boolean
  /** SSO identity links. Only ever populated for User-kind accounts; empty otherwise. */
  externalLogins?: ExternalLogin[]
  /** Stored capability overrides. Empty means "follow the role default bundle". */
  capabilities?: Capability[]
  /** Resolved capability set actually in force (read-only; set `capabilities` to change it). */
  effectiveCapabilities?: Capability[]
  /**
   * Per-service scope (allowlist of service-account ids). Empty means unrestricted — the account sees
   * every service. Non-empty confines all cross-service reads to these ids. Ignored for Admins.
   */
  assignedServiceIds?: string[]
}

export interface CreateAccountRequest {
  name: string
  label?: string | null
  description?: string | null
  /** Contact email. Asked for in the UI; the server accepts blank for backwards compatibility. */
  email?: string | null
  kind: AccountKind
  role: AccountRole
  enabled?: boolean
  /** SSO identity links. Only valid for User-kind accounts. */
  externalLogins?: ExternalLogin[]
  /** Capability overrides. Omit/empty seeds the role default bundle; a non-empty list is stored verbatim. Ignored for Admins. */
  capabilities?: Capability[]
  /** Per-service scope. Omit/empty leaves the account unrestricted; a non-empty list confines it to those service ids. Ignored for Admins. */
  assignedServiceIds?: string[]
}

export interface UpdateAccountRequest {
  label?: string | null
  description?: string | null
  /** Contact email. Blank clears it. */
  email?: string | null
  role: AccountRole
  enabled: boolean
  /** Replacement set of SSO identity links. Omit to leave links untouched; pass [] to clear. */
  externalLogins?: ExternalLogin[]
  /** Replacement capability override set. Omit to leave untouched; [] clears (reverts to role defaults). Ignored for Admins. */
  capabilities?: Capability[]
  /** Replacement per-service scope. Omit to leave untouched; [] clears (unrestricted); non-empty confines to those service ids. Ignored for Admins. */
  assignedServiceIds?: string[]
}

/**
 * GDPR right-to-erasure mode. 'Anonymise' keeps the statistical KPI values but strips identity;
 * 'Delete' removes the account and everything tied to it.
 */
export type ErasureMode = 'Anonymise' | 'Delete'

/** Per-collection tally returned by an erasure request. */
export interface ErasureResult {
  accountId: string
  pseudonym: string
  mode: ErasureMode
  submissionsAffected: number
  samplesAffected: number
  emailsRemoved: number
  auditEntriesAffected: number
  apiKeysRemoved: number
}

/** One SSO provider the SPA can render a "Continue with …" button for. Empty list ⇒ SSO disabled. */
export interface AuthProvider {
  id: string
  displayName: string
  loginUrl: string
}

export interface ApiKey {
  id: string
  accountId: string
  keyId: string
  createdAt: string
  expiresAt?: string | null
  revokedAt?: string | null
  /** Free-form note recording who/what the key is for (e.g. holiday cover). Empty when unset. */
  description?: string | null
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
  /** User-defined (submitted) or calculated (derived from sibling values). Defaults to UserDefined. */
  kind?: SchemaValueKind
  /** NCalc formula when kind is Calculated. */
  expression?: string | null
  type: SchemaValueType
  unit?: string | null
  cadence: Cadence
  required: boolean
  modifiable: boolean
  enabled: boolean
  min?: number | null
  max?: number | null
  /** Lower edge of the ideal (green) range in the RAG target band. Non-enforced; charts only. */
  greenMin?: number | null
  /** Upper edge of the ideal (green) range. Non-enforced; charts only. */
  greenMax?: number | null
  /** Lower edge of the acceptable (amber) range; below it is "red". Non-enforced; charts only. */
  amberMin?: number | null
  /** Upper edge of the acceptable (amber) range; above it is "red". Non-enforced; charts only. */
  amberMax?: number | null
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
  /**
   * Optional approval policy. Null (the default) means no approval. Only consulted when the
   * `approvalEnabled` master switch is on.
   */
  approval?: ApprovalPolicy | null
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
  /** Non-blocking warnings recorded at the last write. Empty for legacy submissions predating warning persistence. */
  warnings: string[]
  submittedAt: string
  replacedAt?: string | null
  createdAt: string
  createdBy?: string | null
  modifiedAt: string
  modifiedBy?: string | null
  isDeleted: boolean
  /** Where the submission came from. Defaults to `Api` on legacy rows. */
  source: SubmissionSource
  /** Approval lifecycle state. `NotRequired` on legacy rows and whenever approval doesn't apply. */
  approvalStatus: ApprovalStatus
  /** Snapshot of the approvers required when approval was triggered (frozen against later policy edits). */
  requiredApprovers: ApproverSpec[]
  /** Recorded approve/reject decisions, newest last. */
  approvals: SubmissionApproval[]
  /** True while this is a work-in-progress draft: excluded from every live stream and from approval until published. `false` on legacy rows. */
  isDraft: boolean
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

/** One (schema, value) pair a dry-run validation would discard before persistence. */
export interface SampleRef {
  schemaName: string
  valueName: string
}

/**
 * Verdict from a validate-only (dry-run) submission: what a real submission would do, without
 * saving. `valid` is the headline; the rest explains why and previews the would-be approval state.
 */
export interface SubmissionValidationResponse {
  valid: boolean
  errors: string[]
  warnings: string[]
  discardedSamples: SampleRef[]
  approvalStatus: ApprovalStatus
  requiredApprovers: ApproverSpec[]
}

/** File format accepted by the admin bulk import endpoint. */
export type BulkImportFormat = 'Json' | 'Csv'

/** Body for the admin bulk import endpoint: target service, format, and the raw file text. */
export interface BulkImportRequest {
  serviceAccountId: string
  format: BulkImportFormat
  content: string
}

/** Outcome for one submission group within a bulk import. */
export interface BulkImportItemResult {
  index: number
  /** CSV group key when present; null for JSON groups. */
  group?: string | null
  success: boolean
  /** True when the group was a no-op because the submission already existed. */
  skipped: boolean
  submissionId?: string | null
  sampleCount: number
  errors: string[]
  warnings: string[]
}

/** Per-group report returned by the bulk import endpoint (the file itself parsed successfully). */
export interface BulkImportResult {
  total: number
  succeeded: number
  /** Groups skipped because the submission already existed (idempotent import). */
  skipped: number
  failed: number
  items: BulkImportItemResult[]
}

/** Result of restoring a backup: per-collection counts of documents written. */
export interface BackupImportResult {
  restored: Record<string, number>
}

/** Result of importing an accounts file: how many were created/updated, plus any skipped entries. */
export interface AccountsImportResult {
  created: number
  updated: number
  errors: string[]
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

/**
 * Which cadence window a missing-submissions bucket describes. 'Current' = the window is still
 * open (submissions can still arrive — render as a soft warning); 'Previous' = the window has
 * closed and the data is overdue (render as an error).
 */
export type MissingPeriodKind = 'Current' | 'Previous'

/** A per-cadence bucket of the missing-submissions report. The server omits empty buckets. */
export interface MissingByCadence {
  cadence: Cadence
  periodStart: string
  periodEnd: string
  period: MissingPeriodKind
  entries: MissingSubmissionEntry[]
}

/**
 * Detailed missing-submissions report for a single cadence and a single window addressed by
 * `offset` (0 = current, -1 = previous, -N = N periods ago). Powers the analytics page's table
 * and per-service breakdown.
 */
export interface MissingPeriodReport {
  cadence: Cadence
  offset: number
  periodStart: string
  periodEnd: string
  entries: MissingSubmissionEntry[]
}

/** One point on the "missing submissions over time" trend for a single cadence. */
export interface MissingHistoryPoint {
  offset: number
  periodStart: string
  periodEnd: string
  /** Total missing required values across every service and schema in the window. */
  totalMissing: number
}

/** The "missing submissions over time" trend for a single cadence, oldest period first. */
export interface MissingHistory {
  cadence: Cadence
  points: MissingHistoryPoint[]
}

export interface Me {
  id: string
  name: string
  label?: string | null
  role: AccountRole
  kind: AccountKind
  /** The effective capability set in force for this account; drives every capability gate in the UI. */
  capabilities?: Capability[]
  /**
   * Per-service scope (allowlist of service-account ids) in force for this session. Empty means
   * unrestricted (every service visible). Non-empty means the UI should badge the active scope and
   * the account only ever sees those services. Admins are always unrestricted.
   */
  assignedServiceIds?: string[]
  /** Whether the email + notification feature is enabled server-side. Drives whether the related UI shows at all. */
  emailEnabled?: boolean
  /** Whether outbound webhooks are enabled server-side. Drives whether the Webhooks settings section shows. */
  webhooksEnabled?: boolean
  /** Whether integrations (e.g. Microsoft Teams) are enabled server-side. Drives whether the Integrations settings section shows. */
  integrationsEnabled?: boolean
  /** Whether the submission approval workflow is enabled server-side. Drives all approval-related UI. */
  approvalEnabled?: boolean
  /** Whether the global default approval policy currently requires approval (so schemas deferring to it are gated). */
  approvalDefaultRequired?: boolean
  /** Server application version (from Directory.Build.props), shown in the dashboard footer. */
  version?: string
}

// --- Email + notifications -----------------------------------------------------------------

/** Delivery state of a queued email. */
export type EmailStatus = 'Pending' | 'Sending' | 'Sent' | 'Failed'

/** SMTP settings as returned by the API. The password is write-only and never included. */
export interface EmailSettings {
  host: string
  port: number
  useStartTls: boolean
  username?: string | null
  fromAddress: string
  fromName?: string | null
  /** True when a password is stored (the value itself is never returned). */
  hasPassword: boolean
  /** True when enough is set to attempt a send (host + from address). */
  configured: boolean
}

/** Body for updating the SMTP settings. The password is only touched when `updatePassword` is true. */
export interface UpdateEmailSettingsRequest {
  host: string
  port: number
  useStartTls: boolean
  username?: string | null
  fromAddress: string
  fromName?: string | null
  /** When false the stored password is kept. When true it's replaced with `password` (blank clears it). */
  updatePassword?: boolean
  password?: string | null
}

/** An editable email template (Liquid). The key is immutable. */
export interface EmailTemplate {
  key: string
  name: string
  description?: string | null
  subject: string
  htmlBody?: string | null
  textBody: string
  modifiedAt: string
  modifiedBy?: string | null
}

/** Body for updating a template's content. */
export interface UpdateEmailTemplateRequest {
  name: string
  description?: string | null
  subject: string
  htmlBody?: string | null
  textBody: string
}

/** One outbox message shown on the audit "Sent emails" tab. */
export interface EmailMessage {
  id: string
  toAddress: string
  toName?: string | null
  subject: string
  status: EmailStatus
  attempts: number
  lastError?: string | null
  createdAt: string
  sentAt?: string | null
  category: string
  relatedAccountId?: string | null
}

/** Result of a manual outbox drain. */
export interface EmailDrainResult {
  sent: number
  failed: number
}

/** Body for the ad-hoc "send an email to an account" action. */
export interface SendAdhocEmailRequest {
  accountId: string
  subject: string
  body: string
}

/** A single notification trigger's configuration. */
export interface NotificationRule {
  enabled: boolean
  notifyServiceAccount: boolean
  notifyAdminList: boolean
}

/** The whole notification configuration. */
export interface NotificationSettings {
  upcoming: NotificationRule
  missed: NotificationRule
  warnings: NotificationRule
  pendingApproval: NotificationRule
  approved: NotificationRule
  rejected: NotificationRule
  draftSaved: NotificationRule
  upcomingLeadHours: number
  adminRecipientAccountIds: string[]
}

/** Body for updating the notification configuration (same shape as the settings). */
export type UpdateNotificationSettingsRequest = NotificationSettings

/** Per-trigger email counts produced by one notification run. */
export interface NotificationRunResult {
  upcomingQueued: number
  missedQueued: number
  warningsQueued: number
  totalQueued: number
}

/** The kind of change recorded in the audit log. */
export type AuditChangeType = 'Create' | 'Edit' | 'Delete' | 'Approve' | 'Reject'

/**
 * The type of object an audit entry targets. 'User' and 'Account' are both accounts, told apart
 * by the account's kind at the time of the change.
 */
export type AuditTargetType = 'User' | 'Account' | 'Schema' | 'ApiKey' | 'Submission' | 'Report' | 'SchemaHistory' | 'ApprovalRule' | 'Settings' | 'Backup'

/** A single audit-log entry: who changed what, when, and how. */
export interface AuditLog {
  id: string
  timestamp: string
  targetType: AuditTargetType
  targetId: string
  targetName?: string | null
  change: AuditChangeType
  actorId?: string | null
  actorName?: string | null
  /** Free-form note attached to the entry (e.g. a reject reason). */
  note?: string | null
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
  /** Lower edge of the ideal (green) range, overlaid on the chart when set. */
  greenMin?: number | null
  /** Upper edge of the ideal (green) range, overlaid on the chart when set. */
  greenMax?: number | null
  /** Lower edge of the acceptable (amber) range, overlaid on the chart when set. */
  amberMin?: number | null
  /** Upper edge of the acceptable (amber) range, overlaid on the chart when set. */
  amberMax?: number | null
  buckets: HistoryBucket[]
}

export interface SchemaHistory {
  schemaName: string
  label?: string | null
  values: SchemaValueHistory[]
}

/** One row in a schema's version history: metadata about a single save (no schema body). */
export interface SchemaVersionHistoryEntry {
  id: string
  schemaId: string
  schemaName: string
  /** ISO-8601 timestamp of when the save happened. */
  changeDate: string
  authorId?: string | null
  authorName?: string | null
  /** Version before this save; null for the initial create. */
  oldVersion?: number | null
  /** Version after this save. */
  newVersion: number
  /** Whether the version number changed in this save. */
  versionBumped: boolean
  /** Whether the schema was Published (Enabled) at this point; false means Draft. */
  enabled: boolean
  /** Number of submissions for this schema at the time of the save. */
  submissionCount: number
}

/** A version-history entry plus the full schema snapshot (for the read-only "view this version" page). */
export interface SchemaVersionSnapshot extends SchemaVersionHistoryEntry {
  schema: Schema
}

// --- Explore (in-app analytics) ------------------------------------------------------------

/** How an Explore bucket reduces its samples. Wire values mirror the C# enum member names. */
export type ExploreAggregation = 'Average' | 'Sum' | 'Min' | 'Max' | 'Count'

/** One service's reduced value inside an Explore bucket. */
export interface ExploreServicePoint {
  serviceId: string
  /** The bucket reduced by the requested aggregation, for this service only. */
  value: number
  /** Number of samples this service contributed to the bucket. */
  count: number
  /** Anomaly score against this service's preceding history; null unless anomaly scoring was requested. */
  z?: number | null
  /** Whether `z` crossed the requested threshold. */
  isAnomaly?: boolean
}

/** One cadence bucket of an Explore value series, with the overall and per-service reductions. */
export interface ExploreBucket {
  periodStart: string
  periodEnd: string
  /** The bucket reduced across every in-scope service. */
  value: number
  /** Total samples folded into the bucket. */
  count: number
  services: ExploreServicePoint[]
  /** Anomaly score of the overall (combined) value against preceding buckets; null unless requested. */
  z?: number | null
  /** Whether the overall `z` crossed the requested threshold. */
  isAnomaly?: boolean
}

/** A single value's bucketed Explore timeline. */
export interface ExploreValueSeries {
  valueName: string
  label?: string | null
  /** Always numeric (Number or Integer) — non-numeric values never produce a series. */
  type: SchemaValueType
  cadence: Cadence
  unit?: string | null
  buckets: ExploreBucket[]
}

/** A service appearing in an Explore result, with its label resolved. */
export interface ExploreServiceRef {
  serviceId: string
  serviceName: string
  serviceLabel?: string | null
}

/** Red/Amber/Green status of a value against its target band. Mirrors the C# enum member names. */
export type RagStatus = 'Green' | 'Amber' | 'Red'

/** Which sample the scorecard shows per service. Mirrors the C# enum member names. */
export type ScorecardMode = 'LatestAvailable' | 'LastPeriod'

/** Which period `LastPeriod` mode reads. Mirrors the C# enum member names. */
export type ScorecardPeriod = 'Current' | 'LatestClosed'

/**
 * One service's RAG-classified sample for a banded value on the scorecard. A "missing" cell (the
 * service didn't submit the requested period) has `status`, `value` and `submissionId` all null.
 */
export interface ExploreScorecardCell {
  serviceId: string
  /** Submission the sample came from, so the card can deep-link to it; null when missing. */
  submissionId: string | null
  /** The numeric value the service reported; null when missing. */
  value: number | null
  /** Where `value` falls in the value's target band; null when missing. */
  status: RagStatus | null
  periodStart: string
  periodEnd: string
}

/** A banded value and the latest RAG status of every service that reported it. */
export interface ExploreScorecardValue {
  valueName: string
  label?: string | null
  unit?: string | null
  cells: ExploreScorecardCell[]
}

/** One enabled schema's banded values, grouped under the schema for the scorecard. */
export interface ExploreScorecardSchema {
  schemaName: string
  schemaLabel?: string | null
  values: ExploreScorecardValue[]
}

/** Response shape of `GET /api/admin/explore/scorecard`: a cross-schema RAG status board. */
export interface ExploreScorecard {
  services: ExploreServiceRef[]
  schemas: ExploreScorecardSchema[]
}

/** Response shape of `GET /api/admin/explore/series`. */
export interface ExploreSeries {
  schemaName: string
  schemaLabel?: string | null
  aggregation: ExploreAggregation
  from?: string | null
  to?: string | null
  services: ExploreServiceRef[]
  values: ExploreValueSeries[]
}

/** Whether a submitted value for the target period reads as a statistical anomaly. Mirrors the C# enum. */
export type AnomalyState = 'Normal' | 'Anomaly'

/**
 * One service's anomaly result for a numeric value in the target period. A "missing" cell (the
 * service didn't submit the period) has `state`, `value`, `z` and `submissionId` all null.
 */
export interface ExploreAnomalyCell {
  serviceId: string
  /** Submission the tested sample came from, so the card can deep-link to it; null when missing. */
  submissionId: string | null
  /** The value tested; null when missing. */
  value: number | null
  /** The standardised score; null when missing or with too little history. */
  z: number | null
  /** Anomaly classification; null when missing. */
  state: AnomalyState | null
  periodStart: string
  periodEnd: string
}

/** A numeric value and every applicable service's anomaly result for the target period. */
export interface ExploreAnomalyValue {
  valueName: string
  label?: string | null
  unit?: string | null
  cells: ExploreAnomalyCell[]
}

/** One scanned schema's numeric values for the anomaly board, grouped under the schema. */
export interface ExploreAnomalySchema {
  schemaName: string
  schemaLabel?: string | null
  values: ExploreAnomalyValue[]
}

/** Response shape of `GET /api/admin/explore/anomalies`: a per-period anomaly status board. */
export interface ExploreAnomalies {
  services: ExploreServiceRef[]
  schemas: ExploreAnomalySchema[]
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

// --- Webhooks ------------------------------------------------------------------------------

/**
 * The outbound event kinds an endpoint can subscribe to. Wire values are the C# enum names
 * (the API serialises enums with their member names). The dotted name a consumer actually
 * receives in the payload (`submission.accepted`, …) is a separate, server-side concern.
 */
export type WebhookEventKind =
  | 'SubmissionAccepted'
  | 'SubmissionWarnings'
  | 'WindowUpcoming'
  | 'WindowMissed'
  | 'SubmissionPendingApproval'
  | 'SubmissionApproved'
  | 'SubmissionRejected'

/** Delivery state of a queued webhook POST. Mirrors the email outbox states. */
export type WebhookDeliveryStatus = 'Pending' | 'Sending' | 'Sent' | 'Failed'

/** A registered outbound webhook subscription. The signing secret is never returned. */
export interface WebhookEndpoint {
  id: string
  name: string
  url: string
  enabled: boolean
  events: WebhookEventKind[]
  /** Optional service filter; null = fire for every service. */
  serviceAccountId?: string | null
  description?: string | null
  /** True when a signing secret is set (the value itself is never exposed). */
  hasSecret: boolean
  createdAt: string
  modifiedAt: string
  modifiedBy?: string | null
}

/** Body for creating an endpoint. When `generateSecret` is true the secret is returned once. */
export interface CreateWebhookEndpointRequest {
  name: string
  url: string
  enabled?: boolean
  events?: WebhookEventKind[]
  serviceAccountId?: string | null
  description?: string | null
  generateSecret?: boolean
}

/** Body for updating an endpoint. The signing secret is managed via rotate, not here. */
export interface UpdateWebhookEndpointRequest {
  name: string
  url: string
  enabled: boolean
  events?: WebhookEventKind[]
  serviceAccountId?: string | null
  description?: string | null
}

/** Response when an endpoint is created. `secret` is non-null only when one was generated. */
export interface WebhookEndpointCreatedResponse {
  endpoint: WebhookEndpoint
  secret?: string | null
}

/** Response when a signing secret is rotated: carries the plaintext exactly once. */
export interface WebhookSecretResponse {
  endpoint: WebhookEndpoint
  secret: string
}

/** One row in the webhook delivery log. */
export interface WebhookDelivery {
  id: string
  endpointId: string
  url: string
  /** Dotted event name as the consumer sees it (`submission.accepted`, `webhook.test`, …). */
  event: string
  eventId: string
  status: WebhookDeliveryStatus
  attempts: number
  lastError?: string | null
  lastStatusCode?: number | null
  createdAt: string
  deliveredAt?: string | null
  nextAttemptAt?: string | null
  relatedAccountId?: string | null
}

/** Result of a manual webhook outbox drain. */
export interface WebhookDrainResult {
  sent: number
  failed: number
}

// --- Integrations (Microsoft Teams) --------------------------------------------------------

/** Provider an integration targets. Only Microsoft Teams ships today. */
export type IntegrationKind = 'MicrosoftTeams'

/** Whether a Teams integration targets a single user or a channel. */
export type TeamsTargetKind = 'User' | 'Channel'

/** A weekday name, matching the .NET `DayOfWeek` enum serialised as a string. */
export type Weekday = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday'

/** How often an integration's scheduled pass runs (mirrors the schema cadences, minus Fortnightly). */
export type IntegrationFrequency = 'Daily' | 'Weekly' | 'Monthly' | 'Quarterly' | 'SemiAnnually' | 'Yearly'

/** When an integration's scheduled pass runs. */
export interface IntegrationSchedule {
  /** How often the pass runs. */
  frequency: IntegrationFrequency
  /** Weekdays the pass runs on (Weekly only); empty = every day. */
  days: Weekday[]
  /** Day of the month (1-31) for the Monthly-and-longer frequencies; clamped to month length. */
  dayOfMonth: number
  /** When true, run on the last day of the month instead of `dayOfMonth`. */
  lastDayOfMonth: boolean
  /** Anchor month (1-12) for Quarterly / SemiAnnually / Yearly. */
  anchorMonth: number
  /** Hour of day (UTC, 0-23). */
  hourUtc: number
  /** Minute of the hour (UTC, 0-59). */
  minuteUtc: number
}

/** Teams target. The captured conversation reference is never exposed. */
export interface TeamsTarget {
  kind: TeamsTargetKind
  /** Stable id of the user (Entra object id / UPN / email) or channel. */
  targetId: string
  displayName?: string | null
  /** True once the bot has been contacted and a conversation reference is stored. */
  hasConversation: boolean
}

/** A configured integration. */
export interface Integration {
  id: string
  label?: string | null
  enabled: boolean
  kind: IntegrationKind
  /** Scoped services; empty = all. */
  serviceIds: string[]
  /** Scoped schemas; empty = all. */
  schemaIds: string[]
  schedule: IntegrationSchedule
  teams: TeamsTarget
  createdAt: string
  modifiedAt: string
  modifiedBy?: string | null
}

/** Target fields a client may set when creating/updating an integration. */
export interface TeamsTargetInput {
  kind: TeamsTargetKind
  targetId: string
  displayName?: string | null
}

/** Body for creating/updating an integration. */
export interface IntegrationRequest {
  label?: string | null
  enabled: boolean
  kind: IntegrationKind
  serviceIds: string[]
  schemaIds: string[]
  schedule: IntegrationSchedule
  teams: TeamsTargetInput
}

/** Microsoft Teams bot connection settings. The bot secret is write-only and never returned. */
export interface TeamsConnection {
  appId?: string | null
  tenantId?: string | null
  singleTenant: boolean
  /** True when a bot secret is stored. */
  hasPassword: boolean
  /** True when both an app id and a secret are present. */
  isConfigured: boolean
  modifiedAt: string
  modifiedBy?: string | null
}

/** Body for updating the Teams connection. The secret is write-once. */
export interface UpdateTeamsConnectionRequest {
  appId?: string | null
  tenantId?: string | null
  singleTenant: boolean
  updatePassword: boolean
  password?: string | null
}

/** Outcome of verifying the Teams bot credentials. */
export interface TeamsConnectionTestResult {
  ok: boolean
  error?: string | null
}

/** Outcome of an integration run pass. */
export interface IntegrationRunResult {
  prompted: number
  skipped: number
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
