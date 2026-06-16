import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type {
  Account, CreateAccountRequest, UpdateAccountRequest,
  ErasureMode, ErasureResult,
  ApiKey, GeneratedApiKey,
  Cadence,
  ExploreAggregation, ExploreSeries,
  Schema, SchemaHistory, SchemaVersionHistoryEntry, SchemaVersionSnapshot, UpsertSchemaRequest,
  Submission, AdminSubmissionInput, SampleInput, ServiceStatus, Me, Paged,
  SubmissionWriteResponse, BulkImportRequest, BulkImportResult, BackupImportResult,
  MissingByCadence, MissingPeriodReport, MissingHistory,
  Report, RenderReportRequest, ReportRenderResponse,
  AuthProvider,
  AuditLog, AuditChangeType, AuditTargetType,
  EmailSettings, UpdateEmailSettingsRequest,
  EmailTemplate, UpdateEmailTemplateRequest,
  EmailMessage, EmailStatus, EmailDrainResult, SendAdhocEmailRequest,
  NotificationSettings, UpdateNotificationSettingsRequest, NotificationRunResult,
  WebhookEndpoint, CreateWebhookEndpointRequest, UpdateWebhookEndpointRequest,
  WebhookEndpointCreatedResponse, WebhookSecretResponse,
  WebhookDelivery, WebhookDeliveryStatus, WebhookDrainResult,
} from './types'

export const useMe = () => useQuery({ queryKey: ['me'], queryFn: () => api.get<Me>('/api/me') })

// --- Full-list export helpers -------------------------------------------------------------
// The grids' "Export CSV" buttons need the *entire* list, not the page currently on screen.
// These helpers walk every page of a paged endpoint (max 500 rows per request, the server cap)
// and concatenate the items. They return plain promises — they're invoked imperatively from a
// click handler, not as React Query hooks.

/** Page size used when paging through an endpoint to export the whole list (server caps at 500). */
const EXPORT_PAGE_SIZE = 500

async function fetchAllPaged<T>(path: string, search: URLSearchParams): Promise<T[]> {
  const all: T[] = []
  let page = 1
  for (;;) {
    search.set('page', String(page))
    search.set('pageSize', String(EXPORT_PAGE_SIZE))
    const res = await api.get<Paged<T>>(`${path}?${search}`)
    all.push(...res.items)
    // Stop when we've gathered everything the server says exists, or when a page comes back empty
    // (guards against an off-by-one if `total` ever lags behind the data).
    if (res.items.length === 0 || all.length >= res.total) break
    page++
  }
  return all
}

export const fetchAllAccounts = (params?: { kind?: string; role?: string; includeDeleted?: boolean }) => {
  const search = new URLSearchParams()
  if (params?.kind) search.set('kind', params.kind)
  if (params?.role) search.set('role', params.role)
  if (params?.includeDeleted) search.set('includeDeleted', 'true')
  return fetchAllPaged<Account>('/api/admin/accounts', search)
}

export const fetchAllSchemas = (params?: { includeDeleted?: boolean }) => {
  const search = new URLSearchParams()
  if (params?.includeDeleted) search.set('includeDeleted', 'true')
  return fetchAllPaged<Schema>('/api/admin/schemas', search)
}

export const fetchAllSubmissions = (params?: { serviceId?: string; schemaName?: string; from?: string; to?: string }) => {
  const search = new URLSearchParams()
  if (params?.serviceId)  search.set('serviceId', params.serviceId)
  if (params?.schemaName) search.set('schemaName', params.schemaName)
  if (params?.from)       search.set('from', params.from)
  if (params?.to)         search.set('to', params.to)
  return fetchAllPaged<Submission>('/api/admin/submissions', search)
}

export const fetchAllMySubmissions = (params?: { schemaName?: string; from?: string; to?: string }) => {
  const search = new URLSearchParams()
  if (params?.schemaName) search.set('schemaName', params.schemaName)
  if (params?.from)       search.set('from', params.from)
  if (params?.to)         search.set('to', params.to)
  return fetchAllPaged<Submission>('/api/submissions', search)
}

export const fetchAllReports = () => fetchAllPaged<Report>('/api/reports', new URLSearchParams())

export const fetchAllEmailOutbox = (params?: { status?: EmailStatus; from?: string; to?: string }) => {
  const search = new URLSearchParams()
  if (params?.status) search.set('status', params.status)
  if (params?.from)   search.set('from', params.from)
  if (params?.to)     search.set('to', params.to)
  return fetchAllPaged<EmailMessage>('/api/admin/email/outbox', search)
}

/**
 * Enabled SSO providers. Returns [] when SSO is disabled server-side, in which case the UI shows
 * no SSO buttons and no account-linking field — i.e. it looks exactly like the API-key-only build.
 * Never throws into the UI: a failure resolves to [] so the API-key login still works.
 */
export const useAuthProviders = () =>
  useQuery({
    queryKey: ['auth-providers'],
    queryFn: () => api.get<AuthProvider[]>('/api/auth/providers').catch(() => [] as AuthProvider[]),
    staleTime: 5 * 60 * 1000,
  })

export const useAccounts = (
  params?: { kind?: string; role?: string; includeDeleted?: boolean; page?: number; pageSize?: number },
  enabled: boolean = true,
) => {
  // Default to a large page so the many dropdown/count consumers keep getting "everything".
  // Grids that want real pagination pass an explicit page + pageSize.
  const search = new URLSearchParams({ pageSize: String(params?.pageSize ?? 200) })
  if (params?.page) search.set('page', String(params.page))
  if (params?.kind) search.set('kind', params.kind)
  if (params?.role) search.set('role', params.role)
  if (params?.includeDeleted) search.set('includeDeleted', 'true')
  return useQuery({
    queryKey: ['accounts', params],
    queryFn: () => api.get<Paged<Account>>(`/api/admin/accounts?${search}`),
    enabled,
  })
}

export const useCreateAccount = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: CreateAccountRequest) => api.post<Account>('/api/admin/accounts', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['accounts'] }),
  })
}

export const useUpdateAccount = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: UpdateAccountRequest }) =>
      api.put<Account>(`/api/admin/accounts/${id}`, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['accounts'] }),
  })
}

export const useDeleteAccount = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.delete<void>(`/api/admin/accounts/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['accounts'] }),
  })
}

/**
 * GDPR right-to-erasure for one account. Anonymise keeps statistical KPI values; Delete removes
 * everything. Affects many collections, so the whole cache is invalidated on success.
 */
export const useEraseAccount = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, mode }: { id: string; mode: ErasureMode }) =>
      api.post<ErasureResult>(`/api/admin/accounts/${id}/erase`, { mode }),
    onSuccess: () => qc.invalidateQueries(),
  })
}

/** Relative URL for the per-subject DSAR export download (authenticated via downloadFromUrl). */
export const personalDataExportUrl = (id: string) => `/api/admin/accounts/${id}/personal-data/export`

export const useApiKeys = (accountId?: string) =>
  useQuery({
    queryKey: ['keys', accountId],
    queryFn: () => api.get<ApiKey[]>(`/api/admin/accounts/${accountId}/keys`),
    enabled: !!accountId,
  })

export const useRotateApiKey = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ accountId, expiresAt }: { accountId: string; expiresAt?: string | null }) =>
      api.post<GeneratedApiKey>(`/api/admin/accounts/${accountId}/keys`, { expiresAt: expiresAt ?? null }),
    onSuccess: (_d, v) => qc.invalidateQueries({ queryKey: ['keys', v.accountId] }),
  })
}

export const useRevokeApiKey = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ accountId, keyId }: { accountId: string; keyId: string }) =>
      api.post<ApiKey>(`/api/admin/accounts/${accountId}/keys/${keyId}/revoke`),
    onSuccess: (_d, v) => qc.invalidateQueries({ queryKey: ['keys', v.accountId] }),
  })
}

export const useSchemas = (
  params?: { includeDeleted?: boolean; page?: number; pageSize?: number },
  enabled: boolean = true,
) => {
  // As with accounts: default to "everything" for the dropdown/lookup consumers; grids opt into
  // real pagination by passing page + pageSize.
  const search = new URLSearchParams({ pageSize: String(params?.pageSize ?? 200) })
  if (params?.page) search.set('page', String(params.page))
  if (params?.includeDeleted) search.set('includeDeleted', 'true')
  return useQuery({
    queryKey: ['schemas', params],
    queryFn: () => api.get<Paged<Schema>>(`/api/admin/schemas?${search}`),
    enabled,
  })
}

export const useCreateSchema = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: UpsertSchemaRequest) => api.post<Schema>('/api/admin/schemas', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['schemas'] }),
  })
}

export const useUpdateSchema = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: UpsertSchemaRequest }) =>
      api.put<Schema>(`/api/admin/schemas/${id}`, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['schemas'] }),
  })
}

export const useDeleteSchema = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.delete<void>(`/api/admin/schemas/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['schemas'] }),
  })
}

/** Server-side clone — picks a unique name automatically and resets audit fields. */
export const useCloneSchema = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post<Schema>(`/api/admin/schemas/${id}/clone`, {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['schemas'] }),
  })
}

/**
 * Build an example submission body for a schema (one sample per declared value with a sensible
 * default per type). Used by the "Download example submission" affordance on the schema view
 * drawer. Routed through the service-facing endpoint (`/api/schemas/{name}/example`) so
 * operators/services hit the same auth path admins use.
 */
export const fetchSchemaExample = (name: string) =>
  api.get<{ samples: SampleInput[] }>(`/api/schemas/${encodeURIComponent(name)}/example`)

export const useSchemaHistory = (name?: string) =>
  useQuery({
    queryKey: ['schema-history', name],
    queryFn: () => api.get<SchemaHistory>(`/api/admin/schemas/${encodeURIComponent(name!)}/history`),
    enabled: !!name,
  })

/** Page through a schema's saved version snapshots (newest change first), with an optional period window. */
export const useSchemaVersionHistory = (
  name: string | undefined,
  params: { page: number; pageSize: number; from?: string; to?: string },
  enabled: boolean = true,
) => {
  const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize) })
  if (params.from) search.set('from', params.from)
  if (params.to)   search.set('to', params.to)
  return useQuery({
    queryKey: ['schema-version-history', name, params],
    queryFn: () => api.get<Paged<SchemaVersionHistoryEntry>>(`/api/admin/schemas/${encodeURIComponent(name!)}/version-history?${search}`),
    enabled: !!name && enabled,
  })
}

/** Walk every page of a schema's version history for a CSV export (honours the period filter). */
export const fetchAllSchemaVersionHistory = (name: string, params?: { from?: string; to?: string }) => {
  const search = new URLSearchParams()
  if (params?.from) search.set('from', params.from)
  if (params?.to)   search.set('to', params.to)
  return fetchAllPaged<SchemaVersionHistoryEntry>(`/api/admin/schemas/${encodeURIComponent(name)}/version-history`, search)
}

/** Fetch a single version snapshot (full schema at that point in time) for the read-only view. */
export const useSchemaVersionSnapshot = (name?: string, entryId?: string) =>
  useQuery({
    queryKey: ['schema-version-snapshot', name, entryId],
    queryFn: () => api.get<SchemaVersionSnapshot>(`/api/admin/schemas/${encodeURIComponent(name!)}/version-history/${entryId}`),
    enabled: !!name && !!entryId,
  })

/** Permanently delete one version-history entry (audited server-side). */
export const useDeleteSchemaVersionEntry = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ name, entryId }: { name: string; entryId: string }) =>
      api.delete<void>(`/api/admin/schemas/${encodeURIComponent(name)}/version-history/${entryId}`),
    onSuccess: (_d, v) => qc.invalidateQueries({ queryKey: ['schema-version-history', v.name] }),
  })
}

/** Permanently delete a schema's entire version history (audited server-side). */
export const useDeleteSchemaVersionHistory = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (name: string) =>
      api.delete<void>(`/api/admin/schemas/${encodeURIComponent(name)}/version-history`),
    onSuccess: (_d, name) => qc.invalidateQueries({ queryKey: ['schema-version-history', name] }),
  })
}

// --- Explore (in-app analytics) -----------------------------------------------------------

/** Options for the Explore series query. Empty `valueNames`/`serviceIds` mean "all". */
export interface ExploreSeriesParams {
  schema: string
  valueNames?: string[]
  serviceIds?: string[]
  from?: string
  to?: string
  agg: ExploreAggregation
}

/**
 * Per-value, per-cadence, per-service breakdown for one schema, powering the Explore page's
 * Trend/Compare/Snapshot views. Only numeric values come back; the server aggregates so the
 * browser never sees raw rows. Disabled until a schema is chosen.
 */
export const useExploreSeries = (params: ExploreSeriesParams, enabled: boolean = true) => {
  const search = new URLSearchParams()
  search.set('schema', params.schema)
  search.set('agg', params.agg)
  for (const v of params.valueNames ?? []) search.append('value', v)
  for (const sid of params.serviceIds ?? []) search.append('serviceIds', sid)
  if (params.from) search.set('from', params.from)
  if (params.to)   search.set('to', params.to)
  return useQuery({
    queryKey: ['explore-series', params],
    queryFn: () => api.get<ExploreSeries>(`/api/admin/explore/series?${search}`),
    enabled: enabled && !!params.schema,
  })
}

export const useSubmissions = (
  params: { page: number; pageSize: number; serviceId?: string; schemaName?: string; from?: string; to?: string },
  enabled: boolean = true,
) => {
  const search = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize),
  })
  if (params.serviceId)  search.set('serviceId', params.serviceId)
  if (params.schemaName) search.set('schemaName', params.schemaName)
  if (params.from)       search.set('from', params.from)
  if (params.to)         search.set('to', params.to)
  return useQuery({
    queryKey: ['submissions', params],
    queryFn: () => api.get<Paged<Submission>>(`/api/admin/submissions?${search}`),
    enabled,
  })
}

export const useSubmission = (id?: string, enabled: boolean = true) =>
  useQuery({
    queryKey: ['submission', id],
    queryFn: () => api.get<Submission>(`/api/admin/submissions/${id}`),
    enabled: !!id && enabled,
  })

export const useAdminCreateSubmission = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: AdminSubmissionInput) =>
      api.post<SubmissionWriteResponse>('/api/admin/submissions', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['submissions'] }),
  })
}

export const useAdminUpdateSubmission = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: AdminSubmissionInput }) =>
      api.put<SubmissionWriteResponse>(`/api/admin/submissions/${id}`, req),
    onSuccess: (_d, v) => {
      qc.invalidateQueries({ queryKey: ['submissions'] })
      qc.invalidateQueries({ queryKey: ['submission', v.id] })
    },
  })
}

export const useDeleteSubmission = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.delete<void>(`/api/admin/submissions/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['submissions'] }),
  })
}

/**
 * Admin-only bulk import of historical submissions for one service from a JSON/CSV file. The file
 * is read client-side and its text posted as `content`. A 4xx means the file couldn't be parsed at
 * all; a 200 returns a per-group report (some groups may still have failed validation).
 */
export const useBulkImport = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: BulkImportRequest) =>
      api.post<BulkImportResult>('/api/admin/submissions/import', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['submissions'] }),
  })
}

// --- Backup / restore (admin convenience tool) --------------------------------------------

/** Relative URL for the full-registry backup download (authenticated via downloadFromUrl). */
export const backupExportUrl = () => '/api/admin/backup/export'

/**
 * Restore the whole registry from a backup file. The parsed JSON is posted as-is; the server
 * validates the format/version and then replaces every collection. Invalidates everything on
 * success because, by definition, all data has just changed.
 */
export const useImportBackup = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (backup: unknown) => api.post<BackupImportResult>('/api/admin/backup/import', backup),
    onSuccess: () => qc.invalidateQueries(),
  })
}

// --- Email + notifications (admin) --------------------------------------------------------

export const useEmailSettings = (enabled: boolean = true) =>
  useQuery({
    queryKey: ['email-settings'],
    queryFn: () => api.get<EmailSettings>('/api/admin/email/settings'),
    enabled,
  })

export const useUpdateEmailSettings = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: UpdateEmailSettingsRequest) => api.put<EmailSettings>('/api/admin/email/settings', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['email-settings'] }),
  })
}

export const useEmailTemplates = (enabled: boolean = true) =>
  useQuery({
    queryKey: ['email-templates'],
    queryFn: () => api.get<EmailTemplate[]>('/api/admin/email/templates'),
    enabled,
  })

export const useUpdateEmailTemplate = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ key, req }: { key: string; req: UpdateEmailTemplateRequest }) =>
      api.put<EmailTemplate>(`/api/admin/email/templates/${encodeURIComponent(key)}`, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['email-templates'] }),
  })
}

/** Page through the outbox (audit "Sent emails" tab), optionally filtered by delivery status. */
export const useEmailOutbox = (
  params: { page: number; pageSize: number; status?: EmailStatus; from?: string; to?: string },
  enabled: boolean = true,
) => {
  const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize) })
  if (params.status) search.set('status', params.status)
  if (params.from)   search.set('from', params.from)
  if (params.to)     search.set('to', params.to)
  return useQuery({
    queryKey: ['email-outbox', params],
    queryFn: () => api.get<Paged<EmailMessage>>(`/api/admin/email/outbox?${search}`),
    enabled,
  })
}

/** Trigger a manual outbox drain (sends pending mail now). */
export const useDrainEmail = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.post<EmailDrainResult>('/api/admin/email/drain', {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['email-outbox'] }),
  })
}

/** Send an ad-hoc plain-text email to one account (operators + admins). */
export const useSendAdhocEmail = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: SendAdhocEmailRequest) => api.post<{ id: string }>('/api/admin/email/send', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['email-outbox'] }),
  })
}

export const useNotificationSettings = (enabled: boolean = true) =>
  useQuery({
    queryKey: ['notification-settings'],
    queryFn: () => api.get<NotificationSettings>('/api/admin/notifications/settings'),
    enabled,
  })

export const useUpdateNotificationSettings = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: UpdateNotificationSettingsRequest) =>
      api.put<NotificationSettings>('/api/admin/notifications/settings', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['notification-settings'] }),
  })
}

/** Run the notification job now (internal trigger; the scheduler also runs it on a timer). */
export const useRunNotifications = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.post<NotificationRunResult>('/api/admin/notifications/run', {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['email-outbox'] }),
  })
}

// --- Webhooks (admin) ---------------------------------------------------------------------

export const useWebhookEndpoints = (enabled: boolean = true) =>
  useQuery({
    queryKey: ['webhook-endpoints'],
    queryFn: () => api.get<WebhookEndpoint[]>('/api/admin/webhooks'),
    enabled,
  })

export const useCreateWebhookEndpoint = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: CreateWebhookEndpointRequest) =>
      api.post<WebhookEndpointCreatedResponse>('/api/admin/webhooks', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['webhook-endpoints'] }),
  })
}

export const useUpdateWebhookEndpoint = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: UpdateWebhookEndpointRequest }) =>
      api.put<WebhookEndpoint>(`/api/admin/webhooks/${id}`, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['webhook-endpoints'] }),
  })
}

export const useDeleteWebhookEndpoint = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.delete<void>(`/api/admin/webhooks/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['webhook-endpoints'] }),
  })
}

/** Mint a fresh signing secret for an endpoint. The plaintext comes back exactly once. */
export const useRotateWebhookSecret = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post<WebhookSecretResponse>(`/api/admin/webhooks/${id}/rotate-secret`, {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['webhook-endpoints'] }),
  })
}

/** Enqueue a `webhook.test` delivery so the operator can verify the wiring end-to-end. */
export const useSendWebhookTest = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.post<{ id: string }>(`/api/admin/webhooks/${id}/test`, {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['webhook-deliveries'] }),
  })
}

/** Page through the webhook delivery log, optionally filtered by status and a created-at window. */
export const useWebhookDeliveries = (
  params: { page: number; pageSize: number; status?: WebhookDeliveryStatus; from?: string; to?: string },
  enabled: boolean = true,
) => {
  const search = new URLSearchParams({ page: String(params.page), pageSize: String(params.pageSize) })
  if (params.status) search.set('status', params.status)
  if (params.from)   search.set('from', params.from)
  if (params.to)     search.set('to', params.to)
  return useQuery({
    queryKey: ['webhook-deliveries', params],
    queryFn: () => api.get<Paged<WebhookDelivery>>(`/api/admin/webhooks/deliveries?${search}`),
    enabled,
  })
}

/** Walk every page of the webhook delivery log for a CSV export (honours the status/period filters). */
export const fetchAllWebhookDeliveries = (params?: { status?: WebhookDeliveryStatus; from?: string; to?: string }) => {
  const search = new URLSearchParams()
  if (params?.status) search.set('status', params.status)
  if (params?.from)   search.set('from', params.from)
  if (params?.to)     search.set('to', params.to)
  return fetchAllPaged<WebhookDelivery>('/api/admin/webhooks/deliveries', search)
}

/** Requeue a delivery (typically a failed one) for another attempt. */
export const useRedeliverWebhook = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (deliveryId: string) => api.post<void>(`/api/admin/webhooks/deliveries/${deliveryId}/redeliver`, {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['webhook-deliveries'] }),
  })
}

/** Trigger a manual outbox drain (sends pending deliveries now). */
export const useDrainWebhooks = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.post<WebhookDrainResult>('/api/admin/webhooks/drain', {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['webhook-deliveries'] }),
  })
}

// --- Audit log ----------------------------------------------------------------------------

/**
 * Page through the audit log (admin-only), newest change first. The optional change/target
 * filters map straight onto the server query params; `name` filtering is intentionally not
 * exposed here (it is reachable only by calling the export endpoint directly).
 */
export const useAuditLog = (
  params: { page: number; pageSize: number; change?: AuditChangeType; targetType?: AuditTargetType; from?: string; to?: string },
  enabled: boolean = true,
) => {
  const search = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize),
  })
  if (params.change)     search.set('change', params.change)
  if (params.targetType) search.set('targetType', params.targetType)
  if (params.from)       search.set('from', params.from)
  if (params.to)         search.set('to', params.to)
  return useQuery({
    queryKey: ['audit', params],
    queryFn: () => api.get<Paged<AuditLog>>(`/api/admin/audit?${search}`),
    enabled,
  })
}

/** Build the relative URL for the server-side CSV export, honouring the current change/target/period filters. */
export const auditExportUrl = (params: { change?: AuditChangeType; targetType?: AuditTargetType; from?: string; to?: string }) => {
  const search = new URLSearchParams()
  if (params.change)     search.set('change', params.change)
  if (params.targetType) search.set('targetType', params.targetType)
  if (params.from)       search.set('from', params.from)
  if (params.to)         search.set('to', params.to)
  const qs = search.toString()
  return qs ? `/api/admin/audit/export?${qs}` : '/api/admin/audit/export'
}

/** Page through the change history recorded for a single submission, newest first. */
export const useSubmissionHistory = (
  id: string | undefined,
  params: { page: number; pageSize: number },
  enabled: boolean = true,
) => {
  const search = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize),
  })
  return useQuery({
    queryKey: ['submission-history', id, params],
    queryFn: () => api.get<Paged<AuditLog>>(`/api/admin/submissions/${id}/history?${search}`),
    enabled: !!id && enabled,
  })
}

export const useServiceStatus = (name?: string, period: string = 'week') =>
  useQuery({
    queryKey: ['service-status', name, period],
    queryFn: () => api.get<ServiceStatus>(`/api/services/${name}/status?period=${period}`),
    enabled: !!name,
  })

/**
 * Registry-wide "what's missing right now" report, bucketed by cadence. Powers the missing-
 * submissions cards on the operator dashboard. Cadences with nothing missing are omitted by
 * the server, so an empty array means everyone is up to date.
 */
export const useMissingSubmissions = (enabled: boolean = true) =>
  useQuery({
    queryKey: ['missing-submissions'],
    queryFn: () => api.get<MissingByCadence[]>('/api/admin/status/missing'),
    enabled,
  })

/**
 * Detailed missing-submissions report for a single cadence and a single window, addressed by
 * `offset` (0 = current, -1 = previous, -N = N periods ago). Powers the per-period analytics
 * page's table and per-service bar chart.
 */
export const useMissingPeriod = (cadence: Cadence, offset: number, enabled: boolean = true) =>
  useQuery({
    queryKey: ['missing-period', cadence, offset],
    queryFn: () =>
      api.get<MissingPeriodReport>(`/api/admin/status/missing/period?cadence=${cadence}&offset=${offset}`),
    enabled,
  })

/**
 * "Missing submissions over time" trend for a single cadence: total missing required values for
 * each of the last `periods` windows, oldest first. Powers the analytics page's trend chart.
 */
export const useMissingHistory = (cadence: Cadence, periods: number = 12, enabled: boolean = true) =>
  useQuery({
    queryKey: ['missing-history', cadence, periods],
    queryFn: () =>
      api.get<MissingHistory>(`/api/admin/status/missing/history?cadence=${cadence}&periods=${periods}`),
    enabled,
  })

// --- Service-facing submission hooks (work for Service-role callers; admins can use them too) ---

/** Schemas visible to the calling service account (server-side filtered). */
export const useMySchemas = (enabled: boolean = true) =>
  useQuery({
    queryKey: ['my-schemas'],
    queryFn: () => api.get<Schema[]>('/api/schemas'),
    enabled,
  })

export const useMySubmissions = (
  params: { page: number; pageSize: number; schemaName?: string; from?: string; to?: string },
  enabled: boolean = true,
) => {
  const search = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize),
  })
  if (params.schemaName) search.set('schemaName', params.schemaName)
  if (params.from) search.set('from', params.from)
  if (params.to)   search.set('to', params.to)
  return useQuery({
    queryKey: ['my-submissions', params],
    queryFn: () => api.get<Paged<Submission>>(`/api/submissions?${search}`),
    enabled,
  })
}

export const useMySubmission = (id?: string, enabled: boolean = true) =>
  useQuery({
    queryKey: ['my-submission', id],
    queryFn: () => api.get<Submission>(`/api/submissions/${id}`),
    enabled: !!id && enabled,
  })

/** Service-facing create. Body shape matches the admin one minus `serviceAccountId` (derived from the caller). */
export const useCreateMySubmission = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (req: { samples: SampleInput[] }) =>
      api.post<SubmissionWriteResponse>('/api/submissions', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['my-submissions'] }),
  })
}

export const useReplaceMySubmission = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: { samples: SampleInput[] } }) =>
      api.put<SubmissionWriteResponse>(`/api/submissions/${id}`, req),
    onSuccess: (_d, v) => {
      qc.invalidateQueries({ queryKey: ['my-submissions'] })
      qc.invalidateQueries({ queryKey: ['my-submission', v.id] })
    },
  })
}

// --- Reports ------------------------------------------------------------------------------

export const useReports = (
  params?: { page?: number; pageSize?: number },
  enabled: boolean = true,
) => {
  // Default to "everything" (TopBar search + other consumers); the grid passes page + pageSize.
  const search = new URLSearchParams({ pageSize: String(params?.pageSize ?? 200) })
  if (params?.page) search.set('page', String(params.page))
  return useQuery({
    queryKey: ['reports', params],
    queryFn: () => api.get<Paged<Report>>(`/api/reports?${search}`),
    enabled,
  })
}

export const useReport = (name?: string, enabled: boolean = true) =>
  useQuery({
    queryKey: ['report', name],
    queryFn: () => api.get<Report>(`/api/reports/${encodeURIComponent(name!)}`),
    enabled: !!name && enabled,
  })

/**
 * Upload a new report. The admin SPA always pushes the raw text through the JSON variant of
 * the upload endpoint — reports are HTML + a YAML front-matter header so they're small and
 * already plain text in the picker. The multipart variant exists for non-browser tooling.
 */
export const useUploadReport = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ fileName, content }: { fileName: string; content: string }) =>
      api.post<Report>('/api/admin/reports/json', { fileName, content }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['reports'] }),
  })
}

export const useDeleteReport = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.delete<void>(`/api/admin/reports/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['reports'] }),
  })
}

/**
 * Render a report and return the produced HTML. We do not cache renders by request — the user
 * can re-render after tweaking the filters and expects fresh output every time.
 */
export const useRenderReport = () =>
  useMutation({
    mutationFn: ({ name, req }: { name: string; req: RenderReportRequest }) =>
      api.post<ReportRenderResponse>(`/api/reports/${encodeURIComponent(name)}/render`, req),
  })
