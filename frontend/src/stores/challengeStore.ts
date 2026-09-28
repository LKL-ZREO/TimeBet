import { create } from 'zustand'
import { callBridge } from '../bridge/webview'
import type {
  ChallengeOverview,
  ChallengeRound,
  PlayChallengeResult,
} from '../bridge/types'
import { useSessionStore } from './sessionStore'

const SPIN_DURATION_MS = 2_400
const RESULT_HOLD_DURATION_MS = 1_600
const REDUCED_MOTION_SPIN_DURATION_MS = 300
const REDUCED_MOTION_RESULT_HOLD_DURATION_MS = 1_200

type PendingRound = {
  id: string
  wagerSeconds: number
}

type ChallengeStore = {
  overview: ChallengeOverview | null
  receipt: ChallengeRound | null
  pendingRound: PendingRound | null
  loading: boolean
  saving: boolean
  spinning: boolean
  settling: boolean
  error: string | null
  load(): Promise<void>
  setEnabled(enabled: boolean): Promise<boolean>
  play(wagerSeconds: number): Promise<boolean>
  clearError(): void
  dismissReceipt(): void
}

export const useChallengeStore = create<ChallengeStore>((set, get) => ({
  overview: null,
  receipt: null,
  pendingRound: null,
  loading: false,
  saving: false,
  spinning: false,
  settling: false,
  error: null,

  async load() {
    set({ loading: true, error: null })
    try {
      const overview = await callBridge<ChallengeOverview>('challenge.overview')
      set({ overview, loading: false })
      syncBalance(overview.balanceSeconds)
    } catch (error) {
      set({ loading: false, error: errorMessage(error) })
    }
  },

  async setEnabled(enabled) {
    set({ saving: true, error: null })
    try {
      const overview = await callBridge<ChallengeOverview>(
        'challenge.settings.update',
        { enabled },
      )
      set({ overview, saving: false, pendingRound: null })
      syncBalance(overview.balanceSeconds)
      return true
    } catch (error) {
      set({ saving: false, error: errorMessage(error) })
      return false
    }
  },

  async play(wagerSeconds) {
    const currentPending = get().pendingRound
    const pending =
      currentPending?.wagerSeconds === wagerSeconds
        ? currentPending
        : { id: crypto.randomUUID(), wagerSeconds }
    set({ spinning: true, settling: false, pendingRound: pending, error: null })
    try {
      const [result] = await Promise.all([
        callBridge<PlayChallengeResult>('challenge.play', {
          roundId: pending.id,
          wagerSeconds,
        }),
        minimumSpinDelay(),
      ])
      set({
        overview: result.overview,
        pendingRound: null,
        spinning: false,
        settling: true,
      })
      syncBalance(result.overview.balanceSeconds)
      await resultHoldDelay()
      set({ receipt: result.round, settling: false })
      return true
    } catch (error) {
      set({ spinning: false, settling: false, error: errorMessage(error) })
      return false
    }
  },

  clearError() {
    set({ error: null })
  },

  dismissReceipt() {
    set({ receipt: null })
  },
}))

function syncBalance(balanceSeconds: number) {
  useSessionStore.setState((current) =>
    current.state ? { state: { ...current.state, balanceSeconds } } : current,
  )
}

function minimumSpinDelay() {
  const reducedMotion = window.matchMedia?.(
    '(prefers-reduced-motion: reduce)',
  ).matches
  return new Promise<void>((resolve) =>
    window.setTimeout(
      resolve,
      reducedMotion ? REDUCED_MOTION_SPIN_DURATION_MS : SPIN_DURATION_MS,
    ),
  )
}

function resultHoldDelay() {
  const reducedMotion = window.matchMedia?.(
    '(prefers-reduced-motion: reduce)',
  ).matches
  return new Promise<void>((resolve) =>
    window.setTimeout(
      resolve,
      reducedMotion
        ? REDUCED_MOTION_RESULT_HOLD_DURATION_MS
        : RESULT_HOLD_DURATION_MS,
    ),
  )
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : '星愿机暂时无法完成这次挑战。'
}
