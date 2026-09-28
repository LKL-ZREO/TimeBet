import { create } from 'zustand'
import { callBridge } from '../bridge/webview'
import type { TrackingOverview, TrackingSettings } from '../bridge/types'

type TimelineStore = {
  overview: TrackingOverview | null
  loading: boolean
  saving: boolean
  error: string | null
  load(): Promise<void>
  update(settings: TrackingSettings): Promise<boolean>
  clearError(): void
}

export const useTimelineStore = create<TimelineStore>((set) => ({
  overview: null,
  loading: false,
  saving: false,
  error: null,

  async load() {
    set({ loading: true, error: null })
    try {
      const overview = await callBridge<TrackingOverview>('timeline.today')
      set({ overview, loading: false })
    } catch (error) {
      set({ loading: false, error: errorMessage(error) })
    }
  },

  async update(settings) {
    set({ saving: true, error: null })
    try {
      const saved = await callBridge<TrackingSettings>(
        'tracking.settings.update',
        settings,
      )
      set((current) => ({
        saving: false,
        overview: current.overview
          ? { ...current.overview, settings: saved }
          : current.overview,
      }))
      return true
    } catch (error) {
      set({ saving: false, error: errorMessage(error) })
      return false
    }
  },

  clearError() {
    set({ error: null })
  },
}))

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : '发生了未知错误。'
}
