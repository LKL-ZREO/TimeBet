import type {
  BridgeError,
  BridgeEvent,
  BridgeMethod,
  BridgeRequest,
  BridgeResponse,
} from './types'

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(message: unknown): void
        addEventListener(
          type: 'message',
          listener: (event: MessageEvent<unknown>) => void,
        ): void
      }
    }
  }
}

type PendingRequest = {
  resolve(value: unknown): void
  reject(reason: BridgeCallError): void
  timeoutId: ReturnType<typeof setTimeout>
}

const pending = new Map<string, PendingRequest>()
const eventListeners = new Set<(event: BridgeEvent) => void>()
let isListening = false

export class BridgeCallError extends Error {
  readonly code: string

  constructor(error: BridgeError) {
    super(error.message)
    this.name = 'BridgeCallError'
    this.code = error.code
  }
}

export function isDesktopBridgeAvailable() {
  return typeof window.chrome?.webview?.postMessage === 'function'
}

export function callBridge<T>(
  method: BridgeMethod,
  payload?: unknown,
): Promise<T> {
  const webview = window.chrome?.webview
  if (!webview) {
    return Promise.reject(
      new BridgeCallError({
        code: 'bridge_unavailable',
        message: '当前页面未连接 TimeBet 桌面服务。',
      }),
    )
  }

  ensureListener()
  const id = crypto.randomUUID()
  const request: BridgeRequest = { id, method, payload }

  return new Promise<T>((resolve, reject) => {
    const timeoutId = setTimeout(() => {
      pending.delete(id)
      reject(
        new BridgeCallError({
          code: 'bridge_timeout',
          message: '桌面服务响应超时，请重试。',
        }),
      )
    }, 10_000)

    pending.set(id, {
      resolve: (value) => resolve(value as T),
      reject,
      timeoutId,
    })
    webview.postMessage(request)
  })
}

export function onBridgeEvent(listener: (event: BridgeEvent) => void) {
  ensureListener()
  eventListeners.add(listener)
  return () => eventListeners.delete(listener)
}

function ensureListener() {
  if (isListening) return

  const webview = window.chrome?.webview
  if (!webview) return

  webview.addEventListener('message', handleMessage)
  isListening = true
}

function handleMessage(event: MessageEvent<unknown>) {
  const message = event.data
  if (!isRecord(message)) return

  if (typeof message.type === 'string') {
    if (message.type === 'session.changed' && isRecord(message.payload)) {
      const bridgeEvent = message as BridgeEvent
      eventListeners.forEach((listener) => listener(bridgeEvent))
    }
    return
  }

  if (typeof message.id !== 'string' || typeof message.ok !== 'boolean') return

  const response = message as BridgeResponse
  const request = pending.get(response.id)
  if (!request) return

  clearTimeout(request.timeoutId)
  pending.delete(response.id)
  if (response.ok) {
    request.resolve(response.result)
  } else {
    request.reject(
      new BridgeCallError(
        response.error ?? {
          code: 'unknown_error',
          message: '桌面服务返回了未知错误。',
        },
      ),
    )
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}
