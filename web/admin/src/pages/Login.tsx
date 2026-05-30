import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button, Card, Field, Input, MessageBarBody, MessageBarTitle, Title2, makeStyles, tokens } from '@fluentui/react-components'
import { AutoScrollMessageBar } from '../components/AutoScrollMessageBar'
import { formatApiError, setApiKey } from '../api/client'
import { api } from '../api/client'
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
})

export function Login() {
  const s = useStyles()
  const nav = useNavigate()
  const [key, setKey] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

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

  return (
    <div className={s.root}>
      <Card className={s.card}>
        <Title2>Ingest</Title2>
        <div className={s.hint}>
          Paste your API key. Only User-kind credentials can sign in here (any role); Application-kind keys are API-only.
          The bootstrap admin key is printed once in the server logs on first start.
        </div>
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
