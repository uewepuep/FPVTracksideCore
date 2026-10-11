// Shapes returned by /api/v1 (see /api/v1/docs → Models).

export interface HeatPilot { pilotId: string; name: string; seed: number; channel: string | null }

export type HeatState = 'waiting' | 'ready' | 'sent' | 'running' | 'flown' | 'finished' | 'void'

export interface Heat {
  id: string
  round: number
  group: string
  title: string
  state: HeatState
  note: string | null
  pilots: HeatPilot[]
  winners: string[]
  losers: string[]
  expectedWinners: number
  canEdit: boolean
  manualLock: boolean
  fpvRaceId: string | null
  fpvRaceName: string | null
}

export interface Standing { rank: number; pilotId: string; name: string; detail: string | null; tied: boolean }

export interface DiagramSeat { pilotId: string | null; name: string | null; position: number; advanced: boolean; fromHeat: string | null; fromRank: number }
export interface DiagramNode { id: string; column: number; lane: string; label: string; seats: DiagramSeat[] }
export interface DiagramEdge { from: string; to: string; ranks: number[] }
export interface Diagram { title: string | null; nodes: DiagramNode[]; edges: DiagramEdge[] }

export interface BracketSummary {
  id: number
  name: string
  format: string
  formatName: string
  pilotCount: number
  heatCount: number
  heatsDone: number
  champion: string | null
  isComplete: boolean
  updated: string
}

export interface Bracket extends BracketSummary {
  setup: Record<string, any>
  pilots: HeatPilot[]
  seatChannels: string[]
  autoResults: boolean
  layout: string
  heats: Heat[]
  standings: Standing[]
  diagrams: Diagram[]
}

export interface BracketFormat { kind: string; name: string; description: string; uses: string[] }

export interface RaceControl {
  state: 'none' | 'ready' | 'starting' | 'running' | 'finished'
  canStart: boolean
  raceControlEnabled: boolean
  elapsedSeconds: number
  currentRace: { id: string; name: string } | null
  nextRace: { id: string; name: string } | null
}
