// Who this browser is signed in as. FPVTrackside answers that on every poll
// of GET /api/v1 (role, signedInAs, signedInVia), so this only adds signing in
// and out, and the sign-in dialog's open state.

const signInOpen = ref(false)

export function useAuth() {
  const { info, refresh } = useLive()

  const role = computed(() => info.value?.role ?? 'none')
  const name = computed(() => info.value?.signedInAs ?? '')
  const via = computed(() => info.value?.signedInVia ?? 'anonymous')
  const rank = (r: string) => ({ none: 0, seeder: 1, operator: 2, admin: 3 } as Record<string, number>)[r] ?? 0
  const can = (needed: 'seeder' | 'operator' | 'admin') => rank(role.value) >= rank(needed)

  async function signIn(user: string, password: string) {
    const res = await post<{ token: string }>('/auth/login', { name: user, password })
    setToken(res.token)
    await refresh()
  }

  async function signOut() {
    try {
      await post('/auth/logout')
    } catch {
      // already gone
    }
    setToken('')
    await refresh()
  }

  const askSignIn = () => { signInOpen.value = true }

  return { role, name, via, can, signIn, signOut, signInOpen, askSignIn }
}
