<script setup lang="ts">
import type { Heat, HeatPilot } from '~/composables/types'

// Click-to-rank: tap the pilots in finishing order.
const props = defineProps<{ heat: Heat | null }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ save: [placement: string[], lock: boolean] }>()

const order = ref<HeatPilot[]>([])
const lock = ref(true)
const saving = ref(false)
const error = ref('')

watch(open, (o) => {
  if (o) {
    order.value = []
    lock.value = true
    error.value = ''
    saving.value = false
  }
})

const left = computed(() => (props.heat?.pilots ?? []).filter(p => !order.value.includes(p)))
const need = computed(() => Math.min(props.heat?.expectedWinners ?? 0, props.heat?.pilots.length ?? 0))

function fail(msg: string) {
  error.value = msg
  saving.value = false
}
defineExpose({ fail })

function save() {
  saving.value = true
  error.value = ''
  emit('save', order.value.map(p => p.pilotId), lock.value)
}
</script>

<template>
  <UModal v-model:open="open" :title="`${heat?.title || heat?.id} — result`" :description="need < (heat?.pilots.length ?? 0) ? `Tap pilots in finishing order. The top ${need} advance, so you need at least them; the rest are optional.` : 'Tap the pilots in finishing order.'">
    <template #body>
      <ol class="mb-4 grid gap-1.5">
        <li v-for="(p, i) in order" :key="p.pilotId" class="flex items-center gap-3 rounded-lg bg-elevated px-3 py-2">
          <span class="grid size-7 place-items-center rounded-full font-mono text-sm font-bold" :class="i < need ? 'bg-primary text-inverted' : 'bg-accented'">{{ i + 1 }}</span>
          <span class="font-semibold">{{ p.name }}</span>
          <UButton class="ml-auto" size="xs" color="neutral" variant="ghost" icon="i-lucide-x" :aria-label="`Remove ${p.name}`" @click="order.splice(i, 1)" />
        </li>
      </ol>
      <div class="flex flex-wrap gap-2">
        <UButton v-for="p in left" :key="p.pilotId" color="neutral" variant="outline" size="lg" :label="p.name" @click="order.push(p)" />
      </div>
      <USwitch v-model="lock" class="mt-5" label="Keep this result even if FPVTrackside's timing says otherwise" />
      <p v-if="error" class="mt-3 text-sm text-error" role="alert">{{ error }}</p>
    </template>
    <template #footer>
      <div class="flex w-full justify-end gap-2">
        <UButton color="neutral" variant="ghost" label="Clear" @click="order = []" />
        <UButton icon="i-lucide-check" label="Save result" :loading="saving" :disabled="order.length < need" @click="save" />
      </div>
    </template>
  </UModal>
</template>
