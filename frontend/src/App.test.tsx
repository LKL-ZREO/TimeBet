import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import type {
  AnalyticsOverview,
  BridgeRequest,
  ChallengeOverview,
  ChallengeRound,
  FinishSessionResult,
  StudyState,
  TrackingOverview,
  TrackingSettings,
  RewardCatalog,
  LedgerEntry,
  PlanningTask,
  PlanningTimeBlock,
  TodayPlan,
  WeekPlan,
  TaskRelevanceSnapshot,
} from './bridge/types'
import { useSessionStore } from './stores/sessionStore'
import { useTimelineStore } from './stores/timelineStore'
import { useRewardStore } from './stores/rewardStore'
import { useAnalyticsStore } from './stores/analyticsStore'
import { useChallengeStore } from './stores/challengeStore'
import { useTaskStore } from './stores/taskStore'
import { usePlannerStore } from './stores/plannerStore'
import { useRelevanceStore } from './stores/relevanceStore'
import App from './App'

const now = '2026-08-12T08:00:00.000Z'
const listeners = new Set<(event: MessageEvent<unknown>) => void>()
let currentState: StudyState
let trackingOverview: TrackingOverview
let rewardCatalog: RewardCatalog
let analyticsOverview: AnalyticsOverview
let ledgerEntries: LedgerEntry[]
let exportRequested: boolean
let challengeOverview: ChallengeOverview
let planningTasks: PlanningTask[]
let planningBlocks: PlanningTimeBlock[]
let taskRelevance: TaskRelevanceSnapshot

const webview = {
  postMessage(message: unknown) {
    const request = message as BridgeRequest
    const result = dispatch(request)
    queueMicrotask(() =>
      listeners.forEach((listener) =>
        listener(
          new MessageEvent('message', {
            data: { id: request.id, ok: true, result },
          }),
        ),
      ),
    )
  },
  addEventListener(
    _type: 'message',
    listener: (event: MessageEvent<unknown>) => void,
  ) {
    listeners.add(listener)
  },
}

beforeAll(() => {
  window.chrome = { webview }
  window.matchMedia = () => ({ matches: true }) as MediaQueryList
})

beforeEach(() => {
  currentState = emptyState()
  trackingOverview = sampleTimeline()
  rewardCatalog = sampleRewardCatalog()
  analyticsOverview = sampleAnalytics()
  ledgerEntries = sampleLedgerEntries()
  exportRequested = false
  challengeOverview = sampleChallenge(false)
  planningTasks = []
  planningBlocks = []
  taskRelevance = sampleTaskRelevance()
  useSessionStore.setState({
    state: null,
    receipt: null,
    loading: true,
    submitting: false,
    error: null,
  })
  useTimelineStore.setState({
    overview: null,
    loading: false,
    saving: false,
    error: null,
  })
  useRewardStore.setState({
    catalog: null,
    receipt: null,
    loading: false,
    submitting: false,
    error: null,
  })
  useAnalyticsStore.setState({
    overview: null,
    entries: [],
    loading: false,
    exporting: false,
    exportedFileName: null,
    error: null,
  })
  useChallengeStore.setState({
    overview: null,
    receipt: null,
    pendingRound: null,
    loading: false,
    saving: false,
    spinning: false,
    settling: false,
    error: null,
  })
  useTaskStore.setState({
    tasks: [],
    loading: false,
    saving: false,
    error: null,
  })
  usePlannerStore.setState({
    overview: null,
    week: null,
    loading: false,
    weekLoading: false,
    saving: false,
    error: null,
  })
  useRelevanceStore.setState({
    snapshot: null,
    loading: false,
    error: null,
  })
})

afterEach(cleanup)

describe('TimeBet application shell', () => {
  it('loads the local account and shows the start form', async () => {
    render(<App />)

    expect(
      screen.getByRole('heading', { name: '今日专注' }),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('当前计时')).toHaveTextContent('00:00:00')
    expect(
      await screen.findByLabelText('这段时间准备推进什么？'),
    ).toBeInTheDocument()
    expect(screen.getAllByText('0 秒').length).toBeGreaterThanOrEqual(2)
  })

  it('starts, pauses and resumes a study session', async () => {
    const user = userEvent.setup()
    render(<App />)

    const goal = await screen.findByLabelText('这段时间准备推进什么？')
    await user.type(goal, '完成高数极限章节')
    await user.click(screen.getByRole('button', { name: '开始专注' }))

    expect(await screen.findByText('学习计时已开始')).toBeInTheDocument()
    expect(
      await screen.findByRole('heading', { name: '完成高数极限章节' }),
    ).toBeInTheDocument()
    expect(await screen.findByText('高关联')).toBeInTheDocument()
    expect(screen.getByText('高数极限错题.docx - Word')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '暂停' }))
    expect(await screen.findByText('计时已暂停')).toBeInTheDocument()
    expect(document.querySelector('.focus-character img')).toHaveAttribute(
      'src',
      '/assets/illustrations/time-bank-trainee-resting.png',
    )
    expect(
      (await screen.findAllByText('已暂停')).length,
    ).toBeGreaterThanOrEqual(1)

    await user.click(screen.getByRole('button', { name: '继续' }))
    expect(document.querySelector('.focus-character img')).toHaveAttribute(
      'src',
      '/assets/illustrations/time-bank-trainee-focused.png',
    )
    expect(await screen.findByText('正在积累')).toBeInTheDocument()
  })

  it('creates a task and starts a linked focus session', async () => {
    const user = userEvent.setup()
    const taskTitle = '整理高数错题'
    render(<App />)

    await user.type(await screen.findByLabelText('新任务标题'), taskTitle)
    await user.type(screen.getByLabelText('预计分钟'), '45')
    await user.click(screen.getByRole('button', { name: '添加任务' }))

    expect(await screen.findByText(taskTitle)).toBeInTheDocument()
    expect(screen.getByText(/预计 45 分钟/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '开始' }))

    expect(
      await screen.findByRole('heading', { name: taskTitle }),
    ).toBeInTheDocument()
    expect(currentState.session?.taskId).toBe(planningTasks[0].id)
  })

  it('schedules a task into today and starts its time block', async () => {
    const user = userEvent.setup()
    const task = samplePlanningTask()
    planningTasks = [task]
    render(<App />)

    await user.click(await screen.findByRole('button', { name: '安排' }))
    await user.type(screen.getByLabelText(`安排${task.title}分钟`), '30')
    await user.click(screen.getByRole('button', { name: '加入计划' }))

    expect(
      await screen.findByText('计划 30 分钟 · 等待执行'),
    ).toBeInTheDocument()
    const blockRow = document.querySelector('.time-block-row')
    expect(blockRow).not.toBeNull()
    await user.click(
      within(blockRow as HTMLElement).getByRole('button', { name: '开始' }),
    )

    expect(currentState.session?.taskId).toBe(task.id)
    expect(currentState.session?.timeBlockId).toBe(planningBlocks[0].id)
  })

  it('moves and defers a planned block from the weekly view', async () => {
    const user = userEvent.setup()
    const task = samplePlanningTask()
    planningTasks = [task]
    planningBlocks = [
      {
        id: '77777777-7777-7777-7777-777777777777',
        taskId: task.id,
        taskTitle: task.title,
        dueDate: task.dueDate,
        startsAtUtc: '2026-08-12T09:00:00.000Z',
        plannedSeconds: 45 * 60,
        actualSeconds: 0,
        status: 'planned',
        resolvedAtUtc: null,
      },
    ]
    render(<App />)

    await user.click(screen.getByRole('button', { name: '本周计划' }))
    expect(
      await screen.findByRole('heading', { name: '本周执行地图' }),
    ).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '调整' }))
    const dateInput = screen.getByLabelText(`调整${task.title}日期`)
    const timeInput = screen.getByLabelText(`调整${task.title}时间`)
    const minutesInput = screen.getByLabelText(`调整${task.title}分钟`)
    await user.clear(dateInput)
    await user.type(dateInput, '2026-08-13')
    await user.clear(timeInput)
    await user.type(timeInput, '10:00')
    await user.clear(minutesInput)
    await user.type(minutesInput, '30')
    await user.click(screen.getByRole('button', { name: '保存调整' }))

    await waitFor(() =>
      expect(planningBlocks[0].startsAtUtc).toContain('2026-08-13'),
    )
    await user.click(await screen.findByRole('button', { name: '明天' }))
    await waitFor(() =>
      expect(planningBlocks[0].startsAtUtc).toContain('2026-08-14'),
    )
  })

  it('drags a weekly block, rejects conflicts and can undo the move', async () => {
    const user = userEvent.setup()
    const task = samplePlanningTask()
    planningTasks = [task]
    planningBlocks = [
      {
        id: '77777777-7777-7777-7777-777777777777',
        taskId: task.id,
        taskTitle: task.title,
        dueDate: task.dueDate,
        startsAtUtc: '2026-08-12T09:00:00.000Z',
        plannedSeconds: 45 * 60,
        actualSeconds: 0,
        status: 'planned',
        resolvedAtUtc: null,
      },
      {
        id: '88888888-8888-8888-8888-888888888888',
        taskId: task.id,
        taskTitle: '英语听力训练',
        dueDate: task.dueDate,
        startsAtUtc: '2026-08-13T09:00:00.000Z',
        plannedSeconds: 45 * 60,
        actualSeconds: 0,
        status: 'planned',
        resolvedAtUtc: null,
      },
    ]
    render(<App />)

    await user.click(screen.getByRole('button', { name: '本周计划' }))
    await screen.findByRole('heading', { name: '本周执行地图' })
    const dataTransfer = {
      effectAllowed: 'none',
      dropEffect: 'none',
      setData() {},
    } as unknown as DataTransfer
    const draggedBlock = () =>
      screen.getByText(task.title).closest('.week-block') as HTMLElement

    const conflictDay = screen.getByLabelText('周四 8/13计划')
    fireEvent.dragStart(draggedBlock(), { dataTransfer })
    fireEvent.dragEnter(conflictDay, { dataTransfer })
    expect(conflictDay).toHaveAttribute('data-drop-state', 'conflict')
    fireEvent.drop(conflictDay, { dataTransfer })
    expect(await screen.findByText('该时段已有安排')).toBeInTheDocument()
    expect(planningBlocks[0].startsAtUtc).toContain('2026-08-12')

    const validDay = screen.getByLabelText('周五 8/14计划')
    fireEvent.dragStart(draggedBlock(), { dataTransfer })
    fireEvent.dragEnter(validDay, { dataTransfer })
    expect(validDay).toHaveAttribute('data-drop-state', 'valid')
    fireEvent.drop(validDay, { dataTransfer })
    await waitFor(() =>
      expect(planningBlocks[0].startsAtUtc).toContain('2026-08-14'),
    )
    expect(await screen.findByText('时间块已移动')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '撤销' }))
    await waitFor(() =>
      expect(planningBlocks[0].startsAtUtc).toContain('2026-08-12'),
    )
    expect(await screen.findByText('移动已撤销')).toBeInTheDocument()
  })

  it('finishes a session and shows the time deposit receipt', async () => {
    const user = userEvent.setup()
    currentState = runningState()
    render(<App />)

    await screen.findByRole('heading', { name: '完成高数极限章节' })
    await user.click(screen.getByRole('button', { name: '完成并存入' }))
    await user.type(
      screen.getByLabelText('完成内容或未完成原因 必填'),
      '完成例题并整理错题',
    )
    await user.type(screen.getByLabelText('任务 1'), '完成 12 道例题')
    await user.click(screen.getByRole('button', { name: '确认存入' }))

    expect(
      await screen.findByRole('heading', { name: '+ 1 分钟' }),
    ).toBeInTheDocument()
    expect(screen.getByText('账户可用时间')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '收好凭证' }))
    await waitFor(() =>
      expect(screen.queryByText('时间存款已到账')).not.toBeInTheDocument(),
    )
  })

  it('switches between application sections and toggles the theme', async () => {
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: '奖励商店' }))
    expect(
      screen.getByRole('heading', { name: '奖励商店', level: 1 }),
    ).toBeInTheDocument()
    expect(await screen.findByText('一杯奶茶')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '切换到深色主题' }))
    expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
  })

  it('creates and deactivates a custom reward', async () => {
    const user = userEvent.setup()
    rewardCatalog = { ...sampleRewardCatalog(), rewards: [] }
    render(<App />)

    await user.click(screen.getByRole('button', { name: '奖励商店' }))
    await user.click(await screen.findByRole('button', { name: '新建奖励' }))
    await user.type(screen.getByLabelText('奖励名称 必填'), '看一场电影')
    await user.clear(screen.getByLabelText('消耗时间 分钟'))
    await user.type(screen.getByLabelText('消耗时间 分钟'), '60')
    await user.click(screen.getByRole('button', { name: '保存奖励' }))

    expect(await screen.findByText('奖励已创建')).toBeInTheDocument()
    expect(
      await screen.findByRole('heading', { name: '看一场电影' }),
    ).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '编辑看一场电影' }))
    const nameInput = screen.getByLabelText('奖励名称 必填')
    await user.clear(nameInput)
    await user.type(nameInput, '周末电影之夜')
    await user.click(screen.getByRole('button', { name: '保存奖励' }))

    expect(await screen.findByText('奖励已更新')).toBeInTheDocument()
    expect(
      await screen.findByRole('heading', { name: '周末电影之夜' }),
    ).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '停用周末电影之夜' }))
    expect(await screen.findByText('奖励已停用')).toBeInTheDocument()
    expect(
      await screen.findByRole('button', { name: '重新启用周末电影之夜' }),
    ).toBeInTheDocument()
  })

  it('redeems a reward and updates the time balance', async () => {
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: '奖励商店' }))
    await user.click(await screen.findByRole('button', { name: '兑换' }))
    const dialog = screen.getByRole('dialog', { name: '一杯奶茶' })
    const confirm = within(dialog).getByRole('button', { name: '确认兑换' })
    expect(confirm).toBeInTheDocument()
    await user.click(confirm)

    expect(await screen.findByText('奖励已兑换')).toBeInTheDocument()
    const receiptDialog = screen.getByRole('dialog', { name: '一杯奶茶' })
    expect(
      within(receiptDialog).getByText('30 分钟', { selector: 'strong' }),
    ).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '收好凭证' }))
    await waitFor(() =>
      expect(screen.queryByText('奖励已兑换')).not.toBeInTheDocument(),
    )
  })

  it('shows foreground activities on the study timeline', async () => {
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: '学习时间轴' }))

    expect(await screen.findAllByText('Visual Studio Code')).toHaveLength(1)
    expect(
      screen.queryByText('notes.md - Visual Studio Code'),
    ).not.toBeInTheDocument()
    expect(screen.queryByText('离开电脑')).not.toBeInTheDocument()
    expect(screen.getByText('2 个应用')).toBeInTheDocument()
    expect(screen.getByText('空闲 4 分钟')).toBeInTheDocument()
    expect(screen.getByText('10 分钟')).toBeInTheDocument()
    expect(screen.getByText('1 分钟')).toBeInTheDocument()
  })

  it('shows trends and ledger entries and starts a summary export', async () => {
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: '数据统计' }))

    expect(await screen.findByText('近 7 天有效时长')).toBeInTheDocument()
    expect(screen.getByText('Visual Studio Code')).toBeInTheDocument()
    expect(screen.getByText('完成高数复盘')).toBeInTheDocument()
    expect(screen.getByText('+ 1 小时 30 分钟')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '导出摘要' }))
    await waitFor(() => expect(exportRequested).toBe(true))
    expect(screen.getByText('已保存 TimeBet-学习摘要.csv')).toBeInTheDocument()
  })

  it('enables screenshots and saves local tracking settings', async () => {
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: '设置' }))
    const screenshotToggle = await screen.findByRole('checkbox', {
      name: '定时截图',
    })
    expect(screenshotToggle).not.toBeChecked()

    await user.click(screenshotToggle)
    await user.selectOptions(screen.getByLabelText('截图间隔'), '10')
    await user.click(screen.getByRole('button', { name: '保存设置' }))

    await waitFor(() =>
      expect(trackingOverview.settings.captureScreenshots).toBe(true),
    )
    expect(trackingOverview.settings.screenshotIntervalMinutes).toBe(10)
    expect(await screen.findByText('隐私设置已保存')).toBeInTheDocument()
  })

  it('opts into Star Wish and settles a transparent challenge result', async () => {
    const user = userEvent.setup()
    render(<App />)

    await user.click(screen.getByRole('button', { name: '时间挑战' }))
    expect(
      await screen.findByRole('heading', { name: '开启星愿机' }),
    ).toBeInTheDocument()
    expect(screen.getByText('95.5%')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '了解并开启' }))

    expect(await screen.findByText('星愿机已开启')).toBeInTheDocument()
    expect(
      await screen.findByRole('heading', { name: '星愿机' }),
    ).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '增加投入一分钟' }))
    await user.click(screen.getByRole('button', { name: '开启星愿' }))

    expect(await screen.findByText('正在揭示结果')).toBeInTheDocument()
    expect(
      screen.queryByRole('dialog', { name: '小小幸运' }),
    ).not.toBeInTheDocument()
    expect(document.querySelector('.wish-machine')).toHaveAttribute(
      'data-settling',
      'true',
    )

    const receipt = await screen.findByRole(
      'dialog',
      { name: '小小幸运' },
      { timeout: 3_000 },
    )
    expect(within(receipt).getByText('+ 1 分钟')).toBeInTheDocument()
    expect(within(receipt).getByText('1 小时 1 分钟')).toBeInTheDocument()
    await user.click(within(receipt).getByRole('button', { name: '收好结果' }))
    expect(screen.getByText('2 / 3')).toBeInTheDocument()
  })

  it('can disable Star Wish from settings without deleting history', async () => {
    const user = userEvent.setup()
    challengeOverview = sampleChallenge(true)
    render(<App />)

    await user.click(screen.getByRole('button', { name: '设置' }))
    const toggle = await screen.findByRole('checkbox', { name: '启用星愿机' })
    expect(toggle).toBeChecked()
    await user.click(toggle)

    await waitFor(() => expect(challengeOverview.enabled).toBe(false))
    expect(challengeOverview.recentRounds).toHaveLength(1)
  })
})

function dispatch(request: BridgeRequest) {
  switch (request.method) {
    case 'session.current':
      return currentState
    case 'session.start': {
      const payload = request.payload as {
        goal: string
        captureScreenshots: boolean
        taskId?: string
        timeBlockId?: string
      }
      currentState = runningState(
        payload.goal,
        payload.captureScreenshots,
        payload.taskId,
        payload.timeBlockId,
      )
      return currentState
    }
    case 'session.pause':
      currentState = {
        ...currentState,
        session: currentState.session
          ? { ...currentState.session, status: 'paused' }
          : null,
      }
      return currentState
    case 'session.resume':
      currentState = {
        ...currentState,
        session: currentState.session
          ? { ...currentState.session, status: 'running' }
          : null,
      }
      return currentState
    case 'session.finish': {
      const completedSession = {
        ...(currentState.session ?? runningState().session!),
        status: 'completed' as const,
        endedAtUtc: '2026-08-12T08:01:05.000Z',
        effectiveDurationSeconds: 65,
        completionNote: '完成例题并整理错题',
      }
      if (completedSession.timeBlockId) {
        planningBlocks = planningBlocks.map((block) =>
          block.id === completedSession.timeBlockId
            ? {
                ...block,
                actualSeconds: 65,
                status: 'completed',
                resolvedAtUtc: '2026-08-12T08:01:05.000Z',
              }
            : block,
        )
      }
      currentState = {
        session: null,
        balanceSeconds: 65,
        asOfUtc: '2026-08-12T08:01:05.000Z',
      }
      return {
        state: currentState,
        completedSession,
      } satisfies FinishSessionResult
    }
    case 'session.relevance.current':
      return currentState.session?.status === 'running' ? taskRelevance : null
    case 'ledger.balance':
      return { balanceSeconds: currentState.balanceSeconds }
    case 'task.list':
      return planningTasks.filter((task) => task.status === 'active')
    case 'task.create': {
      const input = request.payload as {
        title: string
        estimatedMinutes?: number
        dueDate?: string
      }
      const task: PlanningTask = {
        id: crypto.randomUUID(),
        title: input.title,
        estimatedSeconds: input.estimatedMinutes
          ? input.estimatedMinutes * 60
          : null,
        actualSeconds: 0,
        dueDate: input.dueDate ?? null,
        status: 'active',
        createdAtUtc: now,
        completedAtUtc: null,
      }
      planningTasks = [task, ...planningTasks]
      return task
    }
    case 'task.set-completed': {
      const input = request.payload as { id: string; completed: boolean }
      const current = planningTasks.find((task) => task.id === input.id)!
      const updated: PlanningTask = {
        ...current,
        status: input.completed ? 'completed' : 'active',
        completedAtUtc: input.completed ? now : null,
      }
      planningTasks = planningTasks.map((task) =>
        task.id === updated.id ? updated : task,
      )
      return updated
    }
    case 'planner.today':
      return sampleTodayPlan()
    case 'planner.week':
      return sampleWeekPlan()
    case 'timeblock.create': {
      const input = request.payload as {
        taskId: string
        startsAtUtc: string
        plannedMinutes: number
      }
      const task = planningTasks.find((item) => item.id === input.taskId)!
      const block: PlanningTimeBlock = {
        id: crypto.randomUUID(),
        taskId: task.id,
        taskTitle: task.title,
        dueDate: task.dueDate,
        startsAtUtc: input.startsAtUtc,
        plannedSeconds: input.plannedMinutes * 60,
        actualSeconds: 0,
        status: 'planned',
        resolvedAtUtc: null,
      }
      planningBlocks = [...planningBlocks, block]
      return block
    }
    case 'timeblock.skip': {
      const input = request.payload as { id: string }
      const current = planningBlocks.find((block) => block.id === input.id)!
      const updated: PlanningTimeBlock = {
        ...current,
        status: 'skipped',
        resolvedAtUtc: now,
      }
      planningBlocks = planningBlocks.map((block) =>
        block.id === updated.id ? updated : block,
      )
      return updated
    }
    case 'timeblock.move': {
      const input = request.payload as {
        id: string
        startsAtUtc: string
        plannedMinutes: number
      }
      const current = planningBlocks.find((block) => block.id === input.id)!
      const updated: PlanningTimeBlock = {
        ...current,
        startsAtUtc: input.startsAtUtc,
        plannedSeconds: input.plannedMinutes * 60,
      }
      planningBlocks = planningBlocks.map((block) =>
        block.id === updated.id ? updated : block,
      )
      return updated
    }
    case 'timeblock.defer': {
      const input = request.payload as { id: string }
      const current = planningBlocks.find((block) => block.id === input.id)!
      const startsAt = new Date(current.startsAtUtc)
      startsAt.setUTCDate(startsAt.getUTCDate() + 1)
      const updated: PlanningTimeBlock = {
        ...current,
        startsAtUtc: startsAt.toISOString(),
      }
      planningBlocks = planningBlocks.map((block) =>
        block.id === updated.id ? updated : block,
      )
      return updated
    }
    case 'timeline.today':
      return trackingOverview
    case 'tracking.settings.get':
      return trackingOverview.settings
    case 'tracking.settings.update':
      trackingOverview = {
        ...trackingOverview,
        settings: request.payload as TrackingSettings,
      }
      return trackingOverview.settings
    case 'reward.catalog':
      return rewardCatalog
    case 'reward.create': {
      const input = request.payload as {
        name: string
        description: string
        costSeconds: number
      }
      rewardCatalog = {
        ...rewardCatalog,
        rewards: [
          {
            id: crypto.randomUUID(),
            ...input,
            isActive: true,
            createdAtUtc: now,
            updatedAtUtc: now,
          },
          ...rewardCatalog.rewards,
        ],
      }
      return rewardCatalog
    }
    case 'reward.update': {
      const input = request.payload as {
        id: string
        name: string
        description: string
        costSeconds: number
      }
      rewardCatalog = {
        ...rewardCatalog,
        rewards: rewardCatalog.rewards.map((reward) =>
          reward.id === input.id ? { ...reward, ...input } : reward,
        ),
      }
      return rewardCatalog
    }
    case 'reward.set-active': {
      const input = request.payload as { id: string; isActive: boolean }
      rewardCatalog = {
        ...rewardCatalog,
        rewards: rewardCatalog.rewards.map((reward) =>
          reward.id === input.id
            ? { ...reward, isActive: input.isActive }
            : reward,
        ),
      }
      return rewardCatalog
    }
    case 'reward.redeem': {
      const input = request.payload as { id: string }
      const reward = rewardCatalog.rewards.find((item) => item.id === input.id)!
      const redemption = {
        id: crypto.randomUUID(),
        rewardId: reward.id,
        rewardName: reward.name,
        costSeconds: reward.costSeconds,
        redeemedAtUtc: now,
      }
      rewardCatalog = {
        ...rewardCatalog,
        balanceSeconds: rewardCatalog.balanceSeconds - reward.costSeconds,
        recentRedemptions: [redemption, ...rewardCatalog.recentRedemptions],
      }
      return { catalog: rewardCatalog, redemption }
    }
    case 'analytics.overview':
      return analyticsOverview
    case 'ledger.entries':
      return ledgerEntries
    case 'export.summary':
      exportRequested = true
      return { saved: true, fileName: 'TimeBet-学习摘要.csv' }
    case 'challenge.overview':
      return challengeOverview
    case 'challenge.settings.update': {
      const payload = request.payload as { enabled: boolean }
      challengeOverview = { ...challengeOverview, enabled: payload.enabled }
      return challengeOverview
    }
    case 'challenge.play': {
      const payload = request.payload as {
        roundId: string
        wagerSeconds: number
      }
      const round = sampleChallengeRound(payload.roundId, payload.wagerSeconds)
      challengeOverview = {
        ...challengeOverview,
        balanceSeconds: challengeOverview.balanceSeconds + round.netSeconds,
        roundsPlayedToday: challengeOverview.roundsPlayedToday + 1,
        roundsRemainingToday: challengeOverview.roundsRemainingToday - 1,
        recentRounds: [round, ...challengeOverview.recentRounds],
      }
      return { overview: challengeOverview, round }
    }
  }
}

function sampleChallenge(enabled: boolean): ChallengeOverview {
  return {
    enabled,
    balanceSeconds: 3600,
    hasActiveStudySession: false,
    roundsPlayedToday: 0,
    roundsRemainingToday: 3,
    minimumBalanceSeconds: 600,
    minimumWagerSeconds: 60,
    maximumWagerSeconds: 360,
    dailyRoundLimit: 3,
    maximumWagerBalanceShare: 0.1,
    expectedReturn: 0.955,
    probabilityVersion: 'star-wish-v1',
    probabilityTiers: [
      {
        code: 'starlight-fades',
        label: '星光熄灭',
        probability: 0.4,
        payoutMultiplier: 0,
        symbols: ['moon', 'cloud', 'book'],
      },
      {
        code: 'ember-remains',
        label: '留下火种',
        probability: 0.2,
        payoutMultiplier: 0.5,
        symbols: ['spark', 'book', 'spark'],
      },
      {
        code: 'small-fortune',
        label: '小小幸运',
        probability: 0.25,
        payoutMultiplier: 1.5,
        symbols: ['book', 'star', 'book'],
      },
      {
        code: 'double-progress',
        label: '双倍进展',
        probability: 0.12,
        payoutMultiplier: 3,
        symbols: ['star', 'medal', 'star'],
      },
      {
        code: 'wish-arrives',
        label: '星愿降临',
        probability: 0.03,
        payoutMultiplier: 4,
        symbols: ['crown', 'crown', 'crown'],
      },
    ],
    recentRounds: [sampleChallengeRound(crypto.randomUUID(), 60)],
  }
}

function sampleChallengeRound(
  id: string,
  wagerSeconds: number,
): ChallengeRound {
  return {
    id,
    wagerSeconds,
    payoutSeconds: Math.round(wagerSeconds * 1.5),
    netSeconds: Math.round(wagerSeconds * 0.5),
    outcomeCode: 'small-fortune',
    outcomeLabel: '小小幸运',
    symbols: ['book', 'star', 'book'],
    playedAtUtc: now,
  }
}

function sampleAnalytics(): AnalyticsOverview {
  return {
    generatedAtUtc: now,
    todayStartedAtUtc: '2026-08-11T16:00:00.000Z',
    weekStartedAtUtc: '2026-08-09T16:00:00.000Z',
    todayStudySeconds: 90 * 60,
    weekStudySeconds: 8 * 3600,
    todayCompletedSessions: 2,
    weekCompletedSessions: 9,
    ledger: {
      balanceSeconds: 3600,
      earnedSeconds: 12 * 3600,
      spentSeconds: 11 * 3600,
    },
    dailyTrend: [30, 45, 0, 70, 90, 120, 90].map((minutes, index) => ({
      date: `2026-08-${String(index + 6).padStart(2, '0')}`,
      dayStartedAtUtc: `2026-08-${String(index + 5).padStart(2, '0')}T16:00:00.000Z`,
      studySeconds: minutes * 60,
      completedSessions: minutes === 0 ? 0 : 1,
    })),
    applicationUsage: [
      {
        processName: 'Visual Studio Code',
        durationSeconds: 50 * 60,
        share: 0.56,
      },
      {
        processName: 'Microsoft Edge',
        durationSeconds: 40 * 60,
        share: 0.44,
      },
    ],
  }
}

function sampleLedgerEntries(): LedgerEntry[] {
  return [
    {
      id: '55555555-5555-5555-5555-555555555555',
      type: 'earn',
      amountSeconds: 90 * 60,
      sourceType: 'study-session',
      sourceId: '11111111-1111-1111-1111-111111111111',
      note: '完成高数复盘',
      occurredAtUtc: now,
    },
  ]
}

function sampleRewardCatalog(): RewardCatalog {
  return {
    balanceSeconds: 3600,
    rewards: [
      {
        id: '44444444-4444-4444-4444-444444444444',
        name: '一杯奶茶',
        description: '完成高数章节后兑换',
        costSeconds: 1800,
        isActive: true,
        createdAtUtc: now,
        updatedAtUtc: now,
      },
    ],
    recentRedemptions: [],
  }
}

function sampleTimeline(): TrackingOverview {
  return {
    settings: {
      captureActivity: true,
      captureScreenshots: false,
      screenshotIntervalMinutes: 5,
      screenshotRetentionDays: 7,
      idleThresholdMinutes: 5,
    },
    timeline: {
      dayStartedAtUtc: '2026-08-11T16:00:00.000Z',
      dayEndedAtUtc: '2026-08-12T16:00:00.000Z',
      trackedSeconds: 900,
      idleSeconds: 240,
      sessions: [
        {
          id: '11111111-1111-1111-1111-111111111111',
          goal: '完成高数极限章节',
          status: 'completed',
          startedAtUtc: '2026-08-12T08:00:00.000Z',
          endedAtUtc: '2026-08-12T08:15:00.000Z',
          effectiveDurationSeconds: 900,
          activities: [
            {
              id: '22222222-2222-2222-2222-222222222222',
              sessionId: '11111111-1111-1111-1111-111111111111',
              startedAtUtc: '2026-08-12T08:00:00.000Z',
              endedAtUtc: '2026-08-12T08:05:00.000Z',
              processName: 'Visual Studio Code',
              windowTitle: 'TimeBet - Visual Studio Code',
              isIdle: false,
            },
            {
              id: '88888888-8888-8888-8888-888888888888',
              sessionId: '11111111-1111-1111-1111-111111111111',
              startedAtUtc: '2026-08-12T08:05:00.000Z',
              endedAtUtc: '2026-08-12T08:06:00.000Z',
              processName: 'quark',
              windowTitle: '高数强化18.mp4',
              isIdle: false,
            },
            {
              id: '77777777-7777-7777-7777-777777777777',
              sessionId: '11111111-1111-1111-1111-111111111111',
              startedAtUtc: '2026-08-12T08:06:00.000Z',
              endedAtUtc: '2026-08-12T08:11:00.000Z',
              processName: 'Visual Studio Code',
              windowTitle: 'notes.md - Visual Studio Code',
              isIdle: false,
            },
            {
              id: '33333333-3333-3333-3333-333333333333',
              sessionId: '11111111-1111-1111-1111-111111111111',
              startedAtUtc: '2026-08-12T08:11:00.000Z',
              endedAtUtc: '2026-08-12T08:15:00.000Z',
              processName: 'Visual Studio Code',
              windowTitle: 'TimeBet - Visual Studio Code',
              isIdle: true,
            },
          ],
          screenshots: [],
        },
      ],
    },
  }
}

function sampleTaskRelevance(): TaskRelevanceSnapshot {
  return {
    available: true,
    goal: '完成高数极限章节',
    processName: 'WINWORD',
    windowTitle: '高数极限错题.docx - Word',
    level: 'high',
    score: 82,
    isIdle: false,
    observedAtUtc: now,
    reasons: ['窗口标题包含任务关键词：高数、极限。'],
  }
}

function samplePlanningTask(): PlanningTask {
  return {
    id: '66666666-6666-6666-6666-666666666666',
    title: '整理高数错题',
    estimatedSeconds: 45 * 60,
    actualSeconds: 0,
    dueDate: '2026-08-12',
    status: 'active',
    createdAtUtc: now,
    completedAtUtc: null,
  }
}

function sampleTodayPlan(): TodayPlan {
  return {
    date: '2026-08-12',
    dayStartedAtUtc: '2026-08-11T16:00:00.000Z',
    dayEndedAtUtc: '2026-08-12T16:00:00.000Z',
    plannedSeconds: planningBlocks.reduce(
      (sum, block) => sum + block.plannedSeconds,
      0,
    ),
    actualSeconds: planningBlocks.reduce(
      (sum, block) => sum + block.actualSeconds,
      0,
    ),
    plannedCount: planningBlocks.filter((block) => block.status === 'planned')
      .length,
    completedCount: planningBlocks.filter(
      (block) => block.status === 'completed',
    ).length,
    skippedCount: planningBlocks.filter((block) => block.status === 'skipped')
      .length,
    blocks: planningBlocks,
  }
}

function sampleWeekPlan(): WeekPlan {
  const dates = Array.from(
    { length: 7 },
    (_, index) => `2026-08-${String(10 + index).padStart(2, '0')}`,
  )
  const days = dates.map((date) => {
    const blocks = planningBlocks.filter((block) =>
      block.startsAtUtc.startsWith(date),
    )
    return {
      date,
      dayStartedAtUtc: `${date}T00:00:00.000Z`,
      dayEndedAtUtc: `${date}T23:59:59.999Z`,
      plannedSeconds: blocks.reduce(
        (sum, block) => sum + block.plannedSeconds,
        0,
      ),
      actualSeconds: blocks.reduce(
        (sum, block) => sum + block.actualSeconds,
        0,
      ),
      plannedCount: blocks.filter((block) => block.status === 'planned').length,
      completedCount: blocks.filter((block) => block.status === 'completed')
        .length,
      skippedCount: blocks.filter((block) => block.status === 'skipped').length,
      blocks,
    }
  })
  return {
    weekStartedDate: '2026-08-10',
    weekStartedAtUtc: '2026-08-10T00:00:00.000Z',
    weekEndedAtUtc: '2026-08-17T00:00:00.000Z',
    plannedSeconds: days.reduce((sum, day) => sum + day.plannedSeconds, 0),
    actualSeconds: days.reduce((sum, day) => sum + day.actualSeconds, 0),
    completedCount: days.reduce((sum, day) => sum + day.completedCount, 0),
    skippedCount: days.reduce((sum, day) => sum + day.skippedCount, 0),
    days,
  }
}

function emptyState(): StudyState {
  return { session: null, balanceSeconds: 0, asOfUtc: now }
}

function runningState(
  goal = '完成高数极限章节',
  captureScreenshots = false,
  taskId?: string,
  timeBlockId?: string,
): StudyState {
  return {
    session: {
      id: '11111111-1111-1111-1111-111111111111',
      goal,
      taskId: taskId ?? null,
      timeBlockId: timeBlockId ?? null,
      captureScreenshots,
      status: 'running',
      startedAtUtc: now,
      lastTransitionAtUtc: now,
      pausedAtUtc: null,
      endedAtUtc: null,
      effectiveDurationSeconds: 65,
      completionNote: null,
    },
    balanceSeconds: 0,
    asOfUtc: now,
  }
}
