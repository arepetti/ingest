/**
 * Tiny helpers for the "download as JSON" and "upload JSON" affordances used by the schema
 * import/export workflow. Kept dependency-free so they're usable from any page.
 */

/**
 * Trigger a browser download for `payload`, JSON-stringified with two-space indentation. The
 * Blob is revoked on the next tick to avoid leaking — once the download dialog appears the
 * browser has its own copy.
 */
export function downloadJson(filename: string, payload: unknown): void {
  downloadText(filename, JSON.stringify(payload, null, 2), 'application/json')
}

/**
 * Trigger a browser download for an arbitrary string payload. Same lifecycle as `downloadJson`
 * but without any JSON shaping — the caller controls the body and the mime type. Used by the
 * report viewer to save the rendered HTML straight from the SPA, no server round-trip needed.
 */
export function downloadText(filename: string, content: string, mimeType: string = 'text/plain'): void {
  const blob = new Blob([content], { type: mimeType })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  setTimeout(() => URL.revokeObjectURL(url), 0)
}

/**
 * Prompt the user to pick a `.json` file and resolve with its parsed contents. Rejects when
 * the user cancels the picker, when the file isn't valid UTF-8, or when the body fails to
 * parse as JSON — callers should surface those rejections inline.
 */
export function pickJsonFile(): Promise<unknown> {
  return new Promise((resolve, reject) => {
    const input = document.createElement('input')
    input.type = 'file'
    input.accept = 'application/json,.json'
    input.style.display = 'none'

    let settled = false
    const finish = (cb: () => void) => {
      if (settled) return
      settled = true
      cb()
      // Reset the input so picking the same file twice still triggers `change`.
      input.value = ''
      input.remove()
    }

    input.addEventListener('change', () => {
      const file = input.files?.[0]
      if (!file) return finish(() => reject(new Error('No file selected.')))
      const reader = new FileReader()
      reader.onerror = () => finish(() => reject(reader.error ?? new Error('Failed to read file.')))
      reader.onload = () => {
        try {
          const parsed = JSON.parse(String(reader.result))
          finish(() => resolve(parsed))
        } catch (e) {
          finish(() => reject(e))
        }
      }
      reader.readAsText(file)
    })

    // Some browsers fire `focus` on the document when the picker is dismissed without a
    // selection. Use that as a soft "user cancelled" signal; harmless if the change handler
    // ran first because of the `settled` guard.
    const onFocus = () => {
      window.removeEventListener('focus', onFocus)
      setTimeout(() => finish(() => reject(new Error('No file selected.'))), 300)
    }
    window.addEventListener('focus', onFocus, { once: true })

    document.body.appendChild(input)
    input.click()
  })
}

/**
 * Prompt the user to pick a text-ish file (default: `.html`) and resolve with `{ fileName,
 * content }`. Used by the report uploader — reports are HTML files with a YAML front matter,
 * so the picker accepts `.html`, `.liquid`, `.txt` and `.htm`.
 */
export function pickTextFile(accept: string = '.html,.liquid,.htm,.txt,text/html'): Promise<{ fileName: string; content: string }> {
  return new Promise((resolve, reject) => {
    const input = document.createElement('input')
    input.type = 'file'
    input.accept = accept
    input.style.display = 'none'

    let settled = false
    const finish = (cb: () => void) => {
      if (settled) return
      settled = true
      cb()
      input.value = ''
      input.remove()
    }

    input.addEventListener('change', () => {
      const file = input.files?.[0]
      if (!file) return finish(() => reject(new Error('No file selected.')))
      const reader = new FileReader()
      reader.onerror = () => finish(() => reject(reader.error ?? new Error('Failed to read file.')))
      reader.onload = () => finish(() => resolve({ fileName: file.name, content: String(reader.result ?? '') }))
      reader.readAsText(file)
    })

    const onFocus = () => {
      window.removeEventListener('focus', onFocus)
      setTimeout(() => finish(() => reject(new Error('No file selected.'))), 300)
    }
    window.addEventListener('focus', onFocus, { once: true })

    document.body.appendChild(input)
    input.click()
  })
}
