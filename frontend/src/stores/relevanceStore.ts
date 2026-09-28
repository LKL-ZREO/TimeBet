import { create } from 'zustand'
import { callBridge } from '../bridge/webview'
import type { TaskRelevanceSnapshot } from '../bridge/types'

type RelevanceStore = {
  snapshot: TaskRelevanceSnapshot | null
  loading: boolean
  error: string | null
  load(): Promise<void>
  clear(): void
}

export const useRelevanceStore = create<RelevanceStore>((set) => ({
  snapshot: null,
  loading: false,
  error: null,

  async load() {
    set({ loading: true, error: null })
    try {
      const snapshot = await callBridge<TaskRelevanceSnapshot | null>(
        'session.relevance.current',
      )
      set({ snapshot, loading: false })
    } catch (error) {
      set({ loading: false, error: errorMessage(error) })
    }
  },

  clear() {
    set({ snapshot: null, loading: false, error: null })
  },
}))

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : '无法读取当前任务关联度。'
}
