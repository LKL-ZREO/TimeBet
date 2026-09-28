import { create } from 'zustand'
import { callBridge, onBridgeEvent } from '../bridge/webview'
import type {
  FinishSessionResult,
  SessionTaskInput,
  StudyState,
} from '../bridge/types'

type SessionStore = {
  state: StudyState | null
  receipt: FinishSessionResult | null
  loading: boolean
  submitting: boolean
  error: string | null
  initialize(): Promise<void>
  start(
    goal: string,
    captureScreenshots: boolean,
    taskId?: string,
    timeBlockId?: string,
  ): Promise<boolean>
  pause(): Promise<boolean>
  resume(): Promise<boolean>
  finish(completionNote: string, tasks: SessionTaskInput[]): Promise<boolean>
  clearError(): void
  dismissReceipt(): void
}

let stopListening: (() => void) | undefined

export const useSessionStore = create<SessionStore>((set) => ({
  state: null,
  receipt: null,
  loading: true,
  submitting: false,
  error: null,

  async initialize() {
    stopListening?.()
    stopListening = onBridgeEvent((event) => set({ state: event.payload }))
    set({ loading: true, error: null })
    try {
      const state = await callBridge<StudyState>('session.current')
      set({ state, loading: false })
    } catch (error) {
      set({ loading: false, error: errorMessage(error) })
    }
  },

  async start(goal, captureScreenshots, taskId, timeBlockId) {
    return runStateCommand(set, 'session.start', {
      goal,
      captureScreenshots,
      taskId,
      timeBlockId,
    })
  },

  async pause() {
    return runStateCommand(set, 'session.pause')
  },

  async resume() {
    return runStateCommand(set, 'session.resume')
  },

  async finish(completionNote, tasks) {
    set({ submitting: true, error: null })
    try {
      const receipt = await callBridge<FinishSessionResult>('session.finish', {
        completionNote,
        tasks,
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
  set: (partial: Partial<SessionStore>) => void,
  method: 'session.start' | 'session.pause' | 'session.resume',
  payload?: unknown,
) {
  set({ submitting: true, error: null })
  try {
    const state = await callBridge<StudyState>(method, payload)
    set({ state, submitting: false })
    return true
  } catch (error) {
    set({ submitting: false, error: errorMessage(error) })
    return false
  }
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : '发生了未知错误。'
}
