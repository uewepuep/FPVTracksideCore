<script setup lang="ts">
import type { Bracket, Heat } from '~/composables/types'

const route = useRoute()
const id = computed(() => Number(route.params.id))
const onError = useApiErrors()
const toast = useToast()
const { liveRaceId, refresh: refreshLive } = useLive()

const bracket = ref<Bracket | null>(null)
const view = ref<'heats' | 'diagram' | 'standings'>('heats')
const main = ref('all')
const busy = ref<string | null>(null)
let sig = ''

async function load(quiet = false) {
  try {
    const b = await get<Bracket>(`/brackets/${id.value}`)
    const s = JSON.stringify(b)
    if (quiet && s === sig) return
    sig = s
    bracket.value = b
  } catch (e) {
    if (!quiet) onError(e)
  }
}
let timer: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  load()
  timer = setInterval(() => { if (!document.hidden && !resultOpen.value) load(true) }, 3000)
})
onBeforeUnmount(() => clearInterval(timer))

function setBracket(b: Bracket) {
  sig = JSON.stringify(b)
  bracket.value = b
}

async function act<T>(key: string, fn: () => Promise<T>, done?: (r: T) => void) {
  busy.value = key
  try {
    const r = await fn()
    done?.(r)
  } catch (e) {
    onError(e)
  } finally {
    busy.value = null
  }
}

const mains = computed(() => [...new Set((bracket.value?.heats ?? []).map(h => h.id.includes(':') ? h.id.split(':')[0]! : '').filter(Boolean))].sort())
const heats = computed(() => (bracket.value?.heats ?? []).filter(h => main.value === 'all' || h.id.startsWith(`${main.value}:`)))
const ready = computed(() => (bracket.value?.heats ?? []).filter(h => h.state === 'ready').length)
const rounds = computed(() => {
  const groups = new Map<string, Heat[]>()
  for (const h of heats.value) {
    const m = h.id.includes(':') && main.value === 'all' ? `${h.id.split(':')[0]} Main · ` : ''
    const k = `${m}Round ${h.round}`
    if (!groups.has(k)) groups.set(k, [])
    groups.get(k)!.push(h)
  }
  return [...groups.entries()]
})
const diagrams = computed(() => (bracket.value?.diagrams ?? []).filter(d => main.value === 'all' || d.title?.startsWith(main.value)))

const heatPath = (h: string) => `/brackets/${id.value}/heats/${encodeURIComponent(h)}`

function sendAll() {
  act('send', () => post(`/brackets/${id.value}/send`, {}), (r: any) => {
    const probs = Object.entries(r.problems ?? {})
    toast.add({
      title: `${r.sent.length} heat${r.sent.length === 1 ? '' : 's'} sent to FPVTrackside`,
      description: probs.length ? `${probs[0]![0]}: ${probs[0]![1]}` : undefined,
      color: probs.length ? 'warning' : 'success',
      icon: 'i-lucide-send',
    })
    setBracket(r.bracket)
  })
}
function sync() {
  act('sync', () => post(`/brackets/${id.value}/sync`, {}), (r: any) => {
    const probs = Object.entries(r.problems ?? {})
    toast.add({
      title: `${r.applied.length} result${r.applied.length === 1 ? '' : 's'} pulled from FPVTrackside`,
      description: probs.length ? `${probs[0]![0]}: ${probs[0]![1]}` : undefined,
      color: probs.length ? 'warning' : 'success',
      icon: 'i-lucide-refresh-cw',
    })
    setBracket(r.bracket)
  })
}
function setAutoSend(v: boolean) {
  act('autosend', () => api('PATCH', `/brackets/${id.value}`, { autoSend: v }), (b: Bracket) => {
    setBracket(b)
    if (v) toast.add({ title: 'Heats will be sent to FPVTrackside automatically', description: 'Each heat is created as soon as its pilots are known.', icon: 'i-lucide-send', color: 'success' })
  })
}
function setAuto(v: boolean) {
  act('auto', () => api('PATCH', `/brackets/${id.value}`, { autoResults: v }), setBracket)
}
function send(h: Heat, makeCurrent: boolean) {
  act(h.id, () => post(heatPath(h.id) + '/send', { makeCurrent }), (b: Bracket) => {
    setBracket(b)
    refreshLive()
    toast.add({ title: makeCurrent ? `${h.id} is on the race-director screen` : `${h.id} sent to FPVTrackside`, color: 'success', icon: 'i-lucide-check' })
  })
}
function reset(h: Heat) {
  if (!confirm(`Clear the result of ${h.id}? Its pilots are taken back out of later heats.`)) return
  act(h.id, () => post(heatPath(h.id) + '/reset', {}), setBracket)
}
function lock(h: Heat, locked: boolean) {
  act(h.id, () => post(heatPath(h.id) + '/lock', { locked }), setBracket)
}

const resultOpen = ref(false)
const resultHeat = ref<Heat | null>(null)
const resultModal = ref<{ fail: (m: string) => void } | null>(null)
function openResult(h: Heat) {
  resultHeat.value = h
  resultOpen.value = true
}
async function saveResult(placement: string[], lockIt: boolean) {
  const h = resultHeat.value!
  try {
    setBracket(await post(heatPath(h.id) + '/result', { placement, lock: lockIt }))
    resultOpen.value = false
    toast.add({ title: `${h.id} result saved`, color: 'success', icon: 'i-lucide-check' })
  } catch (e: any) {
    resultModal.value?.fail(e.message)
  }
}

async function goToHeat(heatId: string) {
  view.value = 'heats'
  await nextTick()
  const el = document.getElementById(`heat-${heatId}`)
  el?.scrollIntoView({ behavior: 'smooth', block: 'center' })
}

async function remove() {
  if (!bracket.value || !confirm(`Delete "${bracket.value.name}"? Races already sent stay in FPVTrackside.`)) return
  act('delete', () => api('DELETE', `/brackets/${id.value}`), () => navigateTo('/'))
}

const tabs = computed(() => [
  { label: 'Heats', value: 'heats', icon: 'i-lucide-layout-grid' },
  ...(bracket.value?.diagrams.length ? [{ label: 'Diagram', value: 'diagram', icon: 'i-lucide-git-fork' }] : []),
  { label: 'Standings', value: 'standings', icon: 'i-lucide-list-ordered' },
])
const standingColumns = [
  { accessorKey: 'rank', header: '#' },
  { accessorKey: 'name', header: 'Pilot' },
  { accessorKey: 'detail', header: '' },
]
</script>

<template>
  <div v-if="!bracket">
    <UContainer class="py-10"><USkeleton class="h-40 rounded-xl" /></UContainer>
  </div>
  <div v-else>
    <ArtImage :name="`format-${bracket.format}`" class="border-b border-default">
      <div class="absolute inset-0 bg-gradient-to-r from-[var(--ui-bg)] via-[var(--ui-bg)]/85 to-[var(--ui-bg)]/40" />
      <UContainer class="relative py-8">
        <UButton to="/" variant="link" color="neutral" icon="i-lucide-arrow-left" label="Brackets" class="mb-2 px-0" />
        <div class="flex flex-wrap items-end gap-4">
          <div class="min-w-0 flex-1">
            <UBadge :label="bracket.formatName" color="primary" variant="subtle" />
            <h1 class="mt-2 text-3xl font-bold sm:text-4xl">{{ bracket.name }}</h1>
            <p class="mt-1 text-muted">{{ bracket.pilotCount }} pilots · {{ bracket.heatsDone }} of {{ bracket.heatCount }} heats run</p>
          </div>
          <div v-if="bracket.champion" class="flex items-center gap-3 rounded-xl bg-warning/10 px-4 py-3 ring-1 ring-warning/40">
            <UIcon name="i-lucide-trophy" class="size-8 text-warning" />
            <div>
              <p class="text-xs uppercase tracking-widest text-warning">Champion</p>
              <p class="font-display text-xl font-bold">{{ bracket.champion }}</p>
            </div>
          </div>
        </div>
        <UProgress :model-value="bracket.heatCount ? (100 * bracket.heatsDone) / bracket.heatCount : 0" class="mt-5 max-w-xl" />
      </UContainer>
    </ArtImage>

    <UContainer class="py-6">
      <div class="mb-5 flex flex-wrap items-center gap-2">
        <UButton icon="i-lucide-send" :label="`Send ${ready} ready heat${ready === 1 ? '' : 's'} to FPVTrackside`" :disabled="!ready" :loading="busy === 'send'" @click="sendAll" />
        <UButton color="neutral" variant="outline" icon="i-lucide-refresh-cw" label="Sync results" :loading="busy === 'sync'" @click="sync" />
        <USwitch :model-value="bracket.autoSend" label="Send heats to FPVTrackside automatically" class="ml-1" @update:model-value="setAutoSend" />
        <USwitch :model-value="bracket.autoResults" label="Results from FPVTrackside automatically" class="ml-1" @update:model-value="setAuto" />
        <span class="flex-1" />
        <UButton color="error" variant="ghost" icon="i-lucide-trash-2" label="Delete" :loading="busy === 'delete'" @click="remove" />
      </div>

      <UTabs v-model="view" :items="tabs" :content="false" class="mb-5" />

      <div v-if="mains.length > 1 && view !== 'standings'" class="mb-5 flex flex-wrap gap-2">
        <UButton v-for="m in ['all', ...mains]" :key="m" size="sm" :color="main === m ? 'primary' : 'neutral'" :variant="main === m ? 'solid' : 'outline'" :label="m === 'all' ? 'All mains' : `${m} Main`" @click="main = m" />
      </div>

      <!-- Heats, a column per round -->
      <div v-if="view === 'heats'" class="flex gap-4 overflow-x-auto pb-4 max-md:flex-col">
        <section v-for="[label, list] in rounds" :key="label" class="w-full shrink-0 md:w-80">
          <h2 class="mb-2 text-xs font-bold uppercase tracking-widest text-muted">{{ label }}</h2>
          <div class="grid gap-3">
            <HeatCard
              v-for="h in list" :key="h.id" :heat="h"
              :live="!!h.fpvRaceId && h.fpvRaceId === liveRaceId"
              @send="send(h, $event)" @result="openResult(h)" @reset="reset(h)" @lock="lock(h, $event)"
            />
          </div>
        </section>
      </div>

      <!-- Diagram -->
      <div v-else-if="view === 'diagram'" class="grid gap-5">
        <UCard v-for="d in diagrams" :key="d.title ?? 'd'" :title="d.title ?? undefined">
          <BracketDiagram :diagram="d" :heats="bracket.heats" :live-race-id="liveRaceId" @open="goToHeat" />
        </UCard>
      </div>

      <!-- Standings -->
      <UCard v-else>
        <UTable v-if="bracket.standings.length" :data="bracket.standings" :columns="standingColumns">
          <template #name-cell="{ row }">
            <span class="font-semibold">{{ row.original.name }}</span>
            <UIcon v-if="row.original.rank === 1 && bracket.champion" name="i-lucide-trophy" class="ml-1.5 align-middle text-warning" />
            <UBadge v-if="row.original.tied" label="not decided on track" size="sm" color="neutral" variant="subtle" class="ml-2" />
          </template>
        </UTable>
        <UEmpty v-else icon="i-lucide-list-ordered" title="No standings yet" description="They fill in as heats are run." />
      </UCard>
    </UContainer>

    <ResultModal ref="resultModal" v-model:open="resultOpen" :heat="resultHeat" @save="saveResult" />
  </div>
</template>
