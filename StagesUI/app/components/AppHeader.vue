<script setup lang="ts">
// Top bar: brand, the open event, what the race-director screen is doing.
const { info, control, reachable } = useLive()

const raceLabel = computed(() => {
  const c = control.value
  if (!c) return ''
  const name = c.currentRace?.name ?? 'No race selected'
  if (c.state === 'running') {
    const s = c.elapsedSeconds
    return `${name} · ${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`
  }
  return `${name} · ${c.state}`
})
const { role, name, via, signOut, askSignIn } = useAuth()
const roleLabel = computed(() => ({ stages: 'Stages', operator: 'Operator', admin: 'Admin' } as Record<string, string>)[role.value] ?? 'Read only')
const userMenu = computed(() => [
  [{ type: 'label' as const, label: `${name.value} · ${roleLabel.value}` }],
  [
    ...(role.value === 'admin' ? [{ label: 'Users', icon: 'i-lucide-users', to: '/users' }] : []),
    ...(via.value === 'session' ? [{ label: 'Sign out', icon: 'i-lucide-log-out', onSelect: () => signOut() }] : []),
    ...(via.value === 'this-computer' ? [{ label: 'Sign in as someone else', icon: 'i-lucide-log-in', onSelect: () => askSignIn() }] : []),
  ],
])

const raceColor = computed(() => ({ running: 'error', starting: 'warning', finished: 'success' } as const)[control.value?.state as 'running'] ?? 'neutral')
</script>

<template>
  <header class="sticky top-0 z-40 border-b border-default bg-default/80 backdrop-blur">
    <UContainer class="flex h-14 items-center gap-3">
      <NuxtLink to="/" class="flex items-center gap-2 font-display text-lg font-bold">
        <span class="grid size-8 place-items-center rounded-lg bg-primary text-inverted">
          <UIcon name="i-lucide-trophy" class="size-5" />
        </span>
        <span>Stages</span>
        <span class="hidden text-sm font-normal text-muted sm:inline">· FPVTrackside</span>
      </NuxtLink>

      <div class="flex min-w-0 flex-1 items-center gap-2 overflow-hidden">
        <UBadge v-if="!reachable" color="error" variant="subtle" icon="i-lucide-wifi-off" label="FPVTrackside not reachable" />
        <UBadge v-else-if="info" color="primary" variant="subtle" icon="i-lucide-calendar" :label="info.eventName" class="hidden truncate md:inline-flex" />
        <UBadge v-if="control" :color="raceColor" variant="subtle" icon="i-lucide-radio" :label="raceLabel" class="truncate" :class="{ 'is-live': control.state === 'running' }" />
      </div>

      <UButton to="/api/v1/docs" external target="_blank" variant="ghost" color="neutral" icon="i-lucide-braces" label="API" class="hidden sm:inline-flex" />
      <UButton v-if="role === 'none'" color="neutral" variant="outline" icon="i-lucide-log-in" label="Sign in" @click="askSignIn()" />
      <UDropdownMenu v-else :items="userMenu">
        <UButton color="neutral" variant="ghost" :icon="via === 'this-computer' ? 'i-lucide-monitor' : 'i-lucide-user-round'" :label="name" class="max-w-40 truncate" />
      </UDropdownMenu>
      <UColorModeButton />
    </UContainer>
  </header>
</template>
