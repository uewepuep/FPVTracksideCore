// Shapes returned by /api/v1 (see /api/v1/docs → Models).

export interface RacePilot { pilotId: string; name: string; seed: number; channel: string | null }

export type RaceState = 'waiting' | 'ready' | 'sent' | 'running' | 'flown' | 'finished' | 'void'

export interface Race {
  id: string
  round: number
  group: string
  title: string
  state: RaceState
  note: string | null
  pilots: RacePilot[]
  winners: string[]
  losers: string[]
  expectedWinners: number
  canEdit: boolean
  manualLock: boolean
  fpvRaceId: string | null
  fpvRaceName: string | null
}

export interface Standing { rank: number; pilotId: string; name: string; detail: string | null; tied: boolean }

export interface DiagramSeat { pilotId: string | null; name: string | null; position: number; advanced: boolean; fromRace: string | null; fromRank: number }
export interface DiagramNode { id: string; column: number; lane: string; label: string; seats: DiagramSeat[] }
export interface DiagramEdge { from: string; to: string; ranks: number[] }
export interface Diagram { title: string | null; nodes: DiagramNode[]; edges: DiagramEdge[] }

export interface StageSummary {
  id: number
  name: string
  format: string
  formatName: string
  pilotCount: number
  raceCount: number
  racesDone: number
  champion: string | null
  isComplete: boolean
  updated: string
}

export interface Stage extends StageSummary {
  setup: Record<string, any>
  pilots: RacePilot[]
  seatChannels: string[]
  autoResults: boolean
  autoSend: boolean
  layout: string
  races: Race[]
  standings: Standing[]
  diagrams: Diagram[]
}

export interface StageFormat { kind: string; name: string; description: string; uses: string[] }

export interface RaceControl {
  state: 'none' | 'ready' | 'starting' | 'running' | 'finished'
  canStart: boolean
  raceControlEnabled: boolean
  elapsedSeconds: number
  currentRace: { id: string; name: string } | null
  nextRace: { id: string; name: string } | null
}
