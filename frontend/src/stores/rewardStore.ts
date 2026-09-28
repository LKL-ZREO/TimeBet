import { create } from 'zustand'
import { callBridge } from '../bridge/webview'
import type { RedeemRewardResult, RewardCatalog } from '../bridge/types'
import { useSessionStore } from './sessionStore'

type RewardInput = {
  name: string
  description: string
  costSeconds: number
}

type RewardStore = {
  catalog: RewardCatalog | null
  receipt: RedeemRewardResult | null
  loading: boolean
  submitting: boolean
  error: string | null
  load(): Promise<void>
  create(input: RewardInput): Promise<boolean>
  update(id: string, input: RewardInput): Promise<boolean>
  setActive(id: string, isActive: boolean): Promise<boolean>
  redeem(id: string): Promise<boolean>
  clearError(): void
  dismissReceipt(): void
}

export const useRewardStore = create<RewardStore>((set) => ({
  catalog: null,
  receipt: null,
  loading: false,
  submitting: false,
  error: null,

  async load() {
    set({ loading: true, error: null })
    try {
      const catalog = await callBridge<RewardCatalog>('reward.catalog')
      set({ catalog, loading: false })
      syncBalance(catalog.balanceSeconds)
    } catch (error) {
      set({ loading: false, error: errorMessage(error) })
    }
  },

  async create(input) {
    return saveCatalog(set, 'reward.create', input)
  },

  async update(id, input) {
    return saveCatalog(set, 'reward.update', { id, ...input })
  },

  async setActive(id, isActive) {
    return saveCatalog(set, 'reward.set-active', { id, isActive })
  },

  async redeem(id) {
    set({ submitting: true, error: null })
    try {
      const receipt = await callBridge<RedeemRewardResult>('reward.redeem', {
        id,
      })
      set({ catalog: receipt.catalog, receipt, submitting: false })
      syncBalance(receipt.catalog.balanceSeconds)
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

async function saveCatalog(
  set: (partial: Partial<RewardStore>) => void,
  method: 'reward.create' | 'reward.update' | 'reward.set-active',
  payload: unknown,
) {
  set({ submitting: true, error: null })
  try {
    const catalog = await callBridge<RewardCatalog>(method, payload)
    set({ catalog, submitting: false })
    syncBalance(catalog.balanceSeconds)
    return true
  } catch (error) {
    set({ submitting: false, error: errorMessage(error) })
    return false
  }
}

function syncBalance(balanceSeconds: number) {
  useSessionStore.setState((current) =>
    current.state ? { state: { ...current.state, balanceSeconds } } : current,
  )
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : '发生了未知错误。'
}
