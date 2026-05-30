import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type {
  Account, CreateAccountRequest, UpdateAccountRequest,
  ApiKey, GeneratedApiKey,
  Schema, SchemaHistory, UpsertSchemaRequest,
  Submission, AdminSubmissionInput, SampleInput, ServiceStatus, Me, Paged,
  SubmissionWriteResponse, MissingByCadence,
  Report, RenderReportRequest, ReportRenderResponse,
} from './types'

export const useMe = () => useQuery({ queryKey: ['me'], queryFn: () => api.get<Me>('/api/me') })

export const useAccounts = (
  params?: { kind?: string; role?: string; includeDeleted?: boolean },
  enabled: boolean = true,
) => {
  const search = new URLSearchParams({ pageSize: '200' })
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

export const useApiKeys = (accountId?: string) =>
  useQuery({
    queryKey: ['keys', accountId],
    queryFn: () => api.get<ApiKey[]>(`/api/admin/accounts/${accountId}/keys`),
    enabled: !!accountId,
  })

export const useRotateApiKey = () => {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (accountId: string) => api.post<GeneratedApiKey>(`/api/admin/accounts/${accountId}/keys`),
    onSuccess: (_d, accountId) => qc.invalidateQueries({ queryKey: ['keys', accountId] }),
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
  params?: { includeDeleted?: boolean },
  enabled: boolean = true,
) => {
  const search = new URLSearchParams({ pageSize: '200' })
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

export const useSubmissions = (
  params: { page: number; pageSize: number; serviceId?: string; from?: string; to?: string },
  enabled: boolean = true,
) => {
  const search = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize),
  })
  if (params.serviceId) search.set('serviceId', params.serviceId)
  if (params.from)      search.set('from', params.from)
  if (params.to)        search.set('to', params.to)
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

// --- Service-facing submission hooks (work for Service-role callers; admins can use them too) ---

/** Schemas visible to the calling service account (server-side filtered). */
export const useMySchemas = (enabled: boolean = true) =>
  useQuery({
    queryKey: ['my-schemas'],
    queryFn: () => api.get<Schema[]>('/api/schemas'),
    enabled,
  })

export const useMySubmissions = (
  params: { page: number; pageSize: number; from?: string; to?: string },
  enabled: boolean = true,
) => {
  const search = new URLSearchParams({
    page: String(params.page),
    pageSize: String(params.pageSize),
  })
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

export const useReports = (enabled: boolean = true) =>
  useQuery({
    queryKey: ['reports'],
    queryFn: () => api.get<Paged<Report>>('/api/reports?pageSize=200'),
    enabled,
  })

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
