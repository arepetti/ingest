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
  /** Non-blocking warnings recorded at the last write. Empty for legacy submissions predating warning persistence. */
  warnings: string[]
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
  submissionId?: string | null
  sampleCount: number
  errors: string[]
  warnings: string[]
}

/** Per-group report returned by the bulk import endpoint (the file itself parsed successfully). */
export interface BulkImportResult {
  total: number
  succeeded: number
  failed: number
  items: BulkImportItemResult[]
}

/** Result of restoring a backup: per-collection counts of documents written. */
export interface BackupImportResult {
  restored: Record<string, number>
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
  /** Whether the email + notification feature is enabled server-side. Drives whether the related UI shows at all. */
  emailEnabled?: boolean
  /** Whether outbound webhooks are enabled server-side. Drives whether the Webhooks settings section shows. */
  webhooksEnabled?: boolean
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
export type AuditChangeType = 'Create' | 'Edit' | 'Delete'

/**
 * The type of object an audit entry targets. 'User' and 'Account' are both accounts, told apart
 * by the account's kind at the time of the change.
 */
export type AuditTargetType = 'User' | 'Account' | 'Schema' | 'ApiKey' | 'Submission' | 'Report'

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
