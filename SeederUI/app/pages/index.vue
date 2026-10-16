<script setup lang="ts">
import type { StageSummary } from '~/composables/types'

const onError = useApiErrors()
const stages = ref<StageSummary[] | null>(null)

async function load() {
  try {
    stages.value = await get<StageSummary[]>('/stages')
  } catch (e) {
    onError(e)
    stages.value = []
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
        <h1 class="mt-2 max-w-xl text-4xl font-bold sm:text-5xl">Stages that run themselves</h1>
        <p class="mt-3 max-w-xl text-muted">
          Double elimination, top-4 qualifiers, bracket progression, ladders and points brackets. Each race is a
          normal FPVTrackside race, and its result lands here the moment it ends.
        </p>
        <div class="mt-6 flex flex-wrap gap-3">
          <UButton to="/new" size="lg" icon="i-lucide-plus" label="New stage" />
          <UButton to="/api/v1/docs" external target="_blank" size="lg" color="neutral" variant="outline" icon="i-lucide-braces" label="Run it from code" />
        </div>
      </UContainer>
    </ArtImage>

    <UContainer class="py-8">
      <div v-if="stages === null" class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <USkeleton v-for="i in 3" :key="i" class="h-56 rounded-xl" />
      </div>

      <UEmpty
        v-else-if="stages.length === 0"
        icon="i-lucide-git-fork"
        title="No stages for this event yet"
        description="Create one from the qualifying results, then send its races to FPVTrackside."
        :actions="[{ label: 'New stage', icon: 'i-lucide-plus', to: '/new' }]"
        class="py-16"
      />

      <div v-else class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <NuxtLink v-for="b in stages" :key="b.id" :to="`/s/${b.id}`" class="group">
          <UCard :ui="{ root: 'overflow-hidden transition group-hover:ring-primary group-hover:-translate-y-0.5', header: 'p-0 sm:p-0' }">
            <template #header>
              <ArtImage :name="`format-${b.format}`" class="h-28">
                <div class="absolute inset-0 bg-gradient-to-t from-[var(--ui-bg-elevated)] to-transparent" />
                <UBadge class="absolute left-3 top-3" color="neutral" variant="solid" :label="b.formatName" />
              </ArtImage>
            </template>
            <h2 class="truncate text-lg font-bold">{{ b.name }}</h2>
            <p class="text-sm text-muted">{{ b.pilotCount }} pilots · {{ b.racesDone }} of {{ b.raceCount }} races run</p>
            <UProgress :model-value="b.raceCount ? (100 * b.racesDone) / b.raceCount : 0" size="sm" class="mt-3" />
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
