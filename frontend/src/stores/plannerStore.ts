import { create } from 'zustand'
import { callBridge } from '../bridge/webview'
import type { PlanningTimeBlock, TodayPlan, WeekPlan } from '../bridge/types'

type PlannerStore = {
  overview: TodayPlan | null
  week: WeekPlan | null
  loading: boolean
  weekLoading: boolean
  saving: boolean
  error: string | null
  load(): Promise<void>
  loadWeek(): Promise<void>
  create(
    taskId: string,
    startsAtUtc: string,
    plannedMinutes: number,
  ): Promise<PlanningTimeBlock | null>
  skip(id: string): Promise<boolean>
  move(
    id: string,
    startsAtUtc: string,
    plannedMinutes: number,
  ): Promise<boolean>
  defer(id: string): Promise<boolean>
  remove(id: string): Promise<boolean>
  clearError(): void
}

export const usePlannerStore = create<PlannerStore>((set, get) => ({
  overview: null,
  week: null,
  loading: false,
  weekLoading: false,
  saving: false,
  error: null,

  async load() {
    set({ loading: true, error: null })
    try {
      const overview = await callBridge<TodayPlan>('planner.today')
      set({ overview, loading: false })
    } catch (error) {
      set({ loading: false, error: errorMessage(error) })
    }
  },

  async loadWeek() {
    set({ weekLoading: true, error: null })
    try {
      const week = await callBridge<WeekPlan>('planner.week')
      set({ week, weekLoading: false })
    } catch (error) {
      set({ weekLoading: false, error: errorMessage(error) })
    }
  },

  async create(taskId, startsAtUtc, plannedMinutes) {
    set({ saving: true, error: null })
    try {
      const block = await callBridge<PlanningTimeBlock>('timeblock.create', {
        taskId,
        startsAtUtc,
        plannedMinutes,
      })
      set({ saving: false })
      await refresh(get)
      return block
    } catch (error) {
      set({ saving: false, error: errorMessage(error) })
      return null
    }
  },

  async skip(id) {
    set({ saving: true, error: null })
    try {
      await callBridge<PlanningTimeBlock>('timeblock.skip', { id })
      set({ saving: false })
      await refresh(get)
      return true
    } catch (error) {
      set({ saving: false, error: errorMessage(error) })
      return false
    }
  },

  async move(id, startsAtUtc, plannedMinutes) {
    set({ saving: true, error: null })
    try {
      await callBridge<PlanningTimeBlock>('timeblock.move', {
        id,
        startsAtUtc,
        plannedMinutes,
      })
      set({ saving: false })
      await refresh(get)
      return true
    } catch (error) {
      set({ saving: false, error: errorMessage(error) })
      return false
    }
  },

  async defer(id) {
    set({ saving: true, error: null })
    try {
      await callBridge<PlanningTimeBlock>('timeblock.defer', { id })
      set({ saving: false })
      await refresh(get)
      return true
    } catch (error) {
      set({ saving: false, error: errorMessage(error) })
      return false
    }
  },

  async remove(id) {
    set({ saving: true, error: null })
    try {
      await callBridge('timeblock.delete', { id })
      set({ saving: false })
      await refresh(get)
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

async function refresh(get: () => PlannerStore) {
  const current = get()
  const refreshes: Promise<void>[] = [current.load()]
  if (current.week) refreshes.push(current.loadWeek())
  await Promise.all(refreshes)
}

function errorMessage(error: unknown) {
  if (!(error instanceof Error)) return '排程操作失败，请重试。'
  if (error.message.includes('overlaps another active plan')) {
    return '这个时段已有其他安排，请选择新的日期或时间。'
  }
  if (error.message.includes('Only planned time blocks can be moved')) {
    return '只有尚未执行的时间块可以调整。'
  }
  if (error.message.includes('active or has already changed')) {
    return '这个时间块正在执行或已经发生变化，请刷新后再试。'
  }
  return error.message
}
