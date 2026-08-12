import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Button, Card, Divider, Field, Input, MessageBarBody, MessageBarTitle, Title2, makeStyles, tokens } from '@fluentui/react-components'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { formatApiError, setApiKey } from '../api/client'
import { api } from '../api/client'
import { useAuthProviders } from '../api/hooks'
import type { Me } from '../api/types'
import { useTranslation } from 'react-i18next'

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
const SSO_ERROR_KEYS: Record<string, string> = {
  not_linked: 'notLinked',
  no_email: 'noEmail',
  remote: 'remote',
}

export function Login() {
  const s = useStyles()
  const { t } = useTranslation()
  const nav = useNavigate()
  const [params] = useSearchParams()
  const [key, setKey] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const { data: providers } = useAuthProviders()
  const ssoError = params.get('sso_error')
  const ssoMessage = ssoError
    ? t(`shell.login.sso.errors.${SSO_ERROR_KEYS[ssoError] ?? 'default'}`)
    : null

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
        setError(t('shell.login.applicationKeyError'))
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
          {t('shell.login.hint')}
        </div>

        {ssoMessage && (
          <AutoScrollMessageBar intent="error">
            <MessageBarBody>
              <MessageBarTitle>{t('shell.login.sso.title')}</MessageBarTitle>
              {ssoMessage}
            </MessageBarBody>
          </AutoScrollMessageBar>
        )}

        {hasProviders && (
          <>
            <div className={s.providers}>
              {providers!.map(p => (
                <Button key={p.id} appearance="outline" onClick={() => signInWith(p.loginUrl)}>
                  {t('shell.login.continueWith', { provider: p.displayName })}
                </Button>
              ))}
            </div>
            <Divider>{t('shell.login.orUseApiKey')}</Divider>
          </>
        )}

        <form onSubmit={onSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <Field label={t('shell.login.apiKey')} required>
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
                <MessageBarTitle>{t('shell.login.couldNotSignIn')}</MessageBarTitle>
                {error}
              </MessageBarBody>
            </AutoScrollMessageBar>
          )}
          <Button type="submit" appearance="primary" disabled={!key || busy}>
            {busy ? t('shell.login.verifying') : t('shell.login.signIn')}
          </Button>
        </form>
      </Card>
    </div>
  )
}
