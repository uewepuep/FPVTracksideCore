// Every call the page makes goes through FPVTrackside's REST API (/api/v1) —
// nothing here that a script couldn't do too. The optional API token is the
// one saved by the API console (only needed for changes from another machine).

export class ApiError extends Error {
  constructor(message: string, public status: number, public code?: string) {
    super(message)
  }
}

function token(): string {
  try {
    return JSON.parse(localStorage.getItem('fpvapi.token') || '""') || ''
  } catch {
    return ''
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

// Surface an API failure as a toast, with the fix for the common case.
export function useApiErrors() {
  const toast = useToast()
  return (e: unknown) => {
    const err = e as ApiError
    const auth = err?.status === 401 || err?.status === 403
    toast.add({
      title: auth ? 'Changes need permission' : 'Couldn\'t do that',
      description: auth
        ? `${err.message}. Open this page on the FPVTrackside machine, or add the API token in the API console.`
        : err?.message || String(e),
      color: 'error',
      icon: 'i-lucide-triangle-alert',
    })
  }
}
