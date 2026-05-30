import { Navigate, useLocation } from 'react-router-dom'
import { getApiKey, setApiKey } from '../api/client'
import { useMe } from '../api/hooks'
import type { PropsWithChildren } from 'react'

/**
 * Route guard for the authenticated section of the app.
 *
 * Two layers:
 *  1. No API key in storage → bounce to /login.
 *  2. Key present but resolves to an Application-kind account → drop the key and bounce to
 *     /login with an error. This covers the case where someone pasted an Application key
 *     directly into localStorage, or a credential's kind was changed server-side after sign-in.
 *
 * While /api/me is still resolving on first load we render nothing rather than flashing the
 * shell briefly — same effect users get for any normal slow first request.
 */
export function RequireAuth({ children }: PropsWithChildren) {
  const location = useLocation()
  const hasKey = !!getApiKey()
  const { data: me, isLoading, isError } = useMe()

  if (!hasKey) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }
  if (isError) {
    setApiKey(null)
    return <Navigate to="/login" state={{ from: location }} replace />
  }
  if (isLoading || !me) {
    return null
  }
  if (me.kind === 'Application') {
    setApiKey(null)
    return <Navigate to="/login" state={{ from: location, reason: 'application-key' }} replace />
  }
  return <>{children}</>
}
