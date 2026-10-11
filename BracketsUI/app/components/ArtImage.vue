<script setup lang="ts">
// Illustration from public/art, with a track-lights backdrop until it loads —
// or instead of it, if this build shipped without the art.
const props = defineProps<{ name: string; alt?: string }>()
const failed = ref(false)
const src = computed(() => `${useRuntimeConfig().app.baseURL}art/${props.name}.webp`)
</script>

<template>
  <div class="track-backdrop relative overflow-hidden">
    <img v-if="!failed" :src="src" :alt="alt ?? ''" class="absolute inset-0 size-full object-cover" loading="lazy" @error="failed = true">
    <slot />
  </div>
</template>
