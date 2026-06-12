import { Navigate, useLocation } from 'react-router-dom'
import { setApiKey } from '../api/client'
import { useMe } from '../api/hooks'
import type { PropsWithChildren } from 'react'

/**
 * Route guard for the authenticated section of the app.
 *
 * Authentication is whatever `/api/me` accepts — either an API key in storage (the X-Api-Key
 * header) or an SSO session cookie. We no longer gate on a key being present locally, because an
 * SSO session lives in an HttpOnly cookie the SPA can't read. So:
 *  1. `/api/me` resolves → signed in. The `Kind == 'Application'` guard still applies (defence in
 *     depth: a credential whose kind changed server-side, or an Application key pasted into
 *     storage, can't use the UI).
 *  2. `/api/me` 401s/errors → bounce to /login, clearing any stale local key.
 *
 * While `/api/me` is resolving on first load we render nothing rather than flashing the shell.
 */
export function RequireAuth({ children }: PropsWithChildren) {
  const location = useLocation()
  const { data: me, isLoading, isError } = useMe()

  if (isLoading) {
    return null
  }
  if (isError || !me) {
    setApiKey(null)
    return <Navigate to="/login" state={{ from: location }} replace />
  }
  if (me.kind === 'Application') {
    setApiKey(null)
    return <Navigate to="/login" state={{ from: location, reason: 'application-key' }} replace />
  }
  return <>{children}</>
}
