<script setup lang="ts">
import type { Stage, Race } from '~/composables/types'

const route = useRoute()
const id = computed(() => Number(route.params.id))
const onError = useApiErrors()
const toast = useToast()
const { liveRaceId, refresh: refreshLive } = useLive()

const stage = ref<Stage | null>(null)
const view = ref<'races' | 'diagram' | 'standings'>('races')
const main = ref('all')
const busy = ref<string | null>(null)
let sig = ''

async function load(quiet = false) {
  try {
    const b = await get<Stage>(`/stages/${id.value}`)
    const s = JSON.stringify(b)
    if (quiet && s === sig) return
    sig = s
    stage.value = b
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

function setStage(b: Stage) {
  sig = JSON.stringify(b)
  stage.value = b
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

const mains = computed(() => [...new Set((stage.value?.races ?? []).map(h => h.id.includes(':') ? h.id.split(':')[0]! : '').filter(Boolean))].sort())
const races = computed(() => (stage.value?.races ?? []).filter(h => main.value === 'all' || h.id.startsWith(`${main.value}:`)))
const ready = computed(() => (stage.value?.races ?? []).filter(h => h.state === 'ready').length)
const rounds = computed(() => {
  const groups = new Map<string, Race[]>()
  for (const h of races.value) {
    const m = h.id.includes(':') && main.value === 'all' ? `Bracket ${h.id.split(':')[0]} · ` : ''
    const k = `${m}Round ${h.round}`
    if (!groups.has(k)) groups.set(k, [])
    groups.get(k)!.push(h)
  }
  return [...groups.entries()]
})
const diagrams = computed(() => (stage.value?.diagrams ?? []).filter(d => main.value === 'all' || d.title === `Bracket ${main.value}`))

const racePath = (h: string) => `/stages/${id.value}/races/${encodeURIComponent(h)}`

function sendAll() {
  act('send', () => post(`/stages/${id.value}/send`, {}), (r: any) => {
    const probs = Object.entries(r.problems ?? {})
    toast.add({
      title: `${r.sent.length} race${r.sent.length === 1 ? '' : 's'} sent to FPVTrackside`,
      description: probs.length ? `${probs[0]![0]}: ${probs[0]![1]}` : undefined,
      color: probs.length ? 'warning' : 'success',
      icon: 'i-lucide-send',
    })
    setStage(r.stage)
  })
}
function sync() {
  act('sync', () => post(`/stages/${id.value}/sync`, {}), (r: any) => {
    const probs = Object.entries(r.problems ?? {})
    toast.add({
      title: `${r.applied.length} result${r.applied.length === 1 ? '' : 's'} pulled from FPVTrackside`,
      description: probs.length ? `${probs[0]![0]}: ${probs[0]![1]}` : undefined,
      color: probs.length ? 'warning' : 'success',
      icon: 'i-lucide-refresh-cw',
    })
    setStage(r.stage)
  })
}
function setAutoSend(v: boolean) {
  act('autosend', () => api('PATCH', `/stages/${id.value}`, { autoSend: v }), (b: Stage) => {
    setStage(b)
    if (v) toast.add({ title: 'Races will be sent to FPVTrackside automatically', description: 'Each race is created as soon as its pilots are known.', icon: 'i-lucide-send', color: 'success' })
  })
}
function setAuto(v: boolean) {
  act('auto', () => api('PATCH', `/stages/${id.value}`, { autoResults: v }), setStage)
}
function send(h: Race, makeCurrent: boolean) {
  act(h.id, () => post(racePath(h.id) + '/send', { makeCurrent }), (b: Stage) => {
    setStage(b)
    refreshLive()
    toast.add({ title: makeCurrent ? `${h.id} is on the race-director screen` : `${h.id} sent to FPVTrackside`, color: 'success', icon: 'i-lucide-check' })
  })
}
function reset(h: Race) {
  if (!confirm(`Clear the result of ${h.id}? Its pilots are taken back out of later races.`)) return
  act(h.id, () => post(racePath(h.id) + '/reset', {}), setStage)
}
function lock(h: Race, locked: boolean) {
  act(h.id, () => post(racePath(h.id) + '/lock', { locked }), setStage)
}

const resultOpen = ref(false)
const resultRace = ref<Race | null>(null)
const resultModal = ref<{ fail: (m: string) => void } | null>(null)
function openResult(h: Race) {
  resultRace.value = h
  resultOpen.value = true
}
async function saveResult(placement: string[], lockIt: boolean) {
  const h = resultRace.value!
  try {
    setStage(await post(racePath(h.id) + '/result', { placement, lock: lockIt }))
    resultOpen.value = false
    toast.add({ title: `${h.id} result saved`, color: 'success', icon: 'i-lucide-check' })
  } catch (e: any) {
    resultModal.value?.fail(e.message)
  }
}

async function goToRace(raceId: string) {
  view.value = 'races'
  await nextTick()
  const el = document.getElementById(`race-${raceId}`)
  el?.scrollIntoView({ behavior: 'smooth', block: 'center' })
}

async function remove() {
  if (!stage.value || !confirm(`Delete "${stage.value.name}"? Races already sent stay in FPVTrackside.`)) return
  act('delete', () => api('DELETE', `/stages/${id.value}`), () => navigateTo('/'))
}

const tabs = computed(() => [
  { label: 'Races', value: 'races', icon: 'i-lucide-layout-grid' },
  ...(stage.value?.diagrams.length ? [{ label: 'Diagram', value: 'diagram', icon: 'i-lucide-git-fork' }] : []),
  { label: 'Standings', value: 'standings', icon: 'i-lucide-list-ordered' },
])
const standingColumns = [
  { accessorKey: 'rank', header: '#' },
  { accessorKey: 'name', header: 'Pilot' },
  { accessorKey: 'detail', header: '' },
]
</script>

<template>
  <div v-if="!stage">
    <UContainer class="py-10"><USkeleton class="h-40 rounded-xl" /></UContainer>
  </div>
  <div v-else>
    <ArtImage :name="`format-${stage.format}`" class="border-b border-default">
      <div class="absolute inset-0 bg-gradient-to-r from-[var(--ui-bg)] via-[var(--ui-bg)]/85 to-[var(--ui-bg)]/40" />
      <UContainer class="relative py-8">
        <UButton to="/" variant="link" color="neutral" icon="i-lucide-arrow-left" label="Stages" class="mb-2 px-0" />
        <div class="flex flex-wrap items-end gap-4">
          <div class="min-w-0 flex-1">
            <UBadge :label="stage.formatName" color="primary" variant="subtle" />
            <h1 class="mt-2 text-3xl font-bold sm:text-4xl">{{ stage.name }}</h1>
            <p class="mt-1 text-muted">{{ stage.pilotCount }} pilots · {{ stage.racesDone }} of {{ stage.raceCount }} races run</p>
          </div>
          <div v-if="stage.champion" class="flex items-center gap-3 rounded-xl bg-warning/10 px-4 py-3 ring-1 ring-warning/40">
            <UIcon name="i-lucide-trophy" class="size-8 text-warning" />
            <div>
              <p class="text-xs uppercase tracking-widest text-warning">Champion</p>
              <p class="font-display text-xl font-bold">{{ stage.champion }}</p>
            </div>
          </div>
        </div>
        <UProgress :model-value="stage.raceCount ? (100 * stage.racesDone) / stage.raceCount : 0" class="mt-5 max-w-xl" />
      </UContainer>
    </ArtImage>

    <UContainer class="py-6">
      <div class="mb-5 flex flex-wrap items-center gap-2">
        <UButton icon="i-lucide-send" :label="`Send ${ready} ready race${ready === 1 ? '' : 's'} to FPVTrackside`" :disabled="!ready" :loading="busy === 'send'" @click="sendAll" />
        <UButton color="neutral" variant="outline" icon="i-lucide-refresh-cw" label="Sync results" :loading="busy === 'sync'" @click="sync" />
        <USwitch :model-value="stage.autoSend" label="Send races to FPVTrackside automatically" class="ml-1" @update:model-value="setAutoSend" />
        <USwitch :model-value="stage.autoResults" label="Results from FPVTrackside automatically" class="ml-1" @update:model-value="setAuto" />
        <span class="flex-1" />
        <UButton color="error" variant="ghost" icon="i-lucide-trash-2" label="Delete" :loading="busy === 'delete'" @click="remove" />
      </div>

      <UTabs v-model="view" :items="tabs" :content="false" class="mb-5" />

      <div v-if="mains.length > 1 && view !== 'standings'" class="mb-5 flex flex-wrap gap-2">
        <UButton v-for="m in ['all', ...mains]" :key="m" size="sm" :color="main === m ? 'primary' : 'neutral'" :variant="main === m ? 'solid' : 'outline'" :label="m === 'all' ? 'All brackets' : `Bracket ${m}`" @click="main = m" />
      </div>

      <!-- Races, a column per round -->
      <div v-if="view === 'races'" class="flex gap-4 overflow-x-auto pb-4 max-md:flex-col">
        <section v-for="[label, list] in rounds" :key="label" class="w-full shrink-0 md:w-80">
          <h2 class="mb-2 text-xs font-bold uppercase tracking-widest text-muted">{{ label }}</h2>
          <div class="grid gap-3">
            <RaceCard
              v-for="h in list" :key="h.id" :race="h"
              :live="!!h.fpvRaceId && h.fpvRaceId === liveRaceId"
              @send="send(h, $event)" @result="openResult(h)" @reset="reset(h)" @lock="lock(h, $event)"
            />
          </div>
        </section>
      </div>

      <!-- Diagram -->
      <div v-else-if="view === 'diagram'" class="grid gap-5">
        <UCard v-for="d in diagrams" :key="d.title ?? 'd'" :title="d.title ?? undefined">
          <StageDiagram :diagram="d" :races="stage.races" :live-race-id="liveRaceId" @open="goToRace" />
        </UCard>
      </div>

      <!-- Standings -->
      <UCard v-else>
        <UTable v-if="stage.standings.length" :data="stage.standings" :columns="standingColumns">
          <template #name-cell="{ row }">
            <span class="font-semibold">{{ row.original.name }}</span>
            <UIcon v-if="row.original.rank === 1 && stage.champion" name="i-lucide-trophy" class="ml-1.5 align-middle text-warning" />
            <UBadge v-if="row.original.tied" label="not decided on track" size="sm" color="neutral" variant="subtle" class="ml-2" />
          </template>
        </UTable>
        <UEmpty v-else icon="i-lucide-list-ordered" title="No standings yet" description="They fill in as races are run." />
      </UCard>
    </UContainer>

    <ResultModal ref="resultModal" v-model:open="resultOpen" :race="resultRace" @save="saveResult" />
  </div>
</template>
