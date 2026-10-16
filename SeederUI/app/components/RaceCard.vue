<script setup lang="ts">
import type { Race } from '~/composables/types'

const props = defineProps<{ race: Race; live: boolean }>()
const emit = defineEmits<{ send: [makeCurrent: boolean]; result: []; reset: []; lock: [locked: boolean] }>()

const STATE: Record<string, { label: string; color: 'neutral' | 'primary' | 'secondary' | 'error' | 'warning' | 'success' }> = {
  waiting: { label: 'Waiting', color: 'neutral' },
  ready: { label: 'Ready', color: 'primary' },
  sent: { label: 'In FPVTrackside', color: 'secondary' },
  running: { label: 'Racing', color: 'error' },
  flown: { label: 'Flown', color: 'warning' },
  finished: { label: 'Done', color: 'success' },
  void: { label: 'Not needed', color: 'neutral' },
}

const finished = computed(() => props.race.state === 'finished')
const winners = computed(() => new Set(props.race.winners))
// Finish order for the place numbers (winners, then the rest).
const place = computed(() => {
  const m = new Map<string, number>()
  if (finished.value) props.race.winners.concat(props.race.losers).forEach((n, i) => m.set(n, i + 1))
  return m
})
const state = computed(() => (props.live ? { label: 'On screen', color: 'error' as const } : STATE[props.race.state]!))
</script>

<template>
  <UCard
    :id="`race-${race.id}`"
    class="scroll-mt-24"
    :class="{ 'opacity-50': race.state === 'void', 'is-live ring-2 ring-error': live }"
    :ui="{ body: 'p-3 sm:p-4' }"
  >
    <div class="flex items-start justify-between gap-2">
      <div class="min-w-0">
        <p class="truncate font-display font-bold">{{ race.title || race.id }}</p>
        <p class="truncate font-mono text-xs text-muted">{{ race.id }}<span v-if="race.fpvRaceName"> · {{ race.fpvRaceName }}</span></p>
      </div>
      <UBadge :color="state.color" variant="subtle" :label="state.label" class="shrink-0" />
    </div>

    <p v-if="race.note" class="mt-2 text-xs text-muted">{{ race.note }}</p>

    <ol v-if="race.pilots.length" class="mt-3 grid gap-1">
      <li v-for="p in race.pilots" :key="p.pilotId" class="grid grid-cols-[1.5rem_minmax(0,1fr)_auto_auto] items-center gap-2 text-sm">
        <span class="font-mono text-xs font-bold text-muted">{{ place.get(p.name) ?? '' }}</span>
        <span class="truncate" :class="finished ? (winners.has(p.name) ? 'font-semibold text-success' : 'text-muted') : ''">
          {{ p.name }}<UIcon v-if="finished && winners.has(p.name)" name="i-lucide-arrow-up-right" class="ml-1 size-3.5 align-middle" />
        </span>
        <span class="font-mono text-xs text-muted">#{{ p.seed }}</span>
        <UBadge v-if="p.channel" :label="p.channel" size="sm" color="neutral" variant="outline" class="font-mono" />
        <span v-else />
      </li>
    </ol>

    <div class="mt-3 flex flex-wrap gap-1.5">
      <template v-if="race.state === 'ready' || race.state === 'sent'">
        <UButton size="xs" icon="i-lucide-monitor-play" :label="race.state === 'sent' ? 'Select' : 'Send & select'" @click="emit('send', true)" />
        <UButton v-if="race.state === 'ready'" size="xs" color="neutral" variant="outline" icon="i-lucide-send" label="Send" @click="emit('send', false)" />
      </template>
      <UButton
        v-if="race.pilots.length && race.state !== 'void' && race.state !== 'waiting'"
        size="xs" color="neutral" variant="outline" icon="i-lucide-list-ordered"
        :label="finished ? 'Edit result' : 'Enter result'"
        @click="emit('result')"
      />
      <UButton v-if="finished && race.canEdit" size="xs" color="neutral" variant="ghost" icon="i-lucide-rotate-ccw" label="Reset" @click="emit('reset')" />
      <UTooltip v-if="finished || race.state === 'flown'" :text="race.manualLock ? 'Entered by hand — FPVTrackside timing won\'t change it. Click to hand it back to timing.' : 'Lock this result against FPVTrackside timing'">
        <UButton size="xs" color="neutral" variant="ghost" :icon="race.manualLock ? 'i-lucide-lock' : 'i-lucide-lock-open'" :aria-label="race.manualLock ? 'Unlock result' : 'Lock result'" @click="emit('lock', !race.manualLock)" />
      </UTooltip>
    </div>
  </UCard>
</template>
