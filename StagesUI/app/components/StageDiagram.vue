<script setup lang="ts">
import type { Diagram, Race } from '~/composables/types'

// Draws one stage diagram from /stages/{id} `diagrams[]`: races in
// columns, winners above losers, the finals centred against both (as on
// the AUFPV platform), and the flow between races as elbow lines.
const props = defineProps<{ diagram: Diagram; races: Race[]; liveRaceId: string | null }>()
const emit = defineEmits<{ open: [raceId: string] }>()

const W = 176, GAP = 52, ROW = 19, HEAD = 24, PAD = 14, LANE_GAP = 40, NODE_GAP = 12
const LANE_ORDER = ['winners', 'losers']
const FINALS = ['grand-final', 'chase-the-ace']
const LANE_NAME: Record<string, string> = { winners: 'Winners', losers: 'Losers', 'grand-final': 'Grand final', 'chase-the-ace': 'Chase the ace' }

const raceById = computed(() => new Map(props.races.map(h => [h.id, h])))

const layout = computed(() => {
  const nodes = props.diagram.nodes
  const pos = new Map<string, { x: number; y: number; h: number }>()
  const labels: { text: string; x: number; y: number }[] = []
  const height = (n: typeof nodes[number]) => HEAD + Math.max(1, n.seats.length) * ROW + 8
  const x = (col: number) => PAD + (col - 1) * (W + GAP)

  let y = 28
  const lanes = LANE_ORDER.concat([...new Set(nodes.map(n => n.lane))].filter(l => !LANE_ORDER.includes(l) && !FINALS.includes(l)))
  for (const lane of lanes) {
    const list = nodes.filter(n => n.lane === lane)
    if (!list.length) continue
    labels.push({ text: LANE_NAME[lane] ?? lane, x: PAD, y: y - 10 })
    const colY = new Map<number, number>()
    let bottom = y
    for (const n of list) {
      const top = colY.get(n.column) ?? y
      pos.set(n.id, { x: x(n.column), y: top, h: height(n) })
      colY.set(n.column, top + height(n) + NODE_GAP)
      bottom = Math.max(bottom, top + height(n))
    }
    y = bottom + LANE_GAP
  }
  const bodyBottom = y - LANE_GAP

  // Finals sit in columns nothing else races in: centre them on the stage.
  const finals = nodes.filter(n => FINALS.includes(n.lane))
  if (finals.length) {
    const tallest = Math.max(...finals.map(height))
    const top = Math.max(28, (28 + bodyBottom) / 2 - tallest / 2)
    for (const lane of FINALS) {
      const list = finals.filter(n => n.lane === lane)
      if (!list.length) continue
      labels.push({ text: LANE_NAME[lane]!, x: x(Math.min(...list.map(n => n.column))), y: top - 10 })
      for (const n of list) pos.set(n.id, { x: x(n.column), y: top, h: height(n) })
    }
    y = Math.max(y, top + tallest + LANE_GAP)
  }

  const cols = Math.max(1, ...nodes.map(n => n.column))
  return { pos, labels, width: PAD * 2 + cols * W + (cols - 1) * GAP, height: y }
})

function edgePath(from: string, to: string) {
  const a = layout.value.pos.get(from), b = layout.value.pos.get(to)
  if (!a || !b) return ''
  const x1 = a.x + W, y1 = a.y + a.h / 2, x2 = b.x, y2 = b.y + HEAD / 2 + 2
  const mx = x2 > x1 ? x1 + (x2 - x1) / 2 : x1 + 16
  return `M${x1} ${y1} H${mx} V${y2} H${x2}`
}

const ordinal = (n: number) => n + (['th', 'st', 'nd', 'rd'][(n % 100 - 20) % 10] || ['th', 'st', 'nd', 'rd'][n % 100] || 'th')
const clip = (s: string, n = 21) => (s.length > n ? `${s.slice(0, n - 1)}…` : s)
</script>

<template>
  <div class="overflow-auto">
    <svg :width="layout.width" :height="layout.height" :viewBox="`0 0 ${layout.width} ${layout.height}`" role="img" :aria-label="diagram.title ?? 'Stage diagram'" class="block">
      <text v-for="l in layout.labels" :key="l.text + l.x" :x="l.x" :y="l.y" class="fill-[var(--ui-text-muted)] text-[11px] font-bold uppercase tracking-widest">{{ l.text }}</text>
      <path v-for="e in diagram.edges" :key="e.from + e.to" :d="edgePath(e.from, e.to)" fill="none" class="stroke-[var(--ui-border-accented)]" stroke-width="1.5" />
      <g
        v-for="n in diagram.nodes"
        :key="n.id"
        class="cursor-pointer"
        role="button"
        tabindex="0"
        :aria-label="`Race ${n.label}`"
        @click="emit('open', n.id)"
        @keydown.enter="emit('open', n.id)"
      >
        <rect
          :x="layout.pos.get(n.id)!.x" :y="layout.pos.get(n.id)!.y" :width="W" :height="layout.pos.get(n.id)!.h" rx="9"
          class="fill-[var(--ui-bg-elevated)]"
          :class="raceById.get(n.id)?.fpvRaceId && raceById.get(n.id)?.fpvRaceId === liveRaceId ? 'stroke-[var(--ui-error)] is-live' : raceById.get(n.id)?.state === 'finished' ? 'stroke-[var(--ui-success)]' : 'stroke-[var(--ui-border)]'"
          stroke-width="1.5"
        />
        <rect :x="layout.pos.get(n.id)!.x" :y="layout.pos.get(n.id)!.y" width="4" :height="layout.pos.get(n.id)!.h" rx="2"
          :class="n.lane === 'losers' ? 'fill-[var(--ui-error)]' : n.lane === 'winners' ? 'fill-[var(--ui-success)]' : 'fill-[var(--ui-secondary)]'" />
        <text :x="layout.pos.get(n.id)!.x + 12" :y="layout.pos.get(n.id)!.y + 16" class="fill-[var(--ui-text-muted)] font-mono text-[11px] font-bold">{{ n.label }}</text>
        <text
          v-for="(s, i) in n.seats" :key="i"
          :x="layout.pos.get(n.id)!.x + 12" :y="layout.pos.get(n.id)!.y + HEAD + (i + 1) * ROW - 5"
          class="text-[12.5px]"
          :class="s.name ? (s.advanced ? 'fill-[var(--ui-success)] font-semibold' : 'fill-[var(--ui-text)]') : 'fill-[var(--ui-text-dimmed)] italic'"
        >{{ s.name ? clip((s.position ? `${s.position}. ` : '') + s.name) : s.fromRace ? `${ordinal(s.fromRank)} of ${s.fromRace.replace(/^\w+:/, '')}` : 'TBD' }}</text>
      </g>
    </svg>
  </div>
</template>
