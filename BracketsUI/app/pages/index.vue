<script setup lang="ts">
import type { BracketSummary } from '~/composables/types'

const onError = useApiErrors()
const brackets = ref<BracketSummary[] | null>(null)

async function load() {
  try {
    brackets.value = await get<BracketSummary[]>('/brackets')
  } catch (e) {
    onError(e)
    brackets.value = []
  }
}
onMounted(load)
</script>

<template>
  <div>
    <ArtImage name="hero" class="border-b border-default">
      <div class="absolute inset-0 bg-gradient-to-r from-[var(--ui-bg)] via-[var(--ui-bg)]/70 to-transparent" />
      <UContainer class="relative py-14 sm:py-20">
        <p class="font-display text-sm font-semibold uppercase tracking-[0.2em] text-primary">Race day</p>
        <h1 class="mt-2 max-w-xl text-4xl font-bold sm:text-5xl">Brackets that run themselves</h1>
        <p class="mt-3 max-w-xl text-muted">
          Elimination, top-4 qualifiers, mains progression, ladders and points mains. Each heat runs as a normal
          FPVTrackside race, and its result lands here the moment the race ends.
        </p>
        <div class="mt-6 flex flex-wrap gap-3">
          <UButton to="/new" size="lg" icon="i-lucide-plus" label="New bracket" />
          <UButton to="/api/v1/docs" external target="_blank" size="lg" color="neutral" variant="outline" icon="i-lucide-braces" label="Run it from code" />
        </div>
      </UContainer>
    </ArtImage>

    <UContainer class="py-8">
      <div v-if="brackets === null" class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <USkeleton v-for="i in 3" :key="i" class="h-56 rounded-xl" />
      </div>

      <UEmpty
        v-else-if="brackets.length === 0"
        icon="i-lucide-git-fork"
        title="No brackets for this event yet"
        description="Create one from the qualifying results, then send its heats to FPVTrackside."
        :actions="[{ label: 'New bracket', icon: 'i-lucide-plus', to: '/new' }]"
        class="py-16"
      />

      <div v-else class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <NuxtLink v-for="b in brackets" :key="b.id" :to="`/b/${b.id}`" class="group">
          <UCard :ui="{ root: 'overflow-hidden transition group-hover:ring-primary group-hover:-translate-y-0.5', header: 'p-0 sm:p-0' }">
            <template #header>
              <ArtImage :name="`format-${b.format}`" class="h-28">
                <div class="absolute inset-0 bg-gradient-to-t from-[var(--ui-bg-elevated)] to-transparent" />
                <UBadge class="absolute left-3 top-3" color="neutral" variant="solid" :label="b.formatName" />
              </ArtImage>
            </template>
            <h2 class="truncate text-lg font-bold">{{ b.name }}</h2>
            <p class="text-sm text-muted">{{ b.pilotCount }} pilots · {{ b.heatsDone }} of {{ b.heatCount }} heats run</p>
            <UProgress :model-value="b.heatCount ? (100 * b.heatsDone) / b.heatCount : 0" size="sm" class="mt-3" />
            <p v-if="b.champion" class="mt-3 flex items-center gap-1.5 font-semibold text-warning">
              <UIcon name="i-lucide-trophy" /> {{ b.champion }}
            </p>
            <p v-else-if="b.isComplete" class="mt-3 text-sm text-success">Complete</p>
          </UCard>
        </NuxtLink>
      </div>
    </UContainer>
  </div>
</template>
