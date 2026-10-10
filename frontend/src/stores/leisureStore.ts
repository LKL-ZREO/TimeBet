import { create } from 'zustand'
import { callBridge } from '../bridge/webview'
import type { FinishLeisureResult, LeisureState } from '../bridge/types'

type LeisureStore = {
  state: LeisureState | null
  receipt: FinishLeisureResult | null
  loading: boolean
  submitting: boolean
  error: string | null
  initialize(): Promise<void>
  start(activity: string, plannedMinutes: number): Promise<boolean>
  pause(): Promise<boolean>
  resume(): Promise<boolean>
  finish(completionNote?: string): Promise<boolean>
  clearError(): void
  dismissReceipt(): void
}

export const useLeisureStore = create<LeisureStore>((set) => ({
  state: null,
  receipt: null,
  loading: true,
  submitting: false,
  error: null,

  async initialize() {
    set({ loading: true, error: null })
    try {
      const state = await callBridge<LeisureState>('leisure.current')
      set({ state, loading: false })
    } catch (error) {
      set({ loading: false, error: errorMessage(error) })
    }
  },

  async start(activity, plannedMinutes) {
    return runStateCommand(set, 'leisure.start', {
      activity,
      plannedMinutes,
    })
  },

  async pause() {
    return runStateCommand(set, 'leisure.pause')
  },

  async resume() {
    return runStateCommand(set, 'leisure.resume')
  },

  async finish(completionNote) {
    set({ submitting: true, error: null })
    try {
      const receipt = await callBridge<FinishLeisureResult>('leisure.finish', {
        completionNote,
      })
      set({ state: receipt.state, receipt, submitting: false })
      return true
    } catch (error) {
      set({ submitting: false, error: errorMessage(error) })
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

async function runStateCommand(
  set: (partial: Partial<LeisureStore>) => void,
  method: 'leisure.start' | 'leisure.pause' | 'leisure.resume',
  payload?: unknown,
) {
  set({ submitting: true, error: null })
  try {
    const state = await callBridge<LeisureState>(method, payload)
    set({ state, submitting: false })
    return true
  } catch (error) {
    set({ submitting: false, error: errorMessage(error) })
    return false
  }
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : '自由时间操作失败。'
}
