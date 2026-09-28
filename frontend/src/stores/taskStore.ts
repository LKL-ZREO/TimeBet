import { create } from 'zustand'
import { callBridge } from '../bridge/webview'
import type { PlanningTask } from '../bridge/types'

type TaskStore = {
  tasks: PlanningTask[]
  loading: boolean
  saving: boolean
  error: string | null
  load(): Promise<void>
  create(
    title: string,
    estimatedMinutes?: number,
    dueDate?: string,
  ): Promise<PlanningTask | null>
  setCompleted(id: string, completed: boolean): Promise<boolean>
  clearError(): void
}

export const useTaskStore = create<TaskStore>((set) => ({
  tasks: [],
  loading: false,
  saving: false,
  error: null,

  async load() {
    set({ loading: true, error: null })
    try {
      const tasks = await callBridge<PlanningTask[]>('task.list')
      set({ tasks: Array.isArray(tasks) ? tasks : [], loading: false })
    } catch (error) {
      set({ loading: false, error: errorMessage(error) })
    }
  },

  async create(title, estimatedMinutes, dueDate) {
    set({ saving: true, error: null })
    try {
      const task = await callBridge<PlanningTask>('task.create', {
        title,
        estimatedMinutes,
        dueDate,
      })
      set((current) => ({
        tasks: [task, ...current.tasks],
        saving: false,
      }))
      return task
    } catch (error) {
      set({ saving: false, error: errorMessage(error) })
      return null
    }
  },

  async setCompleted(id, completed) {
    set({ saving: true, error: null })
    try {
      const task = await callBridge<PlanningTask>('task.set-completed', {
        id,
        completed,
      })
      set((current) => ({
        tasks: completed
          ? current.tasks.filter((item) => item.id !== task.id)
          : current.tasks.map((item) => (item.id === task.id ? task : item)),
        saving: false,
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
  return error instanceof Error ? error.message : '浠诲姟鎿嶄綔澶辫触銆?'
}
