import type {
  BridgeRequest,
  AnalyticsOverview,
  ChallengeOverview,
  ChallengeRound,
  DailyReview,
  AiSettings,
  LedgerEntry,
  LeisureSession,
  LeisureState,
  PlanningTask,
  PlanningTimeBlock,
  RewardCatalog,
  StudyState,
  TaskMilestone,
  TodayPlan,
  TrackingOverview,
  WeekPlan,
} from '../bridge/types'

const now = new Date().toISOString()

export function installPreviewBridge() {
  const listeners = new Set<(event: MessageEvent<unknown>) => void>()
  const rewardCatalog: RewardCatalog = {
    balanceSeconds: 3 * 3600 + 20 * 60,
    rewards: [
      reward('奶茶休息站', '完成本周数学复盘后，去喝一杯喜欢的奶茶。', 30),
      reward('看一场电影', '周末选一部收藏很久的电影。', 120),
      reward('买一本新书', '给下一阶段准备一本真正想读的书。', 240),
      reward('今晚提前休息', '把睡前的一小时完整留给自己。', 60),
      { ...reward('一件新衣服', '阶段性奖励。', 600), isActive: false },
    ],
    recentRedemptions: [
      {
        id: crypto.randomUUID(),
        rewardId: crypto.randomUUID(),
        rewardName: '一杯咖啡',
        costSeconds: 25 * 60,
        redeemedAtUtc: now,
      },
    ],
  }
  let studyState: StudyState = {
    session: null,
    balanceSeconds: rewardCatalog.balanceSeconds,
    asOfUtc: now,
  }
  let leisureState: LeisureState = {
    session: null,
    balanceSeconds: rewardCatalog.balanceSeconds,
    asOfUtc: now,
  }
  const previewDate = localDate(new Date())
  const dueIn = (days: number) => {
    const value = new Date()
    value.setDate(value.getDate() + days)
    return localDate(value)
  }
  const mathTaskId = crypto.randomUUID()
  const englishTaskId = crypto.randomUUID()
  const reportTaskId = crypto.randomUUID()
  let planningTasks: PlanningTask[] = [
    {
      id: mathTaskId,
      title: '高数期中复习',
      estimatedSeconds: 400 * 60,
      actualSeconds: 260 * 60,
      dueDate: previewDate,
      status: 'active',
      createdAtUtc: new Date(Date.now() - 5 * 86_400_000).toISOString(),
      completedAtUtc: null,
      milestones: [
        milestone(mathTaskId, '整理公式与题型', 0, true),
        milestone(mathTaskId, '完成极限专题', 1, true),
        milestone(mathTaskId, '错题复盘与自测', 2),
      ],
    },
    {
      id: englishTaskId,
      title: '英语四级听力',
      estimatedSeconds: 300 * 60,
      actualSeconds: 120 * 60,
      dueDate: dueIn(12),
      status: 'active',
      createdAtUtc: new Date(Date.now() - 3 * 86_400_000).toISOString(),
      completedAtUtc: null,
      milestones: [
        milestone(englishTaskId, '精听两套真题', 0, true),
        milestone(englishTaskId, '整理高频错词', 1),
        milestone(englishTaskId, '完成一次模拟', 2),
      ],
    },
    {
      id: reportTaskId,
      title: '课程项目报告',
      estimatedSeconds: 240 * 60,
      actualSeconds: 60 * 60,
      dueDate: dueIn(4),
      status: 'active',
      createdAtUtc: new Date(Date.now() - 2 * 86_400_000).toISOString(),
      completedAtUtc: null,
      milestones: [
        milestone(reportTaskId, '完成报告大纲', 0, true),
        milestone(reportTaskId, '补齐核心内容', 1),
        milestone(reportTaskId, '检查格式并提交', 2),
      ],
    },
  ]
  let planningBlocks: PlanningTimeBlock[] = [
    {
      id: crypto.randomUUID(),
      taskId: mathTaskId,
      taskTitle: '高数期中复习',
      dueDate: previewDate,
      startsAtUtc: new Date(`${previewDate}T10:30:00`).toISOString(),
      plannedSeconds: 45 * 60,
      actualSeconds: 0,
      status: 'planned',
      resolvedAtUtc: null,
    },
    {
      id: crypto.randomUUID(),
      taskId: englishTaskId,
      taskTitle: '英语四级听力',
      dueDate: dueIn(12),
      startsAtUtc: new Date(`${previewDate}T14:00:00`).toISOString(),
      plannedSeconds: 40 * 60,
      actualSeconds: 0,
      status: 'planned',
      resolvedAtUtc: null,
    },
  ]
  const tracking: TrackingOverview = {
    settings: {
      captureActivity: true,
      captureScreenshots: false,
      screenshotIntervalMinutes: 5,
      screenshotRetentionDays: 7,
      idleThresholdMinutes: 5,
    },
    timeline: {
      dayStartedAtUtc: now,
      dayEndedAtUtc: now,
      trackedSeconds: 0,
      idleSeconds: 0,
      sessions: [],
    },
  }
  const analytics = previewAnalytics()
  const dailyReview = previewDailyReview()
  let aiSettings: AiSettings = {
    enabled: false,
    baseUrl: 'https://api.openai.com/v1',
    model: 'gpt-4o-mini',
    apiKeyConfigured: false,
    apiKeyHint: null,
  }
  const ledgerEntries: LedgerEntry[] = [
    {
      id: crypto.randomUUID(),
      type: 'earn',
      amountSeconds: 95 * 60,
      sourceType: 'study-session',
      sourceId: crypto.randomUUID(),
      note: '完成高数极限专题与错题复盘',
      occurredAtUtc: now,
    },
    {
      id: crypto.randomUUID(),
      type: 'spend',
      amountSeconds: -30 * 60,
      sourceType: 'reward-redemption',
      sourceId: crypto.randomUUID(),
      note: '兑换奖励：奶茶休息站',
      occurredAtUtc: new Date(Date.now() - 86_400_000).toISOString(),
    },
    {
      id: crypto.randomUUID(),
      type: 'earn',
      amountSeconds: 72 * 60,
      sourceType: 'study-session',
      sourceId: crypto.randomUUID(),
      note: '英语阅读与单词复习',
      occurredAtUtc: new Date(Date.now() - 2 * 86_400_000).toISOString(),
    },
  ]
  let challenge = previewChallenge()

  window.chrome = {
    webview: {
      addEventListener(_type, listener) {
        listeners.add(listener)
      },
      postMessage(message) {
        const request = message as BridgeRequest
        let result: unknown
        switch (request.method) {
          case 'session.current':
            result = studyState
            break
          case 'session.start': {
            const payload = request.payload as {
              goal: string
              captureScreenshots: boolean
              taskId?: string
              timeBlockId?: string
            }
            const startedAt = new Date().toISOString()
            studyState = {
              session: {
                id: crypto.randomUUID(),
                goal: payload.goal,
                taskId: payload.taskId ?? null,
                timeBlockId: payload.timeBlockId ?? null,
                captureScreenshots: payload.captureScreenshots,
                status: 'running',
                startedAtUtc: startedAt,
                lastTransitionAtUtc: startedAt,
                pausedAtUtc: null,
                endedAtUtc: null,
                effectiveDurationSeconds: 0,
                completionNote: null,
              },
              balanceSeconds: studyState.balanceSeconds,
              asOfUtc: startedAt,
            }
            result = studyState
            break
          }
          case 'session.pause': {
            const pausedAt = new Date().toISOString()
            if (studyState.session) {
              studyState = {
                ...studyState,
                session: {
                  ...studyState.session,
                  status: 'paused',
                  pausedAtUtc: pausedAt,
                  lastTransitionAtUtc: pausedAt,
                },
                asOfUtc: pausedAt,
              }
            }
            result = studyState
            break
          }
          case 'session.resume': {
            const resumedAt = new Date().toISOString()
            if (studyState.session) {
              studyState = {
                ...studyState,
                session: {
                  ...studyState.session,
                  status: 'running',
                  pausedAtUtc: null,
                  lastTransitionAtUtc: resumedAt,
                },
                asOfUtc: resumedAt,
              }
            }
            result = studyState
            break
          }
          case 'session.finish': {
            const payload = request.payload as { completionNote?: string }
            const current = studyState.session!
            const endedAt = new Date().toISOString()
            const liveSeconds =
              current.status === 'running'
                ? Math.max(
                    1,
                    Math.floor(
                      (new Date(endedAt).getTime() -
                        new Date(current.lastTransitionAtUtc).getTime()) /
                        1000,
                    ),
                  )
                : 0
            const effectiveDurationSeconds =
              current.effectiveDurationSeconds + liveSeconds
            const completedSession = {
              ...current,
              status: 'completed' as const,
              endedAtUtc: endedAt,
              lastTransitionAtUtc: endedAt,
              pausedAtUtc: null,
              effectiveDurationSeconds,
              completionNote: payload.completionNote ?? '',
            }
            const nextBalance =
              studyState.balanceSeconds + effectiveDurationSeconds
            studyState = {
              session: null,
              balanceSeconds: nextBalance,
              asOfUtc: endedAt,
            }
            leisureState = {
              ...leisureState,
              balanceSeconds: nextBalance,
              asOfUtc: endedAt,
            }
            rewardCatalog.balanceSeconds = nextBalance
            if (current.taskId) {
              planningTasks = planningTasks.map((task) =>
                task.id === current.taskId
                  ? {
                      ...task,
                      actualSeconds:
                        task.actualSeconds + effectiveDurationSeconds,
                    }
                  : task,
              )
            }
            if (current.timeBlockId) {
              planningBlocks = planningBlocks.map((block) =>
                block.id === current.timeBlockId
                  ? {
                      ...block,
                      status: 'completed',
                      actualSeconds: effectiveDurationSeconds,
                      resolvedAtUtc: endedAt,
                    }
                  : block,
              )
            }
            result = { state: studyState, completedSession }
            break
          }
          case 'leisure.current':
            result = leisureState
            break
          case 'leisure.start': {
            const payload = request.payload as {
              activity: string
              plannedMinutes: number
            }
            const startedAt = new Date().toISOString()
            leisureState = {
              session: {
                id: crypto.randomUUID(),
                activity: payload.activity,
                plannedSeconds: payload.plannedMinutes * 60,
                status: 'running',
                startedAtUtc: startedAt,
                lastTransitionAtUtc: startedAt,
                pausedAtUtc: null,
                endedAtUtc: null,
                effectiveDurationSeconds: 0,
                chargedSeconds: 0,
                completionNote: null,
              },
              balanceSeconds: studyState.balanceSeconds,
              asOfUtc: startedAt,
            }
            result = leisureState
            break
          }
          case 'leisure.pause': {
            const asOf = new Date().toISOString()
            if (leisureState.session) {
              leisureState = {
                ...leisureState,
                session: {
                  ...leisureState.session,
                  status: 'paused',
                  pausedAtUtc: asOf,
                  lastTransitionAtUtc: asOf,
                  effectiveDurationSeconds: previewLeisureElapsed(
                    leisureState.session,
                    leisureState.asOfUtc,
                    asOf,
                  ),
                },
                asOfUtc: asOf,
              }
            }
            result = leisureState
            break
          }
          case 'leisure.resume': {
            const asOf = new Date().toISOString()
            if (leisureState.session) {
              leisureState = {
                ...leisureState,
                session: {
                  ...leisureState.session,
                  status: 'running',
                  pausedAtUtc: null,
                  lastTransitionAtUtc: asOf,
                },
                asOfUtc: asOf,
              }
            }
            result = leisureState
            break
          }
          case 'leisure.finish': {
            const payload = request.payload as { completionNote?: string }
            const endedAt = new Date().toISOString()
            const current = leisureState.session!
            const effectiveDurationSeconds = Math.max(
              1,
              previewLeisureElapsed(current, leisureState.asOfUtc, endedAt),
            )
            const chargedSeconds = Math.min(
              effectiveDurationSeconds,
              leisureState.balanceSeconds,
            )
            const completedSession: LeisureSession = {
              ...current,
              status: 'completed',
              pausedAtUtc: null,
              endedAtUtc: endedAt,
              lastTransitionAtUtc: endedAt,
              effectiveDurationSeconds,
              chargedSeconds,
              completionNote: payload.completionNote ?? null,
            }
            const nextBalance = leisureState.balanceSeconds - chargedSeconds
            leisureState = {
              session: null,
              balanceSeconds: nextBalance,
              asOfUtc: endedAt,
            }
            studyState = {
              ...studyState,
              balanceSeconds: nextBalance,
              asOfUtc: endedAt,
            }
            rewardCatalog.balanceSeconds = nextBalance
            ledgerEntries.unshift({
              id: crypto.randomUUID(),
              type: 'spend',
              amountSeconds: -chargedSeconds,
              sourceType: 'leisure-session',
              sourceId: completedSession.id,
              note: `自由时间：${completedSession.activity}`,
              occurredAtUtc: endedAt,
            })
            result = {
              state: leisureState,
              completedSession,
            }
            break
          }
          case 'task.list':
            result = planningTasks.filter((task) => task.status === 'active')
            break
          case 'task.create': {
            const payload = request.payload as {
              title: string
              estimatedMinutes?: number
              dueDate?: string
            }
            const task: PlanningTask = {
              id: crypto.randomUUID(),
              title: payload.title,
              estimatedSeconds: payload.estimatedMinutes
                ? payload.estimatedMinutes * 60
                : null,
              actualSeconds: 0,
              dueDate: payload.dueDate ?? null,
              status: 'active',
              createdAtUtc: new Date().toISOString(),
              completedAtUtc: null,
              milestones: [],
            }
            planningTasks = [task, ...planningTasks]
            result = task
            break
          }
          case 'planner.today':
            result = previewTodayPlan(planningBlocks)
            break
          case 'planner.week':
            result = previewWeekPlan(planningBlocks)
            break
          case 'timeblock.create': {
            const payload = request.payload as {
              taskId: string
              startsAtUtc: string
              plannedMinutes: number
            }
            const task = planningTasks.find(
              (item) => item.id === payload.taskId,
            )!
            const block: PlanningTimeBlock = {
              id: crypto.randomUUID(),
              taskId: task.id,
              taskTitle: task.title,
              dueDate: task.dueDate,
              startsAtUtc: payload.startsAtUtc,
              plannedSeconds: payload.plannedMinutes * 60,
              actualSeconds: 0,
              status: 'planned',
              resolvedAtUtc: null,
            }
            planningBlocks = [...planningBlocks, block]
            result = block
            break
          }
          case 'timeblock.skip': {
            const payload = request.payload as { id: string }
            const current = planningBlocks.find(
              (block) => block.id === payload.id,
            )!
            const updated: PlanningTimeBlock = {
              ...current,
              status: 'skipped',
              resolvedAtUtc: new Date().toISOString(),
            }
            planningBlocks = planningBlocks.map((block) =>
              block.id === updated.id ? updated : block,
            )
            result = updated
            break
          }
          case 'timeblock.move': {
            const payload = request.payload as {
              id: string
              startsAtUtc: string
              plannedMinutes: number
            }
            const current = planningBlocks.find(
              (block) => block.id === payload.id,
            )!
            const updated: PlanningTimeBlock = {
              ...current,
              startsAtUtc: payload.startsAtUtc,
              plannedSeconds: payload.plannedMinutes * 60,
            }
            planningBlocks = planningBlocks.map((block) =>
              block.id === updated.id ? updated : block,
            )
            result = updated
            break
          }
          case 'timeblock.defer': {
            const payload = request.payload as { id: string }
            const current = planningBlocks.find(
              (block) => block.id === payload.id,
            )!
            const startsAt = new Date(current.startsAtUtc)
            startsAt.setDate(startsAt.getDate() + 1)
            const updated: PlanningTimeBlock = {
              ...current,
              startsAtUtc: startsAt.toISOString(),
            }
            planningBlocks = planningBlocks.map((block) =>
              block.id === updated.id ? updated : block,
            )
            result = updated
            break
          }
          case 'task.set-completed': {
            const payload = request.payload as {
              id: string
              completed: boolean
            }
            const current = planningTasks.find((task) => task.id === payload.id)
            if (current) {
              const updated: PlanningTask = {
                ...current,
                status: payload.completed ? 'completed' : 'active',
                completedAtUtc: payload.completed
                  ? new Date().toISOString()
                  : null,
              }
              planningTasks = planningTasks.map((task) =>
                task.id === updated.id ? updated : task,
              )
              result = updated
            }
            break
          }
          case 'task.milestone.add': {
            const payload = request.payload as {
              taskId: string
              content: string
            }
            const task = planningTasks.find(
              (item) => item.id === payload.taskId,
            )!
            const created = milestone(
              task.id,
              payload.content,
              (task.milestones ?? []).length,
            )
            planningTasks = planningTasks.map((item) =>
              item.id === task.id
                ? {
                    ...item,
                    milestones: [...(item.milestones ?? []), created],
                  }
                : item,
            )
            result = created
            break
          }
          case 'task.milestone.set-completed': {
            const payload = request.payload as {
              id: string
              completed: boolean
            }
            const changedAt = new Date().toISOString()
            let updatedMilestone = planningTasks
              .flatMap((task) => task.milestones ?? [])
              .find((item) => item.id === payload.id)!
            updatedMilestone = {
              ...updatedMilestone,
              isCompleted: payload.completed,
              completedAtUtc: payload.completed ? changedAt : null,
            }
            planningTasks = planningTasks.map((task) => ({
              ...task,
              milestones: (task.milestones ?? []).map((item) =>
                item.id === updatedMilestone.id ? updatedMilestone : item,
              ),
            }))
            result = updatedMilestone
            break
          }
          case 'timeblock.delete': {
            const payload = request.payload as { id: string }
            planningBlocks = planningBlocks.filter(
              (block) => block.id !== payload.id,
            )
            result = null
            break
          }
          case 'timeline.today':
            result = tracking
            break
          case 'reward.catalog':
            result = rewardCatalog
            break
          case 'analytics.overview':
            result = analytics
            break
          case 'review.today.get':
            result = aiSettings.enabled
              ? { ...dailyReview, source: 'ai', aiModel: aiSettings.model }
              : dailyReview
            break
          case 'ai.settings.get':
            result = aiSettings
            break
          case 'ai.settings.update': {
            const payload = request.payload as {
              enabled: boolean
              baseUrl?: string
              model?: string
              apiKey?: string
            }
            const key = payload.apiKey?.trim()
            aiSettings = {
              enabled: payload.enabled,
              baseUrl: payload.baseUrl?.trim() || aiSettings.baseUrl,
              model: payload.model?.trim() || aiSettings.model,
              apiKeyConfigured: Boolean(key) || aiSettings.apiKeyConfigured,
              apiKeyHint: key
                ? `已配置（末尾 ${key.slice(-4)}）`
                : aiSettings.apiKeyHint,
            }
            result = aiSettings
            break
          }
          case 'ledger.entries':
            result = ledgerEntries
            break
          case 'export.summary':
            result = { saved: false, fileName: null }
            break
          case 'challenge.overview':
            result = challenge
            break
          case 'challenge.settings.update': {
            const payload = request.payload as { enabled: boolean }
            challenge = { ...challenge, enabled: payload.enabled }
            result = challenge
            break
          }
          case 'challenge.play': {
            const payload = request.payload as {
              roundId: string
              wagerSeconds: number
            }
            const round = previewRound(payload.roundId, payload.wagerSeconds)
            challenge = {
              ...challenge,
              balanceSeconds: challenge.balanceSeconds + round.netSeconds,
              maximumWagerSeconds: Math.min(
                30 * 60,
                Math.floor(
                  (challenge.balanceSeconds + round.netSeconds) / 600,
                ) * 60,
              ),
              roundsPlayedToday: challenge.roundsPlayedToday + 1,
              roundsRemainingToday: challenge.roundsRemainingToday - 1,
              recentRounds: [round, ...challenge.recentRounds],
            }
            result = { overview: challenge, round }
            break
          }
        }
        queueMicrotask(() => {
          const event = new MessageEvent('message', {
            data: { id: request.id, ok: true, result },
          })
          listeners.forEach((listener) => listener(event))
        })
      },
    },
  }
}

function previewTodayPlan(blocks: PlanningTimeBlock[]): TodayPlan {
  const date = localDate(new Date())
  const start = new Date(`${date}T00:00:00`)
  const end = new Date(`${date}T00:00:00`)
  end.setDate(end.getDate() + 1)
  const todayBlocks = blocks
    .filter((block) => {
      const startAt = new Date(block.startsAtUtc)
      return startAt >= start && startAt < end
    })
    .sort(
      (left, right) =>
        new Date(left.startsAtUtc).getTime() -
        new Date(right.startsAtUtc).getTime(),
    )
  return {
    date,
    dayStartedAtUtc: start.toISOString(),
    dayEndedAtUtc: end.toISOString(),
    plannedSeconds: todayBlocks.reduce(
      (sum, block) => sum + block.plannedSeconds,
      0,
    ),
    actualSeconds: todayBlocks.reduce(
      (sum, block) => sum + block.actualSeconds,
      0,
    ),
    plannedCount: todayBlocks.filter((block) => block.status === 'planned')
      .length,
    completedCount: todayBlocks.filter((block) => block.status === 'completed')
      .length,
    skippedCount: todayBlocks.filter((block) => block.status === 'skipped')
      .length,
    blocks: todayBlocks,
  }
}

function previewWeekPlan(blocks: PlanningTimeBlock[]): WeekPlan {
  const today = new Date()
  const monday = new Date(today)
  const daysSinceMonday = (today.getDay() + 6) % 7
  monday.setDate(today.getDate() - daysSinceMonday)
  monday.setHours(0, 0, 0, 0)
  const days = Array.from({ length: 7 }, (_, index) => {
    const date = new Date(monday)
    date.setDate(date.getDate() + index)
    const dateValue = localDate(date)
    const end = new Date(date)
    end.setDate(end.getDate() + 1)
    const dayBlocks = blocks
      .filter((block) => {
        const startsAt = new Date(block.startsAtUtc)
        return startsAt >= date && startsAt < end
      })
      .sort(
        (left, right) =>
          new Date(left.startsAtUtc).getTime() -
          new Date(right.startsAtUtc).getTime(),
      )
    return {
      date: dateValue,
      dayStartedAtUtc: date.toISOString(),
      dayEndedAtUtc: end.toISOString(),
      plannedSeconds: dayBlocks.reduce(
        (sum, block) => sum + block.plannedSeconds,
        0,
      ),
      actualSeconds: dayBlocks.reduce(
        (sum, block) => sum + block.actualSeconds,
        0,
      ),
      plannedCount: dayBlocks.filter((block) => block.status === 'planned')
        .length,
      completedCount: dayBlocks.filter((block) => block.status === 'completed')
        .length,
      skippedCount: dayBlocks.filter((block) => block.status === 'skipped')
        .length,
      blocks: dayBlocks,
    }
  })
  const weekEnd = new Date(monday)
  weekEnd.setDate(weekEnd.getDate() + 7)
  return {
    weekStartedDate: localDate(monday),
    weekStartedAtUtc: monday.toISOString(),
    weekEndedAtUtc: weekEnd.toISOString(),
    plannedSeconds: days.reduce((sum, day) => sum + day.plannedSeconds, 0),
    actualSeconds: days.reduce((sum, day) => sum + day.actualSeconds, 0),
    completedCount: days.reduce((sum, day) => sum + day.completedCount, 0),
    skippedCount: days.reduce((sum, day) => sum + day.skippedCount, 0),
    days,
  }
}

function localDate(date: Date) {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function milestone(
  taskId: string,
  content: string,
  sortOrder: number,
  isCompleted = false,
): TaskMilestone {
  const completedAtUtc = isCompleted ? now : null
  return {
    id: crypto.randomUUID(),
    taskId,
    content,
    sortOrder,
    isCompleted,
    createdAtUtc: now,
    completedAtUtc,
  }
}

function previewLeisureElapsed(
  session: LeisureSession,
  stateAsOfUtc: string,
  nowUtc: string,
) {
  if (session.status !== 'running') return session.effectiveDurationSeconds
  return (
    session.effectiveDurationSeconds +
    Math.max(
      0,
      Math.floor(
        (new Date(nowUtc).getTime() - new Date(stateAsOfUtc).getTime()) / 1000,
      ),
    )
  )
}

function previewChallenge(): ChallengeOverview {
  return {
    enabled: true,
    balanceSeconds: 3 * 3600 + 20 * 60,
    hasActiveStudySession: false,
    roundsPlayedToday: 1,
    roundsRemainingToday: 2,
    minimumBalanceSeconds: 10 * 60,
    minimumWagerSeconds: 60,
    maximumWagerSeconds: 20 * 60,
    dailyRoundLimit: 3,
    maximumWagerBalanceShare: 0.1,
    expectedReturn: 0.955,
    probabilityVersion: 'star-wish-v1',
    probabilityTiers: [
      tier('starlight-fades', '星光熄灭', 0.4, 0, ['moon', 'cloud', 'book']),
      tier('ember-remains', '留下火种', 0.2, 0.5, ['spark', 'book', 'spark']),
      tier('small-fortune', '小小幸运', 0.25, 1.5, ['book', 'star', 'book']),
      tier('double-progress', '双倍进展', 0.12, 3, ['star', 'medal', 'star']),
      tier('wish-arrives', '星愿降临', 0.03, 4, ['crown', 'crown', 'crown']),
    ],
    recentRounds: [previewRound(crypto.randomUUID(), 5 * 60)],
  }
}

function previewRound(id: string, wagerSeconds: number): ChallengeRound {
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

function tier(
  code: string,
  label: string,
  probability: number,
  payoutMultiplier: number,
  symbols: string[],
) {
  return { code, label, probability, payoutMultiplier, symbols }
}

function previewAnalytics(): AnalyticsOverview {
  const durations = [52, 85, 40, 110, 76, 128, 95]
  return {
    generatedAtUtc: now,
    todayStartedAtUtc: now,
    weekStartedAtUtc: now,
    todayStudySeconds: 95 * 60,
    weekStudySeconds: durations
      .slice(1)
      .reduce((sum, value) => sum + value * 60, 0),
    todayCompletedSessions: 2,
    weekCompletedSessions: 11,
    ledger: {
      balanceSeconds: 3 * 3600 + 20 * 60,
      earnedSeconds: 18 * 3600 + 45 * 60,
      spentSeconds: 15 * 3600 + 25 * 60,
    },
    dailyTrend: durations.map((minutes, index) => {
      const date = new Date()
      date.setDate(date.getDate() - (6 - index))
      return {
        date: date.toISOString().slice(0, 10),
        dayStartedAtUtc: date.toISOString(),
        studySeconds: minutes * 60,
        completedSessions: index % 3 === 0 ? 1 : 2,
      }
    }),
    applicationUsage: [
      {
        processName: 'Visual Studio Code',
        durationSeconds: 42 * 60,
        share: 0.44,
      },
      { processName: 'Microsoft Edge', durationSeconds: 31 * 60, share: 0.33 },
      { processName: 'Anki', durationSeconds: 14 * 60, share: 0.15 },
      { processName: '其他应用', durationSeconds: 8 * 60, share: 0.08 },
    ],
  }
}

function previewDailyReview(): DailyReview {
  return {
    date: localDate(new Date()),
    generatedAtUtc: now,
    hasData: true,
    source: 'local',
    aiModel: null,
    headline: '今天的前台活动与设定任务总体一致',
    narrative:
      '今天累计学习 1 小时 35 分钟，完成 2 个学习时间块。在 1 小时 28 分钟的非空闲前台记录中，较强相关证据 52 分钟、可能相关 28 分钟、可能偏离 3 分钟、暂无法判断 5 分钟。使用最多的前台软件是 Visual Studio Code，累计 42 分钟。',
    metrics: {
      plannedSeconds: 2 * 3600,
      studySeconds: 95 * 60,
      trackedSeconds: 95 * 60,
      idleSeconds: 7 * 60,
      plannedBlockCount: 3,
      completedBlockCount: 2,
      skippedBlockCount: 0,
      completedSessionCount: 2,
    },
    relevance: {
      activityTrackingEnabled: true,
      evaluatedSeconds: 88 * 60,
      highSeconds: 52 * 60,
      possibleSeconds: 28 * 60,
      lowSeconds: 3 * 60,
      unknownSeconds: 5 * 60,
    },
    applications: [
      {
        processName: 'Visual Studio Code',
        durationSeconds: 42 * 60,
        share: 0.48,
      },
      { processName: 'Microsoft Edge', durationSeconds: 31 * 60, share: 0.35 },
      { processName: 'Anki', durationSeconds: 15 * 60, share: 0.17 },
    ],
    sessions: [],
    highlights: [
      '完成了：「高数极限专题」、「英语阅读」',
      '留下 1 小时 35 分钟的有效学习记录',
      '主要软件：Visual Studio Code 42 分钟、Microsoft Edge 31 分钟、Anki 15 分钟',
    ],
    suggestions: [
      '今天还有 1 个计划块未完成，可决定继续、顺延或跳过，避免计划一直悬空。',
      '今天的记录暂未显示明显问题，下一次继续保持明确目标和及时暂停即可。',
    ],
  }
}

function reward(name: string, description: string, costMinutes: number) {
  return {
    id: crypto.randomUUID(),
    name,
    description,
    costSeconds: costMinutes * 60,
    isActive: true,
    createdAtUtc: now,
    updatedAtUtc: now,
  }
}
