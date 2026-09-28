import { create } from 'zustand'
import { callBridge } from '../bridge/webview'
import type {
  AnalyticsOverview,
  DailyReview,
  LedgerEntry,
  SummaryExportResult,
} from '../bridge/types'
import { useSessionStore } from './sessionStore'

type AnalyticsStore = {
  overview: AnalyticsOverview | null
  review: DailyReview | null
  entries: LedgerEntry[]
  loading: boolean
  exporting: boolean
  exportedFileName: string | null
  error: string | null
  load(): Promise<void>
  exportSummary(): Promise<void>
  clearError(): void
}

export const useAnalyticsStore = create<AnalyticsStore>((set) => ({
  overview: null,
  review: null,
  entries: [],
  loading: false,
  exporting: false,
  exportedFileName: null,
  error: null,

  async load() {
    set({ loading: true, error: null })
    try {
      const [overview, review, entries] = await Promise.all([
        callBridge<AnalyticsOverview>('analytics.overview'),
        callBridge<DailyReview>('review.today.get'),
        callBridge<LedgerEntry[]>('ledger.entries'),
      ])
      set({ overview, review, entries, loading: false })
      useSessionStore.setState((current) =>
        current.state
          ? {
              state: {
                ...current.state,
                balanceSeconds: overview.ledger.balanceSeconds,
              },
            }
          : current,
      )
    } catch (error) {
      set({ loading: false, error: errorMessage(error) })
    }
  },

  async exportSummary() {
    set({ exporting: true, exportedFileName: null, error: null })
    try {
      const result = await callBridge<SummaryExportResult>('export.summary')
      set({
        exporting: false,
        exportedFileName: result.saved ? result.fileName : null,
      })
    } catch (error) {
      set({ exporting: false, error: errorMessage(error) })
    }
  },

  clearError() {
    set({ error: null })
  },
}))

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : '统计数据读取失败。'
}
