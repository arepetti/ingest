import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Button, Card, Divider, Field, Input, MessageBarBody, MessageBarTitle, Title2, makeStyles, tokens } from '@fluentui/react-components'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { formatApiError, setApiKey } from '../api/client'
import { api } from '../api/client'
import { useAuthProviders } from '../api/hooks'
import type { Me } from '../api/types'

const useStyles = makeStyles({
  root: {
    display: 'flex',
    minHeight: '100vh',
    alignItems: 'center',
    justifyContent: 'center',
    padding: '24px',
  },
  card: {
    width: '420px',
    maxWidth: '100%',
    padding: '24px',
    display: 'flex',
    flexDirection: 'column',
    gap: '16px',
  },
  hint: {
    color: tokens.colorNeutralForeground3,
    fontSize: '12px',
  },
  providers: {
    display: 'flex',
    flexDirection: 'column',
    gap: '8px',
  },
})

// Map the server's sso_error codes to a friendly sentence. Anything unrecognised falls through to
// a generic message so we never leave the user staring at a bare code.
const SSO_ERRORS: Record<string, string> = {
  not_linked: 'That account is not set up for single sign-on here. Ask an administrator to link your identity, or sign in with an API key.',
  no_email: "Your identity provider didn't share a verified email, which is required to sign in.",
  remote: 'Single sign-on was cancelled or failed. Please try again.',
}

export function Login() {
  const s = useStyles()
  const nav = useNavigate()
  const [params] = useSearchParams()
  const [key, setKey] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const { data: providers } = useAuthProviders()
  const ssoError = params.get('sso_error')
  const ssoMessage = ssoError ? (SSO_ERRORS[ssoError] ?? 'Single sign-on did not complete. Please try again.') : null

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault()
    setError(null)
    setBusy(true)
    setApiKey(key.trim())
    try {
      const me = await api.get<Me>('/api/me')
      // Application-kind credentials are intentionally API-only — services that submit data through
      // a script use one of these. Block them at the UI boundary so we don't half-render a console
      // that they can't really use anyway.
      if (me.kind === 'Application') {
        setApiKey(null)
        setError('This API key is for application (non-interactive) use only. Ask an administrator for a User-kind credential to access the admin UI.')
        return
      }
      nav('/', { replace: true })
    } catch (err) {
      setApiKey(null)
      setError(formatApiError(err))
    } finally {
      setBusy(false)
    }
  }

  function signInWith(loginUrl: string) {
    // Full-page navigation hands the browser to the OIDC flow; the server sets the session cookie
    // and redirects back to returnUrl. Clear any stale API key first so the two paths don't mix.
    setApiKey(null)
    window.location.assign(`${loginUrl}?returnUrl=${encodeURIComponent('/')}`)
  }

  const hasProviders = (providers?.length ?? 0) > 0

  return (
    <div className={s.root}>
      <Card className={s.card}>
        <Title2>Ingest</Title2>
        <div className={s.hint}>
          Paste your API key. Only User-kind credentials can sign in here (any role); Application-kind keys are API-only.
          The bootstrap admin key is printed once in the server logs on first start.
        </div>

        {ssoMessage && (
          <AutoScrollMessageBar intent="error">
            <MessageBarBody>
              <MessageBarTitle>Single sign-on</MessageBarTitle>
              {ssoMessage}
            </MessageBarBody>
          </AutoScrollMessageBar>
        )}

        {hasProviders && (
          <>
            <div className={s.providers}>
              {providers!.map(p => (
                <Button key={p.id} appearance="outline" onClick={() => signInWith(p.loginUrl)}>
                  Continue with {p.displayName}
                </Button>
              ))}
            </div>
            <Divider>or use an API key</Divider>
          </>
        )}

        <form onSubmit={onSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <Field label="API key" required>
            <Input
              type="password"
              value={key}
              onChange={(_, v) => setKey(v.value)}
              placeholder="abc123.xyz..."
              autoFocus
            />
          </Field>
          {error && (
            <AutoScrollMessageBar intent="error">
              <MessageBarBody>
                <MessageBarTitle>Could not sign in</MessageBarTitle>
                {error}
              </MessageBarBody>
            </AutoScrollMessageBar>
          )}
          <Button type="submit" appearance="primary" disabled={!key || busy}>
            {busy ? 'Verifying...' : 'Sign in'}
          </Button>
        </form>
      </Card>
    </div>
  )
}
