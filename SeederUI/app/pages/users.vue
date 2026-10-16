<script setup lang="ts">
// Web users: who may change things from a browser or another system, and
// with which role. Admin only (the FPVTrackside machine counts as Admin
// unless "trust this machine" is switched off).
interface Key { id: string; label: string; prefix: string; created: string }
interface User { id: string; name: string; role: string; disabled: boolean; hasPassword: boolean; created: string; keys: Key[] }
interface Role { key: string; name: string; description: string }
interface Access { trustThisMachine: boolean; trustInEffect: boolean; hasAdmin: boolean; environmentTokenSet: boolean; roles: Role[] }

const { role, via, askSignIn } = useAuth()
const onError = useApiErrors()
const toast = useToast()

const users = ref<User[]>([])
const access = ref<Access | null>(null)
const loading = ref(false)

const roleItems = computed(() => (access.value?.roles ?? []).map(r => ({ label: r.name, value: r.key })))

async function load() {
  if (role.value !== 'admin') return
  loading.value = true
  try {
    const [u, a] = await Promise.all([get<User[]>('/users'), get<Access>('/access')])
    users.value = u
    access.value = a
  } catch (e) {
    onError(e)
  } finally {
    loading.value = false
  }
}
watch(role, load, { immediate: true })

async function run(fn: () => Promise<unknown>, done?: string) {
  try {
    await fn()
    if (done) toast.add({ title: done, color: 'success', icon: 'i-lucide-check' })
  } catch (e) {
    onError(e)
  }
  await load()
}

// Add a user
const newUser = reactive({ name: '', role: 'operator', password: '' })
async function addUser() {
  await run(async () => {
    await post('/users', { ...newUser, password: newUser.password || undefined })
    newUser.name = ''
    newUser.password = ''
  }, 'User added')
}

const setRole = (u: User, r: string) => run(() => patch(`/users/${u.id}`, { role: r }), `${u.name} is now ${r}`)
const setDisabled = (u: User, d: boolean) => run(() => patch(`/users/${u.id}`, { disabled: d }), d ? `${u.name} disabled` : `${u.name} enabled`)
const setTrust = (t: boolean) => run(() => patch('/access', { trustThisMachine: t }))

// Remove a user (confirm first)
const removing = ref<User | null>(null)
const removeUser = () => {
  const u = removing.value!
  removing.value = null
  return run(() => del(`/users/${u.id}`), `${u.name} removed`)
}

// Set a password
const pwUser = ref<User | null>(null)
const pwValue = ref('')
function askPassword(u: User) {
  pwValue.value = ''
  pwUser.value = u
}
const savePassword = () => {
  const u = pwUser.value!
  pwUser.value = null
  return run(() => patch(`/users/${u.id}`, { password: pwValue.value }), `Password set for ${u.name}`)
}

// API keys: the key is shown once, here, and never again
const keyUser = ref<User | null>(null)
const keyLabel = ref('')
const newKey = ref('')
function askKey(u: User) {
  keyLabel.value = ''
  newKey.value = ''
  keyUser.value = u
}
async function makeKey() {
  try {
    const res = await post<{ key: string }>(`/users/${keyUser.value!.id}/keys`, { label: keyLabel.value })
    newKey.value = res.key
  } catch (e) {
    onError(e)
  }
  await load()
}
const removeKey = (u: User, k: Key) => run(() => del(`/users/${u.id}/keys/${k.id}`), 'Key removed')
const copyKey = () => navigator.clipboard?.writeText(newKey.value).then(() => toast.add({ title: 'Copied', color: 'success', icon: 'i-lucide-copy' }))

const roleColor = (r: string) => ({ admin: 'error', operator: 'primary', stages: 'secondary' } as Record<string, any>)[r] ?? 'neutral'
</script>

<template>
  <UContainer class="py-8">
    <div class="mb-6 flex items-center gap-3">
      <UButton to="/" color="neutral" variant="ghost" icon="i-lucide-arrow-left" aria-label="Back" />
      <div>
        <h1 class="font-display text-2xl font-bold">Users</h1>
        <p class="text-sm text-muted">Who may change things from a browser or another system. Anyone on the network can still look.</p>
      </div>
    </div>

    <UCard v-if="role !== 'admin'">
      <div class="flex flex-wrap items-center gap-4">
        <UIcon name="i-lucide-lock" class="size-6 text-muted" />
        <p class="flex-1">Managing users needs the Admin role. Sign in as an Admin, or open this page on the FPVTrackside machine.</p>
        <UButton icon="i-lucide-log-in" label="Sign in" @click="askSignIn()" />
      </div>
    </UCard>

    <div v-else class="grid gap-6">
      <!-- What each role allows -->
      <div class="grid gap-3 sm:grid-cols-3">
        <UCard v-for="r in access?.roles" :key="r.key">
          <UBadge :color="roleColor(r.key)" variant="subtle" :label="r.name" class="mb-2" />
          <p class="text-sm text-muted">{{ r.description }}</p>
        </UCard>
      </div>

      <!-- The users -->
      <UCard>
        <template #header>
          <h2 class="font-display text-lg font-semibold">People and systems</h2>
        </template>
        <p v-if="!users.length && !loading" class="text-muted">No users yet. Add one below, then they can sign in from any browser on the network.</p>
        <ul class="divide-y divide-default">
          <li v-for="u in users" :key="u.id" class="grid gap-3 py-4 first:pt-0 last:pb-0">
            <div class="flex flex-wrap items-center gap-3">
              <UIcon name="i-lucide-user-round" class="size-5 text-muted" />
              <span class="font-semibold" :class="{ 'line-through text-muted': u.disabled }">{{ u.name }}</span>
              <UBadge v-if="!u.hasPassword" color="neutral" variant="subtle" label="Keys only" />
              <USelect :model-value="u.role" :items="roleItems" class="ml-auto w-36" @update:model-value="(v: any) => setRole(u, v)" />
              <USwitch :model-value="!u.disabled" label="Enabled" @update:model-value="(v: boolean) => setDisabled(u, !v)" />
              <UButton color="neutral" variant="outline" icon="i-lucide-key-round" label="Password" @click="askPassword(u)" />
              <UButton color="neutral" variant="outline" icon="i-lucide-plus" label="API key" @click="askKey(u)" />
              <UButton color="error" variant="ghost" icon="i-lucide-trash-2" :aria-label="`Remove ${u.name}`" @click="removing = u" />
            </div>
            <ul v-if="u.keys.length" class="ml-8 grid gap-1">
              <li v-for="k in u.keys" :key="k.id" class="flex items-center gap-3 text-sm">
                <UIcon name="i-lucide-key" class="size-4 text-muted" />
                <span>{{ k.label }}</span>
                <code class="text-muted">{{ k.prefix }}</code>
                <span class="text-muted">{{ new Date(k.created).toLocaleDateString() }}</span>
                <UButton class="ml-auto" size="xs" color="error" variant="ghost" icon="i-lucide-x" :aria-label="`Remove key ${k.label}`" @click="removeKey(u, k)" />
              </li>
            </ul>
          </li>
        </ul>
      </UCard>

      <!-- Add a user -->
      <UCard>
        <template #header>
          <h2 class="font-display text-lg font-semibold">Add a user</h2>
        </template>
        <form class="grid items-end gap-4 sm:grid-cols-[1fr_10rem_1fr_auto]" @submit.prevent="addUser">
          <UFormField label="Name">
            <UInput v-model="newUser.name" class="w-full" />
          </UFormField>
          <UFormField label="Role">
            <USelect v-model="newUser.role" :items="roleItems" class="w-full" />
          </UFormField>
          <UFormField label="Password" hint="8+ characters; blank = API keys only">
            <UInput v-model="newUser.password" type="password" autocomplete="new-password" class="w-full" />
          </UFormField>
          <UButton type="submit" icon="i-lucide-user-plus" label="Add" :disabled="!newUser.name.trim() || (newUser.password.length > 0 && newUser.password.length < 8)" />
        </form>
      </UCard>

      <!-- This machine -->
      <UCard v-if="access">
        <template #header>
          <h2 class="font-display text-lg font-semibold">The FPVTrackside machine</h2>
        </template>
        <USwitch
          :model-value="access.trustThisMachine"
          :disabled="!access.hasAdmin && access.trustThisMachine"
          label="People on the FPVTrackside machine are Admin without signing in"
          @update:model-value="setTrust"
        />
        <p class="mt-3 text-sm text-muted">
          Switch this off when other people use the race laptop's browser. It needs an Admin user with a password first, so nobody gets locked out.
          Web pages from other websites never count as this machine, whatever this is set to.
        </p>
        <p v-if="!access.trustThisMachine && via === 'this-computer'" class="mt-2 text-sm text-warning">Saved — this browser needs to sign in from now on.</p>
        <p v-if="access.environmentTokenSet" class="mt-3 text-sm text-muted">
          The <code>FPV_API_TOKEN</code> environment variable is set on this machine. It works as an Admin key; use a user's API key instead where you can.
        </p>
      </UCard>
    </div>

    <!-- Confirm remove -->
    <UModal :open="!!removing" title="Remove user?" :description="`${removing?.name} won't be able to sign in, and their API keys stop working straight away.`" @update:open="(o: boolean) => { if (!o) removing = null }">
      <template #footer>
        <div class="flex w-full justify-end gap-2">
          <UButton color="neutral" variant="ghost" label="Cancel" @click="removing = null" />
          <UButton color="error" icon="i-lucide-trash-2" label="Remove" @click="removeUser" />
        </div>
      </template>
    </UModal>

    <!-- Set password -->
    <UModal :open="!!pwUser" :title="`Password for ${pwUser?.name}`" description="They'll be signed out everywhere and use this from now on." @update:open="(o: boolean) => { if (!o) pwUser = null }">
      <template #body>
        <form id="setpw" @submit.prevent="savePassword">
          <UFormField label="New password" hint="At least 8 characters">
            <UInput v-model="pwValue" type="password" autocomplete="new-password" autofocus class="w-full" />
          </UFormField>
        </form>
      </template>
      <template #footer>
        <div class="flex w-full justify-end gap-2">
          <UButton color="neutral" variant="ghost" label="Cancel" @click="pwUser = null" />
          <UButton type="submit" form="setpw" label="Set password" :disabled="pwValue.length < 8" />
        </div>
      </template>
    </UModal>

    <!-- New API key -->
    <UModal :open="!!keyUser" :title="`API key for ${keyUser?.name}`" :description="newKey ? 'Copy it now: it is shown only once.' : `For a script or another system. It acts with ${keyUser?.name}'s role.`" @update:open="(o: boolean) => { if (!o) keyUser = null }">
      <template #body>
        <div v-if="newKey" class="grid gap-3">
          <code class="break-all rounded-lg bg-elevated p-3 font-mono text-sm">{{ newKey }}</code>
          <p class="text-sm text-muted">Send it as <code>Authorization: Bearer &lt;key&gt;</code>.</p>
        </div>
        <form v-else id="mkkey" @submit.prevent="makeKey">
          <UFormField label="What is it for?">
            <UInput v-model="keyLabel" placeholder="Club dashboard" autofocus class="w-full" />
          </UFormField>
        </form>
      </template>
      <template #footer>
        <div class="flex w-full justify-end gap-2">
          <template v-if="newKey">
            <UButton color="neutral" variant="outline" icon="i-lucide-copy" label="Copy" @click="copyKey" />
            <UButton label="Done" @click="keyUser = null" />
          </template>
          <template v-else>
            <UButton color="neutral" variant="ghost" label="Cancel" @click="keyUser = null" />
            <UButton type="submit" form="mkkey" icon="i-lucide-key-round" label="Make key" />
          </template>
        </div>
      </template>
    </UModal>
  </UContainer>
</template>
