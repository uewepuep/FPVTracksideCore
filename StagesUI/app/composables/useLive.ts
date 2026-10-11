import type { RaceControl } from './types'

// The event + race-director state, polled while the page is visible and shared
// by every component (the header pills, the "on screen now" race highlight).
const info = ref<{ eventName: string; version: string; canWrite: boolean } | null>(null)
const control = ref<RaceControl | null>(null)
const reachable = ref(true)
let started = false

async function poll() {
  if (document.hidden) return
  try {
    const [i, c] = await Promise.all([get('/'), get<RaceControl>('/race-control')])
    info.value = i
    control.value = c
    reachable.value = true
  } catch {
    reachable.value = false
  }
}

export function useLive() {
  if (!started && import.meta.client) {
    started = true
    poll()
    setInterval(poll, 2000)
  }
  const liveRaceId = computed(() => control.value?.currentRace?.id ?? null)
  return { info, control, reachable, liveRaceId, refresh: poll }
}
