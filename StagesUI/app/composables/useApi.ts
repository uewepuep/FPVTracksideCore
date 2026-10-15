// Every call the page makes goes through FPVTrackside's REST API (/api/v1) —
// nothing here that a script couldn't do too. The sign-in token is shared with
// the API console (same localStorage key), so signing in on either signs in both.

export class ApiError extends Error {
  constructor(message: string, public status: number, public code?: string) {
    super(message)
  }
}

const TOKEN_KEY = 'fpvapi.token'

export function token(): string {
  try {
    return JSON.parse(localStorage.getItem(TOKEN_KEY) || '""') || ''
  } catch {
    return ''
  }
}

export function setToken(t: string) {
  try {
    if (t) localStorage.setItem(TOKEN_KEY, JSON.stringify(t))
    else localStorage.removeItem(TOKEN_KEY)
  } catch {
    // private window: the sign-in lasts until the page closes
  }
}

export async function api<T = any>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  const t = token()
  if (t) headers.Authorization = `Bearer ${t}`
  const init: RequestInit = { method, headers, cache: 'no-store' }
  if (method !== 'GET') {
    headers['Content-Type'] = 'application/json'
    init.body = JSON.stringify(body ?? {})
  }
  let res: Response
  try {
    res = await fetch(`/api/v1${path}`, init)
  } catch {
    throw new ApiError('FPVTrackside isn\'t answering — is it still running?', 0)
  }
  const text = await res.text()
  let data: any = null
  try {
    data = text ? JSON.parse(text) : null
  } catch {
    // not JSON
  }
  if (!res.ok) throw new ApiError(data?.error || `${res.status} ${res.statusText}`, res.status, data?.code)
  return data as T
}

export const get = <T = any>(path: string) => api<T>('GET', path)
export const post = <T = any>(path: string, body?: unknown) => api<T>('POST', path, body)
export const patch = <T = any>(path: string, body?: unknown) => api<T>('PATCH', path, body)
export const del = <T = any>(path: string) => api<T>('DELETE', path)

// Surface an API failure as a toast, with the fix for the common case.
export function useApiErrors() {
  const toast = useToast()
  const { askSignIn } = useAuth()
  return (e: unknown) => {
    const err = e as ApiError
    if (err?.status === 401) {
      toast.add({
        title: 'Sign in to do that',
        description: err.message,
        color: 'warning',
        icon: 'i-lucide-lock',
        actions: [{ label: 'Sign in', icon: 'i-lucide-log-in', onClick: () => askSignIn() }],
      })
      return
    }
    toast.add({
      title: err?.status === 403 ? 'Your role doesn\'t allow that' : 'Couldn\'t do that',
      description: err?.status === 403 ? `${err.message}. Ask an Admin to change your role.` : err?.message || String(e),
      color: 'error',
      icon: 'i-lucide-triangle-alert',
    })
  }
}
