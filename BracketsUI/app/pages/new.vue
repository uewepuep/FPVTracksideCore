<script setup lang="ts">
import type { BracketFormat } from '~/composables/types'

// New bracket: format → pilots & seeding → settings. Everything is posted to
// POST /api/v1/brackets, which generates the whole structure at once.

// added: not in the event yet — from FPVTrackside's pilot database, or a new
// name. Creating the bracket adds them to the event.
interface SeedPilot { id: string; name: string; use: boolean; time: number | null; added?: 'database' | 'new' }

const onError = useApiErrors()
const toast = useToast()
const router = useRouter()

const step = ref(0)
const steps = [
  { title: 'Format', description: 'How the bracket runs', icon: 'i-lucide-git-fork', value: 0 },
  { title: 'Pilots', description: 'Who races, in seed order', icon: 'i-lucide-users', value: 1 },
  { title: 'Settings', description: 'Heats, finals, channels', icon: 'i-lucide-sliders-horizontal', value: 2 },
]

const formats = ref<BracketFormat[]>([])
const channels = ref<{ label: string }[]>([])
const rounds = ref<{ id: string; name: string; eventType: string; number: number }[]>([])
const pilots = ref<SeedPilot[]>([])
const database = ref<{ id: string; name: string; inEvent: boolean }[]>([])
const toAdd = ref<string[]>([])

const format = ref('elimination')
const name = ref('')
const setup = reactive({
  eliminationType: 'double',
  finalsFormat: 'chase-the-ace',
  pilotsPerRace: 4,
  pilotsToAdvance: 2,
  aceWinsRequired: 2,
  qualifierCta: false,
  qualifierSeed: 'snake',
  pilotsPerMain: 16,
  bumpUp: 4,
  ctaMainsText: '',
  mainsText: 'A, B',
  rounds: 3,
  direction: 'descending',
  pointsText: '',
  dropWorst: 0,
})
const seats = ref<string[]>([])
const autoSend = ref(false)
const seedLaps = ref<number | undefined>()
const seedRound = ref<string>('all')
const seeding = ref(false)
const creating = ref(false)
const error = ref('')

const current = computed(() => formats.value.find(f => f.kind === format.value))
const uses = (k: string) => !!current.value?.uses.includes(k)
const chosen = computed(() => pilots.value.filter(p => p.use))
const seatCount = computed(() => (format.value === 'mains' ? 0 : Math.max(2, Math.min(8, setup.pilotsPerRace || 4))))

const formatIcons: Record<string, string> = {
  elimination: 'i-lucide-git-fork',
  qualifier: 'i-lucide-medal',
  mainsprogression: 'i-lucide-layers',
  ladder: 'i-lucide-arrow-up-narrow-wide',
  mains: 'i-lucide-table-2',
}

onMounted(async () => {
  try {
    const [f, p, c, r] = await Promise.all([get('/brackets/formats'), get('/pilots'), get('/channels'), get('/rounds')])
    formats.value = f
    channels.value = c
    rounds.value = r
    pilots.value = p.map((x: any) => ({ id: x.id, name: x.name, use: true, time: null }))
  } catch (e) {
    onError(e)
  }
  try {
    database.value = await get('/pilots/database')
  } catch {
    // Older FPVTrackside without the pilot database endpoint: event pilots only.
  }
})

watch(() => setup.pilotsPerRace, (n) => {
  if (setup.pilotsToAdvance >= n) setup.pilotsToAdvance = Math.max(1, n - 1)
  seats.value = seats.value.slice(0, n)
})

async function seedFromQualifying() {
  seeding.value = true
  try {
    const q = new URLSearchParams({ pilots: 'all' })
    if (seedLaps.value) q.set('laps', String(seedLaps.value))
    if (seedRound.value !== 'all') q.set('round', seedRound.value)
    const rows = await get<any[]>(`/brackets/seeding?${q}`)
    const was = new Map(pilots.value.map(p => [p.id, p.use]))
    const seeded = new Set(rows.map(r => r.pilotId))
    // Pilots added from the database (no qualifying in this event) keep their place after the seeded ones.
    pilots.value = rows.map(r => ({ id: r.pilotId, name: r.name, use: was.get(r.pilotId) !== false && r.races > 0, time: r.bestSeconds }))
      .concat(pilots.value.filter(p => !seeded.has(p.id) && p.added))
    toast.add({ title: 'Seeded from qualifying', description: `${rows.filter(r => r.bestSeconds != null).length} pilots have a time`, icon: 'i-lucide-timer', color: 'success' })
  } catch (e) {
    onError(e)
  } finally {
    seeding.value = false
  }
}

// FPVTrackside's pilots not already on the list, for the picker.
const addable = computed(() => {
  const listed = new Set(pilots.value.map(p => p.id))
  return database.value.filter(d => !listed.has(d.id)).map(d => ({ label: d.name, value: d.id }))
})

function addPilots() {
  for (const id of toAdd.value) {
    const d = database.value.find(x => x.id === id)
    if (d && !pilots.value.some(p => p.id === id)) pilots.value.push({ id, name: d.name, use: true, time: null, added: d.inEvent ? undefined : 'database' })
  }
  toAdd.value = []
}

function createPilot(name: string) {
  const n = name.trim()
  if (!n || pilots.value.some(p => p.name.toLowerCase() === n.toLowerCase())) return
  const known = database.value.find(d => d.name.toLowerCase() === n.toLowerCase())
  if (known) {
    toAdd.value = [...toAdd.value, known.id]
    return
  }
  // A brand-new pilot: sent by name, FPVTrackside creates them.
  pilots.value.push({ id: n, name: n, use: true, time: null, added: 'new' })
}

// New pilot form: saved to FPVTrackside right away (POST /pilots adds them to
// the event and its pilot database), then listed here.
const newOpen = ref(false)
const newPilot = reactive({ name: '', firstName: '', lastName: '' })
const newSaving = ref(false)
const newError = ref('')
function openNewPilot() {
  Object.assign(newPilot, { name: '', firstName: '', lastName: '' })
  newError.value = ''
  newOpen.value = true
}
async function saveNewPilot() {
  const n = newPilot.name.trim()
  if (!n) {
    newError.value = 'Enter the pilot\'s callsign.'
    return
  }
  newSaving.value = true
  newError.value = ''
  try {
    const p = await post<{ id: string; name: string }>('/pilots', { name: n, firstName: newPilot.firstName.trim() || null, lastName: newPilot.lastName.trim() || null })
    pilots.value.push({ id: p.id, name: p.name, use: true, time: null })
    database.value.push({ id: p.id, name: p.name, inEvent: true })
    newOpen.value = false
    toast.add({ title: `${p.name} added to the event`, icon: 'i-lucide-user-plus', color: 'success' })
  } catch (e: any) {
    newError.value = e.message
  } finally {
    newSaving.value = false
  }
}

function move(i: number, d: number) {
  const j = i + d
  if (j < 0 || j >= pilots.value.length) return
  const list = pilots.value
  ;[list[i], list[j]] = [list[j]!, list[i]!]
}

// Pointer drag from the grip (mouse and touch). The list reorders live as
// the row passes its neighbours, so you can see where it will land.
const dragging = ref<number | null>(null)
function startDrag(i: number, ev: PointerEvent) {
  const grip = ev.currentTarget as HTMLElement
  const row = grip.closest('li') as HTMLElement
  const h = row.getBoundingClientRect().height + 6
  let originY = ev.clientY
  let at = i
  dragging.value = i
  grip.setPointerCapture(ev.pointerId)
  const onMove = (e: PointerEvent) => {
    let dy = e.clientY - originY
    // Step one place each time the row is dragged half a row past a neighbour.
    while (dy > h / 2 && at < pilots.value.length - 1) {
      const [item] = pilots.value.splice(at, 1)
      pilots.value.splice(++at, 0, item!)
      originY += h
      dy -= h
    }
    while (dy < -h / 2 && at > 0) {
      const [item] = pilots.value.splice(at, 1)
      pilots.value.splice(--at, 0, item!)
      originY -= h
      dy += h
    }
    dragging.value = at
    row.style.transform = `translateY(${dy}px)`
  }
  const onUp = () => {
    grip.removeEventListener('pointermove', onMove)
    grip.removeEventListener('pointerup', onUp)
    grip.removeEventListener('pointercancel', onUp)
    row.style.transform = ''
    dragging.value = null
  }
  grip.addEventListener('pointermove', onMove)
  grip.addEventListener('pointerup', onUp)
  grip.addEventListener('pointercancel', onUp)
}


const list = (s: string) => s.split(',').map(x => x.trim().toUpperCase()).filter(Boolean)

function body() {
  const points = setup.pointsText.split(',').map(x => x.trim()).filter(Boolean).map(Number)
  const pointsTable = points.length && points.every(n => !Number.isNaN(n))
    ? Object.fromEntries(points.map((n, i) => [i + 1, n]))
    : null
  const seatList = seats.value.filter(x => x && x !== 'auto')
  return {
    name: name.value.trim(),
    format: format.value,
    pilots: chosen.value.map(p => p.id),
    seatChannels: seatList.length === seatCount.value ? seatList : [],
    autoSend: autoSend.value,
    setup: {
      eliminationType: setup.eliminationType,
      finalsFormat: setup.finalsFormat,
      pilotsPerRace: setup.pilotsPerRace,
      pilotsToAdvance: setup.pilotsToAdvance,
      aceWinsRequired: setup.aceWinsRequired,
      qualifierCta: setup.qualifierCta,
      qualifierSeed: setup.qualifierSeed,
      pilotsPerMain: setup.pilotsPerMain,
      bumpUp: setup.bumpUp,
      ctaMains: list(setup.ctaMainsText),
      mains: list(setup.mainsText),
      rounds: setup.rounds,
      direction: setup.direction,
      pointsTable,
      dropWorst: setup.dropWorst,
    },
  }
}

async function create() {
  error.value = ''
  if (!name.value.trim()) {
    error.value = 'Give the bracket a name.'
    return
  }
  if (chosen.value.length < 2) {
    error.value = 'Pick at least 2 pilots.'
    step.value = 1
    return
  }
  creating.value = true
  try {
    const b = await post('/brackets', body())
    toast.add({ title: 'Bracket created', description: b.name, icon: 'i-lucide-check', color: 'success' })
    router.push(`/b/${b.id}`)
  } catch (e: any) {
    error.value = e.message
  } finally {
    creating.value = false
  }
}
</script>

<template>
  <UContainer class="py-8">
    <UButton to="/" variant="link" color="neutral" icon="i-lucide-arrow-left" label="Brackets" class="mb-4 px-0" />
    <h1 class="text-3xl font-bold">New bracket</h1>

    <UStepper v-model="step" :items="steps" class="my-8" />

    <!-- 1 · Format -->
    <div v-if="step === 0" class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <button
        v-for="f in formats"
        :key="f.kind"
        type="button"
        class="group overflow-hidden rounded-xl border text-left transition"
        :class="f.kind === format ? 'border-primary ring-2 ring-primary' : 'border-default hover:border-primary/60'"
        :aria-pressed="f.kind === format"
        @click="format = f.kind"
      >
        <ArtImage :name="`format-${f.kind}`" class="h-32">
          <div class="absolute inset-0 bg-gradient-to-t from-[var(--ui-bg-elevated)] to-transparent" />
          <UIcon :name="formatIcons[f.kind] ?? 'i-lucide-git-fork'" class="absolute bottom-3 left-4 size-7 text-primary" />
        </ArtImage>
        <div class="bg-elevated p-4">
          <h2 class="font-display text-lg font-bold">{{ f.name }}</h2>
          <p class="mt-1 text-sm text-muted">{{ f.description }}</p>
        </div>
      </button>
    </div>

    <!-- 2 · Pilots -->
    <UCard v-else-if="step === 1">
      <template #header>
        <div class="flex flex-wrap items-end gap-3">
          <div class="min-w-0 flex-1">
            <h2 class="text-lg font-bold">Pilots and seeding</h2>
            <p class="text-sm text-muted">Top seed first. Seed from the qualifying times, then drag a pilot by its handle (or use the arrows) to change their position. Untick anyone not in this bracket.</p>
          </div>
          <UFormField label="Best consecutive laps">
            <UInputNumber v-model="seedLaps" :min="1" :max="20" placeholder="PB laps" class="w-32" />
          </UFormField>
          <UFormField label="From">
            <USelect v-model="seedRound" :items="[{ label: 'All time-trial rounds', value: 'all' }, ...rounds.map(r => ({ label: r.name || `${r.eventType} round ${r.number}`, value: r.id }))]" class="w-56" />
          </UFormField>
          <UButton icon="i-lucide-timer" label="Seed from qualifying" :loading="seeding" @click="seedFromQualifying" />
        </div>
      </template>

      <div class="mb-4 flex flex-wrap items-start gap-2">
        <UFormField label="Add pilots from FPVTrackside" :help="database.length ? 'Pick from every pilot FPVTrackside knows, or type a new name. Anyone not in this event is added to it when the bracket is created.' : 'Type a name to add a pilot.'" class="min-w-0 flex-1">
          <USelectMenu
            v-model="toAdd"
            :items="addable"
            value-key="value"
            multiple
            create-item
            placeholder="Search pilots…"
            icon="i-lucide-user-plus"
            class="w-full"
            @create="createPilot"
          />
        </UFormField>
        <UButton icon="i-lucide-plus" :label="toAdd.length ? `Add ${toAdd.length}` : 'Add'" :disabled="!toAdd.length" class="mt-6" @click="addPilots" />
        <UButton icon="i-lucide-user-round-plus" label="New pilot" color="neutral" variant="outline" class="mt-6" @click="openNewPilot" />
      </div>

      <p class="mb-3 text-sm text-muted">{{ chosen.length }} pilots in this bracket</p>
      <UEmpty
        v-if="!pilots.length"
        icon="i-lucide-users"
        title="No pilots yet"
        description="This event has no pilots yet. Add them above from FPVTrackside's pilots, or create one with New pilot."
        class="mb-2"
      />
      <ol class="grid gap-1.5">
        <li
          v-for="(p, i) in pilots"
          :key="p.id"
          class="grid grid-cols-[2rem_3rem_minmax(0,1fr)_auto_auto] items-center gap-3 rounded-lg border border-default bg-elevated/50 px-3 py-2 transition"
          :class="{ 'opacity-50': !p.use, 'z-10 shadow-lg ring-2 ring-primary': dragging === i }"
        >
          <span class="flex cursor-grab touch-none select-none items-center justify-center rounded text-muted hover:bg-accented hover:text-default active:cursor-grabbing" title="Drag to change the seed" aria-hidden="true" @pointerdown="startDrag(i, $event)"><UIcon name="i-lucide-grip-vertical" class="size-5" /></span>
          <span class="font-mono text-sm font-bold text-muted">{{ p.use ? `#${pilots.slice(0, i + 1).filter(x => x.use).length}` : '—' }}</span>
          <span class="flex min-w-0 items-center gap-2">
            <UCheckbox v-model="p.use" :label="p.name" />
            <UBadge v-if="p.added" size="sm" color="neutral" variant="subtle" :label="p.added === 'new' ? 'new pilot' : 'joins the event'" />
            <UButton v-if="p.added" size="xs" color="neutral" variant="ghost" icon="i-lucide-x" :aria-label="`Remove ${p.name}`" @click="pilots.splice(i, 1)" />
          </span>
          <span class="font-mono text-xs text-muted">{{ p.time != null ? `${p.time.toFixed(3)}s` : '' }}</span>
          <span class="flex gap-1">
            <UButton size="xs" color="neutral" variant="ghost" icon="i-lucide-chevron-up" :aria-label="`Move ${p.name} up`" @click="move(i, -1)" />
            <UButton size="xs" color="neutral" variant="ghost" icon="i-lucide-chevron-down" :aria-label="`Move ${p.name} down`" @click="move(i, 1)" />
          </span>
        </li>
      </ol>
    </UCard>

    <!-- 3 · Settings -->
    <UCard v-else>
      <div class="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
        <UFormField label="Name" required class="sm:col-span-2 lg:col-span-3">
          <UInput v-model="name" placeholder="e.g. Open Class Finals" size="lg" class="w-full" />
        </UFormField>

        <UFormField v-if="uses('eliminationType')" label="Elimination">
          <USelect v-model="setup.eliminationType" :items="[{ label: 'Double (redemption bracket)', value: 'double' }, { label: 'Single', value: 'single' }]" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('finalsFormat')" label="Finals">
          <USelect v-model="setup.finalsFormat" :items="[{ label: 'Chase the ace', value: 'chase-the-ace' }, { label: 'Grand final', value: 'double-elimination' }]" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('pilotsPerRace')" label="Pilots per heat">
          <UInputNumber v-model="setup.pilotsPerRace" :min="2" :max="8" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('pilotsToAdvance')" label="Advance per heat">
          <UInputNumber v-model="setup.pilotsToAdvance" :min="1" :max="setup.pilotsPerRace - 1" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('qualifierCta')" label="Final">
          <USwitch v-model="setup.qualifierCta" label="Run it as chase the ace" />
        </UFormField>
        <UFormField v-if="uses('aceWinsRequired') && (format !== 'elimination' || setup.finalsFormat === 'chase-the-ace')" label="Chase-the-ace wins needed" help="First to win this many final heats is champion.">
          <UInputNumber v-model="setup.aceWinsRequired" :min="1" :max="5" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('qualifierSeed')" label="Round-1 seeding (16 pilots)">
          <USelect v-model="setup.qualifierSeed" :items="[{ label: 'MultiGP snake', value: 'snake' }, { label: 'Standard seed sheet', value: 'photo' }]" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('pilotsPerMain')" label="Pilots per main">
          <UInputNumber v-model="setup.pilotsPerMain" :min="2" :max="64" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('bumpUp')" label="Bump up per main" help="How many move up to the next main.">
          <UInputNumber v-model="setup.bumpUp" :min="1" :max="16" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('ctaMains')" label="Chase the ace in mains" help="e.g. A, B — or * for all. Blank = A Main only.">
          <UInput v-model="setup.ctaMainsText" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('mains')" label="Mains" help="Comma list, e.g. A, B, C.">
          <UInput v-model="setup.mainsText" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('rounds')" label="Rounds">
          <UInputNumber v-model="setup.rounds" :min="1" :max="20" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('direction')" label="Points">
          <USelect v-model="setup.direction" :items="[{ label: 'High points win', value: 'descending' }, { label: 'Low points win (position = points)', value: 'ascending' }]" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('pointsTable')" label="Points for 1st, 2nd, …" help="e.g. 10, 8, 6, 4. Blank = default.">
          <UInput v-model="setup.pointsText" class="w-full" />
        </UFormField>
        <UFormField v-if="uses('dropWorst')" label="Drop worst rounds">
          <UInputNumber v-model="setup.dropWorst" :min="0" :max="10" class="w-full" />
        </UFormField>
      </div>

      <USeparator class="my-6" />
      <USwitch
        v-model="autoSend"
        label="Send heats to FPVTrackside automatically"
        description="Each heat is created in FPVTrackside as soon as its pilots are known: round 1 straight away, later heats as results come in. You can switch this on or off later."
      />

      <USeparator v-if="seatCount" class="my-6" />
      <div v-if="seatCount">
        <h3 class="font-bold">Channel per seat</h3>
        <p class="mb-3 text-sm text-muted">Optional. A pilot keeps the channel they flew in their last heat when it's free; otherwise seats fill in this order.</p>
        <div class="flex flex-wrap gap-3">
          <UFormField v-for="i in seatCount" :key="i" :label="`Seat ${i}`">
            <USelect :model-value="seats[i - 1] || 'auto'" @update:model-value="(v: any) => (seats[i - 1] = v)" :items="[{ label: 'Auto', value: 'auto' }, ...channels.map(c => ({ label: c.label, value: c.label }))]" class="w-24" />
          </UFormField>
        </div>
      </div>
    </UCard>

    <div class="mt-6 flex flex-wrap items-center gap-3">
      <UButton v-if="step > 0" color="neutral" variant="outline" icon="i-lucide-arrow-left" label="Back" @click="step--" />
      <UButton v-if="step < 2" trailing-icon="i-lucide-arrow-right" label="Next" @click="step++" />
      <UButton v-else size="lg" icon="i-lucide-rocket" label="Create bracket" :loading="creating" @click="create" />
      <p v-if="error" class="text-sm text-error" role="alert">{{ error }}</p>
    
    <UModal v-model:open="newOpen" title="New pilot" description="Adds the pilot to this event in FPVTrackside, and to this bracket.">
      <template #body>
        <form id="new-pilot-form" class="grid gap-4" @submit.prevent="saveNewPilot">
          <UFormField label="Callsign" required :error="newError || undefined">
            <UInput v-model="newPilot.name" placeholder="e.g. Wheelzfpv" autofocus class="w-full" />
          </UFormField>
          <div class="grid gap-4 sm:grid-cols-2">
            <UFormField label="First name"><UInput v-model="newPilot.firstName" class="w-full" /></UFormField>
            <UFormField label="Last name"><UInput v-model="newPilot.lastName" class="w-full" /></UFormField>
          </div>
        </form>
      </template>
      <template #footer>
        <div class="flex w-full justify-end gap-2">
          <UButton color="neutral" variant="ghost" label="Cancel" @click="newOpen = false" />
          <UButton type="submit" form="new-pilot-form" icon="i-lucide-user-round-plus" label="Add pilot" :loading="newSaving" />
        </div>
      </template>
    </UModal>
  </div>
  </UContainer>
</template>
