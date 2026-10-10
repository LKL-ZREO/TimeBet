export type SessionStatus = 'running' | 'paused' | 'completed'

export type StudySession = {
  id: string
  goal: string
  taskId?: string | null
  timeBlockId?: string | null
  captureScreenshots: boolean
  status: SessionStatus
  startedAtUtc: string
  lastTransitionAtUtc: string
  pausedAtUtc: string | null
  endedAtUtc: string | null
  effectiveDurationSeconds: number
  completionNote: string | null
}

export type StudyState = {
  session: StudySession | null
  balanceSeconds: number
  asOfUtc: string
}

export type TaskRelevanceLevel =
  'high' | 'possible' | 'low' | 'idle' | 'unknown' | 'disabled'

export type TaskRelevanceSnapshot = {
  available: boolean
  goal: string
  processName: string
  windowTitle: string
  level: TaskRelevanceLevel
  score: number
  isIdle: boolean
  observedAtUtc: string
  reasons: string[]
}

export type SessionTaskInput = {
  content: string
  isCompleted: boolean
}

export type FinishSessionResult = {
  state: StudyState
  completedSession: StudySession & {
    status: 'completed'
    endedAtUtc: string
    completionNote: string
  }
}

export type LeisureStatus = 'running' | 'paused' | 'completed'

export type LeisureSession = {
  id: string
  activity: string
  plannedSeconds: number
  status: LeisureStatus
  startedAtUtc: string
  lastTransitionAtUtc: string
  pausedAtUtc: string | null
  endedAtUtc: string | null
  effectiveDurationSeconds: number
  chargedSeconds: number
  completionNote: string | null
}

export type LeisureState = {
  session: LeisureSession | null
  balanceSeconds: number
  asOfUtc: string
}

export type FinishLeisureResult = {
  state: LeisureState
  completedSession: LeisureSession & {
    status: 'completed'
    endedAtUtc: string
  }
}

export type BridgeMethod =
  | 'session.current'
  | 'session.start'
  | 'session.pause'
  | 'session.resume'
  | 'session.finish'
  | 'session.relevance.current'
  | 'leisure.current'
  | 'leisure.start'
  | 'leisure.pause'
  | 'leisure.resume'
  | 'leisure.finish'
  | 'task.list'
  | 'task.create'
  | 'task.set-completed'
  | 'task.milestone.add'
  | 'task.milestone.set-completed'
  | 'planner.today'
  | 'planner.week'
  | 'timeblock.create'
  | 'timeblock.move'
  | 'timeblock.defer'
  | 'timeblock.skip'
  | 'timeblock.delete'
  | 'ledger.balance'
  | 'timeline.today'
  | 'tracking.settings.get'
  | 'tracking.settings.update'
  | 'reward.catalog'
  | 'reward.create'
  | 'reward.update'
  | 'reward.set-active'
  | 'reward.redeem'
  | 'analytics.overview'
  | 'review.today.get'
  | 'ai.settings.get'
  | 'ai.settings.update'
  | 'ledger.entries'
  | 'export.summary'
  | 'challenge.overview'
  | 'challenge.settings.update'
  | 'challenge.play'

export type TaskMilestone = {
  id: string
  taskId: string
  content: string
  sortOrder: number
  isCompleted: boolean
  createdAtUtc: string
  completedAtUtc: string | null
}

export type PlanningTask = {
  id: string
  title: string
  estimatedSeconds: number | null
  actualSeconds: number
  dueDate: string | null
  status: 'active' | 'completed' | 'archived'
  createdAtUtc: string
  completedAtUtc: string | null
  milestones?: TaskMilestone[]
}

export type PlanningTimeBlock = {
  id: string
  taskId: string
  taskTitle: string
  dueDate: string | null
  startsAtUtc: string
  plannedSeconds: number
  actualSeconds: number
  status: 'planned' | 'completed' | 'skipped'
  resolvedAtUtc: string | null
}

export type TodayPlan = {
  date: string
  dayStartedAtUtc: string
  dayEndedAtUtc: string
  plannedSeconds: number
  actualSeconds: number
  plannedCount: number
  completedCount: number
  skippedCount: number
  blocks: PlanningTimeBlock[]
}

export type PlanDay = {
  date: string
  dayStartedAtUtc: string
  dayEndedAtUtc: string
  plannedSeconds: number
  actualSeconds: number
  plannedCount: number
  completedCount: number
  skippedCount: number
  blocks: PlanningTimeBlock[]
}

export type WeekPlan = {
  weekStartedDate: string
  weekStartedAtUtc: string
  weekEndedAtUtc: string
  plannedSeconds: number
  actualSeconds: number
  completedCount: number
  skippedCount: number
  days: PlanDay[]
}

export type Reward = {
  id: string
  name: string
  description: string | null
  costSeconds: number
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export type RewardRedemption = {
  id: string
  rewardId: string
  rewardName: string
  costSeconds: number
  redeemedAtUtc: string
}

export type RewardCatalog = {
  balanceSeconds: number
  rewards: Reward[]
  recentRedemptions: RewardRedemption[]
}

export type RedeemRewardResult = {
  catalog: RewardCatalog
  redemption: RewardRedemption
}

export type DailyStudy = {
  date: string
  dayStartedAtUtc: string
  studySeconds: number
  completedSessions: number
}

export type ApplicationUsage = {
  processName: string
  durationSeconds: number
  share: number
}

export type LedgerTotals = {
  balanceSeconds: number
  earnedSeconds: number
  spentSeconds: number
}

export type LedgerEntry = {
  id: string
  type: 'earn' | 'spend' | 'game-win' | 'game-loss' | 'adjustment'
  amountSeconds: number
  sourceType: string
  sourceId: string
  note: string | null
  occurredAtUtc: string
}

export type AnalyticsOverview = {
  generatedAtUtc: string
  todayStartedAtUtc: string
  weekStartedAtUtc: string
  todayStudySeconds: number
  weekStudySeconds: number
  todayCompletedSessions: number
  weekCompletedSessions: number
  ledger: LedgerTotals
  dailyTrend: DailyStudy[]
  applicationUsage: ApplicationUsage[]
}

export type DailyReviewMetrics = {
  plannedSeconds: number
  studySeconds: number
  trackedSeconds: number
  idleSeconds: number
  plannedBlockCount: number
  completedBlockCount: number
  skippedBlockCount: number
  completedSessionCount: number
}

export type DailyReviewRelevance = {
  activityTrackingEnabled: boolean
  evaluatedSeconds: number
  highSeconds: number
  possibleSeconds: number
  lowSeconds: number
  unknownSeconds: number
}

export type DailyReviewApplication = {
  processName: string
  durationSeconds: number
  share: number
}

export type DailyReviewSession = {
  id: string
  goal: string
  status: SessionStatus
  durationSeconds: number
  idleSeconds: number
  highSeconds: number
  possibleSeconds: number
  lowSeconds: number
  unknownSeconds: number
  applications: DailyReviewApplication[]
}

export type DailyReview = {
  date: string
  generatedAtUtc: string
  hasData: boolean
  source: 'local' | 'ai'
  aiModel: string | null
  headline: string
  narrative: string
  metrics: DailyReviewMetrics
  relevance: DailyReviewRelevance
  applications: DailyReviewApplication[]
  sessions: DailyReviewSession[]
  highlights: string[]
  suggestions: string[]
}

export type AiSettings = {
  enabled: boolean
  baseUrl: string
  model: string
  apiKeyConfigured: boolean
  apiKeyHint: string | null
}

export type SummaryExportResult = {
  saved: boolean
  fileName: string | null
}

export type ChallengeProbabilityTier = {
  code: string
  label: string
  probability: number
  payoutMultiplier: number
  symbols: string[]
}

export type ChallengeRound = {
  id: string
  wagerSeconds: number
  payoutSeconds: number
  netSeconds: number
  outcomeCode: string
  outcomeLabel: string
  symbols: string[]
  playedAtUtc: string
}

export type ChallengeOverview = {
  enabled: boolean
  balanceSeconds: number
  hasActiveStudySession: boolean
  roundsPlayedToday: number
  roundsRemainingToday: number
  minimumBalanceSeconds: number
  minimumWagerSeconds: number
  maximumWagerSeconds: number
  dailyRoundLimit: number
  maximumWagerBalanceShare: number
  expectedReturn: number
  probabilityVersion: string
  probabilityTiers: ChallengeProbabilityTier[]
  recentRounds: ChallengeRound[]
}

export type PlayChallengeResult = {
  overview: ChallengeOverview
  round: ChallengeRound
}

export type TrackingSettings = {
  captureActivity: boolean
  captureScreenshots: boolean
  screenshotIntervalMinutes: number
  screenshotRetentionDays: number
  idleThresholdMinutes: number
}

export type TimelineActivity = {
  id: string
  sessionId: string
  startedAtUtc: string
  endedAtUtc: string
  processName: string
  windowTitle: string
  isIdle: boolean
}

export type TimelineScreenshot = {
  id: string
  capturedAtUtc: string
  url: string
  width: number
  height: number
}

export type TimelineSession = {
  id: string
  goal: string
  status: SessionStatus
  startedAtUtc: string
  endedAtUtc: string | null
  effectiveDurationSeconds: number
  activities: TimelineActivity[]
  screenshots: TimelineScreenshot[]
}

export type StudyTimeline = {
  dayStartedAtUtc: string
  dayEndedAtUtc: string
  trackedSeconds: number
  idleSeconds: number
  sessions: TimelineSession[]
}

export type TrackingOverview = {
  settings: TrackingSettings
  timeline: StudyTimeline
}

export type BridgeRequest = {
  id: string
  method: BridgeMethod
  payload?: unknown
}

export type BridgeError = {
  code: string
  message: string
}

export type BridgeResponse<T = unknown> = {
  id: string
  ok: boolean
  result?: T
  error?: BridgeError
}

export type BridgeEvent = {
  type: 'session.changed'
  payload: StudyState
}
