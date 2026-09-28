import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type DragEvent,
  type FormEvent,
} from 'react'
import {
  AlertCircle,
  BookOpen,
  CalendarDays,
  CalendarRange,
  Camera,
  ChartNoAxesColumnIncreasing,
  Check,
  Clock3,
  Cloud,
  Crown,
  Download,
  Gift,
  GripVertical,
  LoaderCircle,
  Medal,
  Minus,
  Moon,
  MonitorDot,
  Pause,
  Play,
  Plus,
  ReceiptText,
  RefreshCw,
  Pencil,
  Power,
  Settings2,
  Sparkles,
  Square,
  Sun,
  ShieldCheck,
  WalletCards,
  X,
  type LucideIcon,
} from 'lucide-react'
import type {
  LedgerEntry,
  SessionTaskInput,
  StudySession,
  TimelineActivity,
  TrackingSettings,
  Reward,
  ChallengeOverview,
  ChallengeRound,
  PlanningTask,
  PlanningTimeBlock,
  TodayPlan,
  WeekPlan,
  TaskRelevanceSnapshot,
  AiSettings,
} from './bridge/types'
import { callBridge } from './bridge/webview'
import { useSessionStore } from './stores/sessionStore'
import { useTimelineStore } from './stores/timelineStore'
import { useRewardStore } from './stores/rewardStore'
import { useAnalyticsStore } from './stores/analyticsStore'
import { useChallengeStore } from './stores/challengeStore'
import { useTaskStore } from './stores/taskStore'
import { usePlannerStore } from './stores/plannerStore'
import { useRelevanceStore } from './stores/relevanceStore'
import './App.css'

type Theme = 'light' | 'dark'
type ViewKey =
  | 'focus'
  | 'planner'
  | 'timeline'
  | 'analytics'
  | 'rewards'
  | 'challenge'
  | 'settings'

type ViewDefinition = {
  key: ViewKey
  label: string
  icon: LucideIcon
}

type FeedbackTone = 'success' | 'info'

type FeedbackNotice = {
  id: string
  tone: FeedbackTone
  title: string
  detail?: string
  action?: {
    label: string
    onSelect(): void
  }
}

type FeedbackInput = Omit<FeedbackNotice, 'id'>

type DisplayApplicationUsage = {
  processName: string
  durationSeconds: number
}

const FeedbackContext = createContext<{
  showFeedback(notice: FeedbackInput): void
} | null>(null)

const views: ViewDefinition[] = [
  { key: 'focus', label: '今日专注', icon: Clock3 },
  { key: 'planner', label: '本周计划', icon: CalendarRange },
  { key: 'timeline', label: '学习时间轴', icon: CalendarDays },
  { key: 'analytics', label: '数据统计', icon: ChartNoAxesColumnIncreasing },
  { key: 'rewards', label: '奖励商店', icon: Gift },
  { key: 'challenge', label: '时间挑战', icon: Sparkles },
  { key: 'settings', label: '设置', icon: Settings2 },
]

const emptyStateCopy: Record<Exclude<ViewKey, 'focus'>, string> = {
  planner: '把任务安排到本周的具体日期和时间。',
  timeline: '完成第一个学习时间块后，今天的活动会出现在这里。',
  analytics: '学习记录积累后，这里会显示日、周和科目趋势。',
  rewards: '还没有自定义奖励。下一阶段会接入奖励的创建与兑换。',
  challenge: '完成学习并获得可用时间后，时间挑战才会开启。',
  settings: '偏好设置将在后续阶段逐步开放。',
}

function useFeedback() {
  const feedback = useContext(FeedbackContext)
  if (!feedback) throw new Error('FeedbackContext is unavailable.')
  return feedback
}

function TimelineView() {
  const { overview, loading, error, load, clearError } = useTimelineStore()

  useEffect(() => {
    void load()
  }, [load])

  if (loading && !overview) {
    return <LoadingPanel label="正在整理今天的本地活动" />
  }

  if (!overview || overview.timeline.sessions.length === 0) {
    return (
      <>
        {error && <InlineError message={error} onClose={clearError} />}
        <section className="empty-view timeline-empty">
          <MonitorDot aria-hidden="true" size={28} />
          <h2>今天还没有活动记录</h2>
          <p>开始一个学习时间块后，前台应用会按时间片保存到本机。</p>
          <span className="privacy-chip">
            <ShieldCheck aria-hidden="true" size={14} />
            截图默认关闭
          </span>
        </section>
      </>
    )
  }

  const { timeline } = overview
  const displaySessions = timeline.sessions.map((session) => ({
    ...session,
    applications: summarizeApplicationUsage(session.activities),
    idleSeconds: session.activities
      .filter((activity) => activity.isIdle)
      .reduce(
        (total, activity) => total + activityDurationSeconds(activity),
        0,
      ),
  }))
  return (
    <>
      {error && <InlineError message={error} onClose={clearError} />}
      <div className="timeline-toolbar">
        <div className="timeline-date-stamp">
          <span>{formatArchiveDate(timeline.dayStartedAtUtc)}</span>
          <strong>今日学习档案</strong>
        </div>
        <button
          className="icon-button"
          type="button"
          aria-label="刷新时间轴"
          title="刷新"
          disabled={loading}
          onClick={() => void load()}
        >
          <RefreshCw
            aria-hidden="true"
            className={loading ? 'spin' : undefined}
            size={17}
          />
        </button>
      </div>
      <div className="timeline-summary">
        <div>
          <span>记录活动</span>
          <strong>{formatBalance(timeline.trackedSeconds)}</strong>
        </div>
        <div>
          <span>空闲时间</span>
          <strong>{formatBalance(timeline.idleSeconds)}</strong>
        </div>
        <div>
          <span>本地截图</span>
          <strong>
            {timeline.sessions.reduce(
              (sum, session) => sum + session.screenshots.length,
              0,
            )}{' '}
            张
          </strong>
        </div>
      </div>
      <div className="timeline-list">
        {displaySessions.map((session, index) => (
          <section className="timeline-session" key={session.id}>
            <span className="archive-index" aria-hidden="true">
              {String(index + 1).padStart(2, '0')}
            </span>
            <div className="timeline-session-heading">
              <div>
                <span>{formatTime(session.startedAtUtc)} 开始</span>
                <h2>{session.goal}</h2>
                <div className="archive-tags">
                  <span>{session.applications.length} 个应用</span>
                  {session.idleSeconds > 0 && (
                    <span>
                      空闲 {formatCompactDuration(session.idleSeconds)}
                    </span>
                  )}
                  <span>{session.screenshots.length} 张截图</span>
                </div>
              </div>
              <strong>{formatBalance(session.effectiveDurationSeconds)}</strong>
            </div>
            {session.applications.length > 0 ? (
              <div className="activity-list">
                {session.applications.map((application) => (
                  <ApplicationUsageRow
                    key={application.processName.toLowerCase()}
                    application={application}
                  />
                ))}
              </div>
            ) : (
              <p className="no-activity">这个时间块还没有采集到应用用时。</p>
            )}
            {session.screenshots.length > 0 && (
              <div className="screenshot-strip">
                {session.screenshots.map((screenshot) => (
                  <a
                    href={screenshot.url}
                    target="_blank"
                    rel="noreferrer"
                    key={screenshot.id}
                    title={`${formatTime(screenshot.capturedAtUtc)} 截图`}
                  >
                    <img
                      src={screenshot.url}
                      alt={`${formatTime(screenshot.capturedAtUtc)} 的学习截图`}
                    />
                    <span>{formatTime(screenshot.capturedAtUtc)}</span>
                  </a>
                ))}
              </div>
            )}
          </section>
        ))}
      </div>
    </>
  )
}

function ApplicationUsageRow({
  application,
}: {
  application: DisplayApplicationUsage
}) {
  return (
    <div className="activity-row">
      <div className="activity-marker">
        <span />
      </div>
      <div className="activity-main">
        <div>
          <strong>{application.processName}</strong>
          <span>本次累计</span>
        </div>
      </div>
      <span className="activity-duration">
        {formatCompactDuration(application.durationSeconds)}
      </span>
    </div>
  )
}

function summarizeApplicationUsage(
  activities: TimelineActivity[],
): DisplayApplicationUsage[] {
  const usageByProcess = new Map<string, DisplayApplicationUsage>()

  for (const activity of activities) {
    if (activity.isIdle) {
      continue
    }

    const processKey = activity.processName.trim().toLowerCase()
    const existing = usageByProcess.get(processKey)
    if (existing) {
      existing.durationSeconds += activityDurationSeconds(activity)
      continue
    }

    usageByProcess.set(processKey, {
      processName: activity.processName,
      durationSeconds: activityDurationSeconds(activity),
    })
  }

  return [...usageByProcess.values()].sort(
    (left, right) =>
      right.durationSeconds - left.durationSeconds ||
      left.processName.localeCompare(right.processName),
  )
}

function activityDurationSeconds(activity: TimelineActivity) {
  return Math.max(
    0,
    Math.floor(
      (new Date(activity.endedAtUtc).getTime() -
        new Date(activity.startedAtUtc).getTime()) /
        1000,
    ),
  )
}

function SettingsView() {
  const { showFeedback } = useFeedback()
  const { overview, loading, saving, error, load, update, clearError } =
    useTimelineStore()
  const [draft, setDraft] = useState<TrackingSettings | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    void load()
  }, [load])
  useEffect(() => {
    if (overview) setDraft(overview.settings)
  }, [overview])

  if ((loading && !draft) || !draft)
    return <LoadingPanel label="正在读取本地隐私设置" />

  const patch = (changes: Partial<TrackingSettings>) => {
    setSaved(false)
    setDraft((current) => (current ? { ...current, ...changes } : current))
  }
  return (
    <>
      {error && <InlineError message={error} onClose={clearError} />}
      <section className="settings-section" aria-labelledby="tracking-title">
        <div className="settings-heading">
          <div>
            <span>本地采集</span>
            <h2 id="tracking-title">活动与截图隐私</h2>
          </div>
          <ShieldCheck aria-hidden="true" size={22} />
        </div>
        <div className="setting-row">
          <div>
            <strong>记录前台应用</strong>
            <p>仅在学习计时运行时保存应用名称、窗口标题和空闲状态。</p>
          </div>
          <Toggle
            checked={draft.captureActivity}
            label="记录前台应用"
            onChange={(checked) =>
              patch({
                captureActivity: checked,
                captureScreenshots: checked ? draft.captureScreenshots : false,
              })
            }
          />
        </div>
        <div className="setting-row">
          <div>
            <strong>定时截图</strong>
            <p>抓取当前前台窗口并保存在本机，空闲和暂停时不会截图。</p>
          </div>
          <Toggle
            checked={draft.captureScreenshots}
            disabled={!draft.captureActivity}
            label="定时截图"
            onChange={(checked) => patch({ captureScreenshots: checked })}
          />
        </div>
        <div className="setting-grid">
          <label>
            截图间隔
            <select
              value={draft.screenshotIntervalMinutes}
              disabled={!draft.captureScreenshots}
              onChange={(event) =>
                patch({ screenshotIntervalMinutes: Number(event.target.value) })
              }
            >
              <option value={1}>1 分钟</option>
              <option value={5}>5 分钟</option>
              <option value={10}>10 分钟</option>
              <option value={15}>15 分钟</option>
              <option value={30}>30 分钟</option>
            </select>
          </label>
          <label>
            截图保留
            <select
              value={draft.screenshotRetentionDays}
              disabled={!draft.captureScreenshots}
              onChange={(event) =>
                patch({ screenshotRetentionDays: Number(event.target.value) })
              }
            >
              <option value={7}>7 天</option>
              <option value={30}>30 天</option>
              <option value={90}>90 天</option>
              <option value={3650}>长期保留</option>
            </select>
          </label>
          <label>
            空闲阈值
            <select
              value={draft.idleThresholdMinutes}
              disabled={!draft.captureActivity}
              onChange={(event) =>
                patch({ idleThresholdMinutes: Number(event.target.value) })
              }
            >
              <option value={1}>1 分钟</option>
              <option value={3}>3 分钟</option>
              <option value={5}>5 分钟</option>
              <option value={10}>10 分钟</option>
              <option value={15}>15 分钟</option>
            </select>
          </label>
        </div>
        <div className="settings-footer">
          <p data-saved={saved}>
            <ShieldCheck aria-hidden="true" size={15} />
            {saved
              ? '设置已保存，下一次采样立即生效。'
              : '所有记录默认仅在本机保存，不会上传。'}
          </p>
          <button
            className="primary-action"
            type="button"
            disabled={saving}
            aria-busy={saving}
            onClick={async () => {
              const ok = await update(draft)
              setSaved(ok)
              if (ok) {
                showFeedback({
                  tone: 'success',
                  title: '隐私设置已保存',
                  detail: '新的采集规则将在下一次采样时生效。',
                })
              }
            }}
          >
            {saving ? (
              <LoaderCircle aria-hidden="true" className="spin" size={18} />
            ) : (
              <Check aria-hidden="true" size={18} />
            )}
            保存设置
          </button>
        </div>
      </section>
      <AiSettingsSection />
      <ChallengePreference />
    </>
  )
}

function AiSettingsSection() {
  const { showFeedback } = useFeedback()
  const [settings, setSettings] = useState<AiSettings | null>(null)
  const [enabled, setEnabled] = useState(false)
  const [baseUrl, setBaseUrl] = useState('')
  const [model, setModel] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    void callBridge<AiSettings>('ai.settings.get')
      .then((value) => {
        if (!active) return
        setSettings(value)
        setEnabled(value.enabled)
        setBaseUrl(value.baseUrl)
        setModel(value.model)
      })
      .catch((reason) => {
        if (active)
          setError(
            reason instanceof Error ? reason.message : '读取 AI 设置失败',
          )
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
    }
  }, [])

  const save = async () => {
    setSaving(true)
    setError(null)
    try {
      const next = await callBridge<AiSettings>('ai.settings.update', {
        enabled,
        baseUrl: baseUrl.trim(),
        model: model.trim(),
        apiKey: apiKey.trim() || undefined,
      })
      setSettings(next)
      setEnabled(next.enabled)
      setBaseUrl(next.baseUrl)
      setModel(next.model)
      setApiKey('')
      showFeedback({
        tone: 'success',
        title: 'AI 设置已保存',
        detail: next.enabled
          ? '今日回顾会优先尝试使用 AI 分析。'
          : '已关闭 AI 分析，将继续使用本地回顾。',
      })
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '保存 AI 设置失败')
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <LoadingPanel label="正在读取 AI 设置" />

  return (
    <section
      className="settings-section ai-settings-section"
      aria-labelledby="ai-settings-title"
    >
      {error && <InlineError message={error} onClose={() => setError(null)} />}
      <div className="settings-heading">
        <div>
          <span>可选增强</span>
          <h2 id="ai-settings-title">AI 今日回顾</h2>
        </div>
        <Sparkles aria-hidden="true" size={22} />
      </div>
      <div className="setting-row">
        <div>
          <strong>启用 AI 分析</strong>
          <p>
            使用兼容 OpenAI API
            的服务生成更自然的任务回顾，关闭后仍保留本地回顾。
          </p>
        </div>
        <Toggle checked={enabled} label="启用 AI 分析" onChange={setEnabled} />
      </div>
      <div className="ai-settings-form">
        <label>
          API 地址
          <input
            value={baseUrl}
            onChange={(event) => setBaseUrl(event.target.value)}
            placeholder="https://api.openai.com/v1"
          />
        </label>
        <label>
          模型名称
          <input
            value={model}
            onChange={(event) => setModel(event.target.value)}
            placeholder="gpt-4o-mini"
          />
        </label>
        <label className="ai-api-key-field">
          API Key
          <input
            type="password"
            value={apiKey}
            onChange={(event) => setApiKey(event.target.value)}
            placeholder={
              settings?.apiKeyConfigured
                ? `已配置 ${settings.apiKeyHint ?? ''}，留空则保持不变`
                : '输入 API Key'
            }
            autoComplete="off"
          />
        </label>
      </div>
      <div className="settings-footer">
        <p>
          <ShieldCheck aria-hidden="true" size={15} />
          只发送结构化统计，不发送窗口标题、截图或文件内容。API Key 使用 Windows
          用户级加密保存在本机。
        </p>
        <button
          className="primary-action"
          type="button"
          disabled={saving}
          aria-busy={saving}
          onClick={() => void save()}
        >
          {saving ? (
            <LoaderCircle aria-hidden="true" className="spin" size={18} />
          ) : (
            <Check aria-hidden="true" size={18} />
          )}
          保存 AI 设置
        </button>
      </div>
    </section>
  )
}

function ChallengePreference() {
  const { showFeedback } = useFeedback()
  const { overview, loading, saving, load, setEnabled } = useChallengeStore()

  useEffect(() => {
    void load()
  }, [load])

  return (
    <section
      className="settings-section challenge-setting"
      aria-labelledby="challenge-setting-title"
    >
      <div className="setting-row">
        <div>
          <strong id="challenge-setting-title">启用星愿机</strong>
          <p>每天最多 3 次，只使用本地虚拟学习时间，可随时关闭。</p>
        </div>
        <Toggle
          checked={overview?.enabled ?? false}
          disabled={loading || saving || !overview}
          label="启用星愿机"
          onChange={async (enabled) => {
            const ok = await setEnabled(enabled)
            if (ok) {
              showFeedback({
                tone: 'info',
                title: enabled ? '星愿机已开启' : '星愿机已关闭',
                detail: enabled
                  ? '每天最多体验限定次数，随时可以在这里关闭。'
                  : '历史记录会保留，已经积累的学习时间不受影响。',
              })
            }
          }}
        />
      </div>
    </section>
  )
}

function AnalyticsView() {
  const {
    overview,
    review,
    entries,
    loading,
    exporting,
    exportedFileName,
    error,
    load,
    exportSummary,
    clearError,
  } = useAnalyticsStore()

  useEffect(() => {
    void load()
  }, [load])

  if (loading && !overview) {
    return <LoadingPanel label="正在汇总本地学习数据" />
  }

  if (!overview) {
    return (
      <>
        {error && <InlineError message={error} onClose={clearError} />}
        <section className="empty-view">
          <ChartNoAxesColumnIncreasing aria-hidden="true" size={28} />
          <h2>统计数据暂时不可用</h2>
          <p>完成一次学习时间块后再回来查看。</p>
        </section>
      </>
    )
  }

  const trendMaximum = Math.max(
    1,
    ...overview.dailyTrend.map((day) => day.studySeconds),
  )
  const applicationMaximum = Math.max(
    1,
    ...overview.applicationUsage.map(
      (application) => application.durationSeconds,
    ),
  )

  return (
    <>
      {error && <InlineError message={error} onClose={clearError} />}
      <div className="analytics-toolbar">
        <div>
          <span>截至 {formatTime(overview.generatedAtUtc)}</span>
          <strong>近 7 天学习概览</strong>
        </div>
        <div className="analytics-actions">
          {exportedFileName && <span>已保存 {exportedFileName}</span>}
          <button
            className="icon-button"
            type="button"
            aria-label="刷新统计"
            title="刷新"
            disabled={loading}
            onClick={() => void load()}
          >
            <RefreshCw
              aria-hidden="true"
              className={loading ? 'spin' : undefined}
              size={16}
            />
          </button>
          <button
            className="secondary-action analytics-export"
            type="button"
            disabled={exporting}
            aria-busy={exporting}
            onClick={() => void exportSummary()}
          >
            {exporting ? (
              <LoaderCircle aria-hidden="true" className="spin" size={17} />
            ) : (
              <Download aria-hidden="true" size={17} />
            )}
            导出摘要
          </button>
        </div>
      </div>

      {review && (
        <section
          className="daily-review-panel"
          aria-labelledby="daily-review-title"
        >
          <div className="daily-review-heading">
            <div>
              <span>
                {review.source === 'ai'
                  ? `AI 智能回顾${review.aiModel ? ` · ${review.aiModel}` : ''}`
                  : '本地智能回顾'}
              </span>
              <h2 id="daily-review-title">今日回顾</h2>
            </div>
            <div className="daily-review-private">
              <ShieldCheck aria-hidden="true" size={15} />
              仅在本机生成 · 不上传数据
            </div>
          </div>

          <div className="daily-review-hero">
            <div className="daily-review-icon" aria-hidden="true">
              <Sparkles size={21} />
            </div>
            <div>
              <h3>{review.headline}</h3>
              <p>{review.narrative}</p>
            </div>
          </div>

          <div className="daily-review-metrics" aria-label="今日回顾指标">
            <div>
              <span>有效学习</span>
              <strong>{formatBalance(review.metrics.studySeconds)}</strong>
              <small>{review.metrics.completedSessionCount} 个学习时间块</small>
            </div>
            <div>
              <span>计划完成</span>
              <strong>
                {review.metrics.plannedBlockCount > 0
                  ? `${review.metrics.completedBlockCount}/${review.metrics.plannedBlockCount}`
                  : '未安排'}
              </strong>
              <small>
                {review.metrics.plannedSeconds > 0
                  ? `计划 ${formatBalance(review.metrics.plannedSeconds)}`
                  : '今天还没有计划块'}
              </small>
            </div>
            <div>
              <span>任务相关证据</span>
              <strong>
                {!review.relevance.activityTrackingEnabled
                  ? '未开启'
                  : review.relevance.evaluatedSeconds > 0
                    ? formatPercentage(
                        (review.relevance.highSeconds +
                          review.relevance.possibleSeconds) /
                          review.relevance.evaluatedSeconds,
                      )
                    : '证据不足'}
              </strong>
              <small>较强相关与可能相关，仅供复盘参考</small>
            </div>
            <div>
              <span>空闲记录</span>
              <strong>{formatBalance(review.metrics.idleSeconds)}</strong>
              <small>
                {review.metrics.trackedSeconds > 0
                  ? `占已记录时长 ${formatPercentage(
                      review.metrics.idleSeconds /
                        review.metrics.trackedSeconds,
                    )}`
                  : '尚无活动记录'}
              </small>
            </div>
          </div>

          <div className="daily-review-details">
            <div>
              <h3>今天做得怎么样</h3>
              {review.highlights.length > 0 ? (
                <ul>
                  {review.highlights.map((highlight) => (
                    <li key={highlight}>
                      <Check aria-hidden="true" size={14} />
                      <span>{highlight}</span>
                    </li>
                  ))}
                </ul>
              ) : (
                <p>开始第一次专注后，这里会列出今天的客观记录。</p>
              )}
            </div>
            <div>
              <h3>下一步建议</h3>
              <ul>
                {review.suggestions.map((suggestion) => (
                  <li key={suggestion}>
                    <Sparkles aria-hidden="true" size={14} />
                    <span>{suggestion}</span>
                  </li>
                ))}
              </ul>
            </div>
          </div>

          <div className="daily-review-footnote">
            结论来自任务、计时、软件与窗口标题的本地规则分析；它是辅助线索，不是对专注状态的绝对判定。
          </div>
        </section>
      )}

      <section className="analytics-summary" aria-label="学习统计摘要">
        <div>
          <span>今日学习</span>
          <strong>{formatBalance(overview.todayStudySeconds)}</strong>
          <small>{overview.todayCompletedSessions} 个时间块</small>
        </div>
        <div>
          <span>本周学习</span>
          <strong>{formatBalance(overview.weekStudySeconds)}</strong>
          <small>{overview.weekCompletedSessions} 个时间块</small>
        </div>
        <div>
          <span>可用时间</span>
          <strong>{formatBalance(overview.ledger.balanceSeconds)}</strong>
          <small>收入 {formatBalance(overview.ledger.earnedSeconds)}</small>
        </div>
        <div>
          <span>累计消费</span>
          <strong>{formatBalance(overview.ledger.spentSeconds)}</strong>
          <small>奖励与挑战支出</small>
        </div>
      </section>

      <div className="analytics-grid">
        <section
          className="analytics-panel trend-panel"
          aria-labelledby="trend-title"
        >
          <div className="analytics-panel-heading">
            <div>
              <span>学习趋势</span>
              <h2 id="trend-title">近 7 天有效时长</h2>
            </div>
            <ChartNoAxesColumnIncreasing aria-hidden="true" size={19} />
          </div>
          <div className="trend-chart">
            {overview.dailyTrend.map((day) => {
              const height =
                day.studySeconds === 0
                  ? 0
                  : Math.max(6, (day.studySeconds / trendMaximum) * 100)
              return (
                <div className="trend-day" key={day.date}>
                  <span className="trend-value">
                    {formatChartDuration(day.studySeconds)}
                  </span>
                  <div className="trend-track">
                    <span style={{ height: `${height}%` }} />
                  </div>
                  <strong>{formatTrendDay(day.dayStartedAtUtc)}</strong>
                  <small>{day.completedSessions} 块</small>
                </div>
              )
            })}
          </div>
        </section>

        <section
          className="analytics-panel application-panel"
          aria-labelledby="application-title"
        >
          <div className="analytics-panel-heading">
            <div>
              <span>今日活动</span>
              <h2 id="application-title">前台应用分布</h2>
            </div>
            <MonitorDot aria-hidden="true" size={19} />
          </div>
          {overview.applicationUsage.length === 0 ? (
            <p className="analytics-empty">今天还没有可统计的应用活动。</p>
          ) : (
            <div className="application-usage-list">
              {overview.applicationUsage.map((application) => (
                <div
                  className="application-usage"
                  key={application.processName}
                >
                  <div>
                    <strong>{application.processName}</strong>
                    <span>{Math.round(application.share * 100)}%</span>
                  </div>
                  <div className="usage-track">
                    <span
                      style={{
                        width: `${(application.durationSeconds / applicationMaximum) * 100}%`,
                      }}
                    />
                  </div>
                  <small>{formatBalance(application.durationSeconds)}</small>
                </div>
              ))}
            </div>
          )}
        </section>
      </div>

      <section className="ledger-panel" aria-labelledby="ledger-title">
        <div className="ledger-heading">
          <div>
            <span>时间账本</span>
            <h2 id="ledger-title">最近收支明细</h2>
          </div>
          <strong>{entries.length} 条</strong>
        </div>
        {entries.length === 0 ? (
          <p className="analytics-empty ledger-empty">还没有时间收入或支出。</p>
        ) : (
          <div className="ledger-list">
            {entries.map((entry) => (
              <LedgerRow entry={entry} key={entry.id} />
            ))}
          </div>
        )}
      </section>
    </>
  )
}

function LedgerRow({ entry }: { entry: LedgerEntry }) {
  const positive = entry.amountSeconds > 0
  return (
    <div className="ledger-row" data-positive={positive}>
      <span className="ledger-direction">{positive ? '+' : '-'}</span>
      <div className="ledger-detail">
        <div>
          <strong>{entry.note || ledgerTypeLabel(entry.type)}</strong>
          <span>{ledgerTypeLabel(entry.type)}</span>
        </div>
        <small>{formatDateTime(entry.occurredAtUtc)}</small>
      </div>
      <b>
        {positive ? '+' : '-'} {formatBalance(Math.abs(entry.amountSeconds))}
      </b>
    </div>
  )
}

function ChallengeView() {
  const { showFeedback } = useFeedback()
  const {
    overview,
    receipt,
    loading,
    saving,
    spinning,
    settling,
    error,
    load,
    setEnabled,
    play,
    clearError,
    dismissReceipt,
  } = useChallengeStore()
  const [wagerSeconds, setWagerSeconds] = useState(60)
  const challengeBusy = spinning || settling

  const updateEnabled = async (enabled: boolean) => {
    const ok = await setEnabled(enabled)
    if (ok) {
      showFeedback({
        tone: 'info',
        title: enabled ? '星愿机已开启' : '星愿机已关闭',
        detail: enabled
          ? '规则与概率保持公开，今天的机会已经准备好了。'
          : '挑战记录会继续保留，可以随时再次开启。',
      })
    }
  }

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    if (!overview) return
    setWagerSeconds((current) =>
      Math.max(
        overview.minimumWagerSeconds,
        Math.min(
          current,
          Math.max(overview.minimumWagerSeconds, overview.maximumWagerSeconds),
        ),
      ),
    )
  }, [overview])

  if (loading && !overview) return <LoadingPanel label="正在整理今日星愿机会" />
  if (!overview) return <LoadingPanel label="星愿机暂时不可用" />

  const latestSymbols = receipt?.symbols ??
    overview.recentRounds[0]?.symbols ?? ['book', 'star', 'medal']
  const disabledReason = getChallengeDisabledReason(overview)
  const canPlay =
    !challengeBusy &&
    !disabledReason &&
    wagerSeconds <= overview.maximumWagerSeconds

  return (
    <>
      {error && <InlineError message={error} onClose={clearError} />}
      {!overview.enabled ? (
        <section
          className="challenge-opt-in"
          aria-labelledby="challenge-opt-in-title"
        >
          <div className="opt-in-mark">
            <Sparkles aria-hidden="true" size={30} />
          </div>
          <span>可选娱乐玩法</span>
          <h2 id="challenge-opt-in-title">开启星愿机</h2>
          <p>
            星愿机只使用本地虚拟学习时间，不涉及金钱、内购或真实商品。每天最多{' '}
            {overview.dailyRoundLimit} 次，单次不超过余额的{' '}
            {formatPercentage(overview.maximumWagerBalanceShare)}。
          </p>
          <div className="opt-in-rules">
            <div>
              <strong>{formatPercentage(overview.expectedReturn)}</strong>
              <span>长期平均返还</span>
            </div>
            <div>
              <strong>{overview.dailyRoundLimit} 次</strong>
              <span>每日机会</span>
            </div>
            <div>
              <strong>
                {formatPercentage(overview.maximumWagerBalanceShare)}
              </strong>
              <span>单次余额上限</span>
            </div>
          </div>
          <button
            className="primary-action"
            type="button"
            disabled={saving}
            aria-busy={saving}
            onClick={() => void updateEnabled(true)}
          >
            {saving ? (
              <LoaderCircle aria-hidden="true" className="spin" size={18} />
            ) : (
              <Sparkles aria-hidden="true" size={18} />
            )}
            了解并开启
          </button>
        </section>
      ) : (
        <>
          <section className="challenge-status" aria-label="星愿机状态">
            <div>
              <span>可用时间</span>
              <strong>{formatBalance(overview.balanceSeconds)}</strong>
            </div>
            <div>
              <span>今日机会</span>
              <strong>
                {overview.roundsRemainingToday} / {overview.dailyRoundLimit}
              </strong>
            </div>
            <div>
              <span>本次上限</span>
              <strong>{formatBalance(overview.maximumWagerSeconds)}</strong>
            </div>
            <button
              className="icon-button"
              type="button"
              aria-label="关闭星愿机"
              title="关闭星愿机"
              disabled={saving || challengeBusy}
              aria-busy={saving || settling}
              onClick={() => void updateEnabled(false)}
            >
              <Power aria-hidden="true" size={16} />
            </button>
          </section>

          <div className="challenge-layout">
            <section
              className="wish-machine"
              data-spinning={spinning}
              data-settling={settling}
              aria-labelledby="wish-machine-title"
            >
              <div className="wish-machine-heading">
                <div>
                  <span>STAR WISH</span>
                  <h2 id="wish-machine-title">星愿机</h2>
                </div>
                <div className="wish-lights" aria-hidden="true">
                  <i />
                  <i />
                  <i />
                  <i />
                  <i />
                </div>
              </div>
              <img
                className="challenge-machine-mascot"
                data-state={
                  spinning ? 'spinning' : settling ? 'settling' : 'idle'
                }
                src="/assets/illustrations/star-wish-celebration.png"
                alt=""
                aria-hidden="true"
                draggable="false"
              />
              <div
                className="reels"
                data-spinning={spinning}
                data-settling={settling}
                aria-label="星愿结果"
              >
                {latestSymbols.map((symbol, index) => (
                  <div className="reel" key={index}>
                    <div className="reel-symbol">
                      <ChallengeSymbol symbol={symbol} />
                    </div>
                  </div>
                ))}
              </div>
              <div className="wager-control">
                <span>投入时间</span>
                <div>
                  <button
                    type="button"
                    aria-label="减少投入一分钟"
                    disabled={
                      challengeBusy ||
                      wagerSeconds <= overview.minimumWagerSeconds
                    }
                    onClick={() => setWagerSeconds((current) => current - 60)}
                  >
                    <Minus aria-hidden="true" size={17} />
                  </button>
                  <strong>{formatBalance(wagerSeconds)}</strong>
                  <button
                    type="button"
                    aria-label="增加投入一分钟"
                    disabled={
                      challengeBusy ||
                      wagerSeconds >= overview.maximumWagerSeconds
                    }
                    onClick={() => setWagerSeconds((current) => current + 60)}
                  >
                    <Plus aria-hidden="true" size={17} />
                  </button>
                </div>
              </div>
              <button
                className="primary-action wish-action"
                type="button"
                disabled={challengeBusy || !canPlay}
                aria-busy={challengeBusy}
                title={disabledReason ?? '开启一次星愿'}
                onClick={() => void play(wagerSeconds)}
              >
                {challengeBusy ? (
                  <LoaderCircle aria-hidden="true" className="spin" size={19} />
                ) : (
                  <Sparkles aria-hidden="true" size={19} />
                )}
                {spinning
                  ? '星轨转动中'
                  : settling
                    ? '正在揭示结果'
                    : '开启星愿'}
              </button>
              <p className="challenge-limit-note">
                {settling
                  ? '星轨已经停下，正在揭示这次挑战的结果。'
                  : (disabledReason ??
                    '结果由本机加密随机数生成，滚轴动画不会改变结算。')}
              </p>
            </section>

            <section
              className="probability-panel"
              aria-labelledby="probability-title"
            >
              <div className="probability-heading">
                <div>
                  <span>公开规则</span>
                  <h2 id="probability-title">概率与返还</h2>
                </div>
                <ShieldCheck aria-hidden="true" size={18} />
              </div>
              <div className="probability-list">
                {overview.probabilityTiers.map((tier) => (
                  <div className="probability-row" key={tier.code}>
                    <ChallengeSymbol symbol={tier.symbols[1]} />
                    <div>
                      <strong>{tier.label}</strong>
                      <span>
                        {formatMultiplier(tier.payoutMultiplier)} 倍返还
                      </span>
                    </div>
                    <b>{Math.round(tier.probability * 100)}%</b>
                  </div>
                ))}
              </div>
              <p>
                版本 {overview.probabilityVersion} · 长期平均返还{' '}
                {formatPercentage(overview.expectedReturn)}
              </p>
            </section>
          </div>

          <ChallengeHistory rounds={overview.recentRounds} />
        </>
      )}
      {receipt && (
        <ChallengeReceipt
          round={receipt}
          balanceSeconds={overview.balanceSeconds}
          onClose={dismissReceipt}
        />
      )}
    </>
  )
}

function ChallengeHistory({ rounds }: { rounds: ChallengeRound[] }) {
  if (rounds.length === 0) return null
  return (
    <section
      className="challenge-history"
      aria-labelledby="challenge-history-title"
    >
      <div className="section-caption">
        <span id="challenge-history-title">最近星愿</span>
        <strong>{rounds.length}</strong>
      </div>
      {rounds.map((round) => (
        <div
          className="challenge-history-row"
          data-positive={round.netSeconds > 0}
          key={round.id}
        >
          <div className="history-symbols">
            {round.symbols.map((symbol, index) => (
              <ChallengeSymbol symbol={symbol} key={`${round.id}-${index}`} />
            ))}
          </div>
          <div>
            <strong>{round.outcomeLabel}</strong>
            <span>
              {formatDateTime(round.playedAtUtc)} · 投入{' '}
              {formatBalance(round.wagerSeconds)}
            </span>
          </div>
          <b>
            {round.netSeconds > 0 ? '+' : '-'}{' '}
            {formatPreciseDuration(Math.abs(round.netSeconds))}
          </b>
        </div>
      ))}
    </section>
  )
}

function ChallengeReceipt({
  round,
  balanceSeconds,
  onClose,
}: {
  round: ChallengeRound
  balanceSeconds: number
  onClose(): void
}) {
  const won = round.netSeconds > 0
  return (
    <div className="modal-backdrop" role="presentation">
      <section
        className="dialog receipt-dialog challenge-receipt"
        role="dialog"
        aria-modal="true"
        aria-labelledby="challenge-receipt-title"
        data-won={won}
      >
        {won && (
          <img
            className="challenge-result-illustration"
            src="/assets/illustrations/star-wish-celebration.png"
            alt=""
            aria-hidden="true"
            draggable="false"
          />
        )}
        <div className="receipt-symbols">
          {round.symbols.map((symbol, index) => (
            <ChallengeSymbol symbol={symbol} key={index} />
          ))}
        </div>
        <span className="receipt-kicker">星愿结算完成</span>
        <h2 id="challenge-receipt-title">{round.outcomeLabel}</h2>
        <p>
          投入 {formatBalance(round.wagerSeconds)}，返还{' '}
          {formatPreciseDuration(round.payoutSeconds)}。
        </p>
        <div className="challenge-net">
          <span>本局净变化</span>
          <strong>
            {won ? '+' : '-'}{' '}
            {formatPreciseDuration(Math.abs(round.netSeconds))}
          </strong>
        </div>
        <div className="receipt-balance">
          <span>当前可用时间</span>
          <strong>{formatBalance(balanceSeconds)}</strong>
        </div>
        <button className="primary-action" type="button" onClick={onClose}>
          <Check aria-hidden="true" size={18} />
          收好结果
        </button>
      </section>
    </div>
  )
}

function ChallengeSymbol({ symbol }: { symbol: string }) {
  const Icon =
    symbol === 'book'
      ? BookOpen
      : symbol === 'medal'
        ? Medal
        : symbol === 'crown'
          ? Crown
          : symbol === 'moon'
            ? Moon
            : symbol === 'cloud'
              ? Cloud
              : Sparkles
  return <Icon aria-hidden="true" size={28} />
}

function RewardsView() {
  const { showFeedback } = useFeedback()
  const {
    catalog,
    receipt,
    loading,
    submitting,
    error,
    load,
    create,
    update,
    setActive,
    redeem,
    clearError,
    dismissReceipt,
  } = useRewardStore()
  const [showEditor, setShowEditor] = useState(false)
  const [editing, setEditing] = useState<Reward | null>(null)
  const [redeeming, setRedeeming] = useState<Reward | null>(null)

  useEffect(() => {
    void load()
  }, [load])

  if (loading && !catalog) return <LoadingPanel label="正在读取奖励商店" />
  if (!catalog) return <LoadingPanel label="奖励商店暂时不可用" />

  const activeRewards = catalog.rewards.filter((reward) => reward.isActive)
  const inactiveRewards = catalog.rewards.filter((reward) => !reward.isActive)
  const updateRewardActive = async (reward: Reward, isActive: boolean) => {
    const ok = await setActive(reward.id, isActive)
    if (ok) {
      showFeedback({
        tone: 'info',
        title: isActive ? '奖励已重新启用' : '奖励已停用',
        detail: reward.name,
      })
    }
  }
  return (
    <>
      {error && <InlineError message={error} onClose={clearError} />}
      <div className="reward-toolbar">
        <div>
          <span>把学习时间换成自己的小奖励</span>
          <strong>{formatBalance(catalog.balanceSeconds)}</strong>
        </div>
        <button
          className="primary-action"
          type="button"
          onClick={() => {
            setEditing(null)
            setShowEditor(true)
          }}
        >
          <Plus aria-hidden="true" size={18} />
          新建奖励
        </button>
        <img
          className="reward-mascot"
          src="/assets/illustrations/time-deposit-chibi.png"
          alt=""
          aria-hidden="true"
          draggable="false"
        />
      </div>
      {activeRewards.length === 0 ? (
        <section className="empty-view reward-empty">
          <Gift aria-hidden="true" size={30} />
          <h2>还没有可兑换的奖励</h2>
          <p>先创建一个对自己有吸引力的小奖励，例如奶茶、电影或休息半小时。</p>
          <button
            className="secondary-action"
            type="button"
            onClick={() => {
              setEditing(null)
              setShowEditor(true)
            }}
          >
            <Plus aria-hidden="true" size={17} />
            创建第一项
          </button>
        </section>
      ) : (
        <div className="reward-grid">
          {activeRewards.map((reward) => (
            <RewardItem
              key={reward.id}
              reward={reward}
              balanceSeconds={catalog.balanceSeconds}
              disabled={submitting}
              onEdit={() => {
                setEditing(reward)
                setShowEditor(true)
              }}
              onToggle={() => void updateRewardActive(reward, false)}
              onRedeem={() => setRedeeming(reward)}
            />
          ))}
        </div>
      )}
      {inactiveRewards.length > 0 && (
        <section className="inactive-rewards">
          <div className="section-caption">
            <span>已停用</span>
            <strong>{inactiveRewards.length}</strong>
          </div>
          {inactiveRewards.map((reward) => (
            <div className="inactive-reward" key={reward.id}>
              <div>
                <strong>{reward.name}</strong>
                <span>{formatBalance(reward.costSeconds)}</span>
              </div>
              <button
                className="icon-button"
                type="button"
                aria-label={`重新启用${reward.name}`}
                title="重新启用"
                disabled={submitting}
                aria-busy={submitting}
                onClick={() => void updateRewardActive(reward, true)}
              >
                <Power aria-hidden="true" size={16} />
              </button>
            </div>
          ))}
        </section>
      )}
      {catalog.recentRedemptions.length > 0 && (
        <section className="redemption-history">
          <div className="section-caption">
            <span>最近兑换</span>
            <strong>{catalog.recentRedemptions.length}</strong>
          </div>
          {catalog.recentRedemptions.map((item) => (
            <div className="redemption-row" key={item.id}>
              <div>
                <strong>{item.rewardName}</strong>
                <span>{formatDateTime(item.redeemedAtUtc)}</span>
              </div>
              <b>- {formatBalance(item.costSeconds)}</b>
            </div>
          ))}
        </section>
      )}
      {showEditor && (
        <RewardEditor
          reward={editing}
          submitting={submitting}
          onCancel={() => setShowEditor(false)}
          onSubmit={async (input) => {
            const wasEditing = Boolean(editing)
            const ok = editing
              ? await update(editing.id, input)
              : await create(input)
            if (ok) {
              setShowEditor(false)
              showFeedback({
                tone: 'success',
                title: wasEditing ? '奖励已更新' : '奖励已创建',
                detail: input.name,
              })
            }
          }}
        />
      )}
      {redeeming && (
        <RedeemDialog
          reward={redeeming}
          balanceSeconds={catalog.balanceSeconds}
          submitting={submitting}
          onCancel={() => setRedeeming(null)}
          onConfirm={async () => {
            const ok = await redeem(redeeming.id)
            if (ok) setRedeeming(null)
          }}
        />
      )}
      {receipt && (
        <RewardReceipt
          redemption={receipt.redemption}
          balanceSeconds={receipt.catalog.balanceSeconds}
          onClose={dismissReceipt}
        />
      )}
    </>
  )
}

function RewardItem({
  reward,
  balanceSeconds,
  disabled,
  onEdit,
  onToggle,
  onRedeem,
}: {
  reward: Reward
  balanceSeconds: number
  disabled: boolean
  onEdit(): void
  onToggle(): void
  onRedeem(): void
}) {
  const canRedeem = balanceSeconds >= reward.costSeconds
  const tier = getRewardTier(reward.costSeconds)
  const TierIcon = tier.icon
  return (
    <article className="reward-item" data-tier={tier.key}>
      <div className="reward-item-heading">
        <div className="reward-glyph">
          <TierIcon aria-hidden="true" size={21} />
        </div>
        <div>
          <span className="reward-tier">{tier.label}</span>
          <h2>{reward.name}</h2>
          <p>{reward.description || '给自己的一份学习奖励'}</p>
        </div>
      </div>
      <div className="reward-item-footer">
        <strong>{formatBalance(reward.costSeconds)}</strong>
        <div>
          <button
            className="icon-button"
            type="button"
            aria-label={`编辑${reward.name}`}
            title="编辑"
            disabled={disabled}
            onClick={onEdit}
          >
            <Pencil aria-hidden="true" size={15} />
          </button>
          <button
            className="icon-button"
            type="button"
            aria-label={`停用${reward.name}`}
            title="停用"
            disabled={disabled}
            aria-busy={disabled}
            onClick={onToggle}
          >
            <Power aria-hidden="true" size={15} />
          </button>
          <button
            className="primary-action reward-redeem"
            type="button"
            disabled={disabled || !canRedeem}
            aria-busy={disabled}
            title={canRedeem ? '兑换奖励' : '可用时间不足'}
            onClick={onRedeem}
          >
            {canRedeem ? '兑换' : '时间不足'}
          </button>
        </div>
      </div>
    </article>
  )
}

function RewardEditor({
  reward,
  submitting,
  onCancel,
  onSubmit,
}: {
  reward: Reward | null
  submitting: boolean
  onCancel(): void
  onSubmit(input: {
    name: string
    description: string
    costSeconds: number
  }): Promise<void>
}) {
  const [name, setName] = useState(reward?.name ?? '')
  const [description, setDescription] = useState(reward?.description ?? '')
  const [minutes, setMinutes] = useState(
    reward ? Math.max(1, Math.round(reward.costSeconds / 60)) : 30,
  )
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!name.trim() || minutes < 1) return
    await onSubmit({
      name: name.trim(),
      description: description.trim(),
      costSeconds: Math.round(minutes * 60),
    })
  }
  return (
    <div className="modal-backdrop" role="presentation">
      <section
        className="dialog reward-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="reward-editor-title"
      >
        <div className="dialog-heading">
          <div>
            <span>{reward ? '编辑奖励' : '新建奖励'}</span>
            <h2 id="reward-editor-title">给这段学习一个期待</h2>
          </div>
          <button
            className="icon-button"
            type="button"
            aria-label="关闭奖励编辑"
            onClick={onCancel}
          >
            <X aria-hidden="true" size={18} />
          </button>
        </div>
        <form onSubmit={submit}>
          <label className="field-label" htmlFor="reward-name">
            奖励名称 <span>必填</span>
          </label>
          <input
            id="reward-name"
            value={name}
            maxLength={80}
            placeholder="例如：一杯奶茶"
            onChange={(event) => setName(event.target.value)}
          />
          <label className="field-label" htmlFor="reward-description">
            给自己的备注
          </label>
          <textarea
            id="reward-description"
            value={description}
            rows={2}
            maxLength={200}
            placeholder="例如：完成一轮高数后享用"
            onChange={(event) => setDescription(event.target.value)}
          />
          <label className="field-label" htmlFor="reward-cost">
            消耗时间 <span>分钟</span>
          </label>
          <input
            id="reward-cost"
            type="number"
            min={1}
            max={100000}
            step={1}
            value={minutes}
            onChange={(event) => setMinutes(Number(event.target.value))}
          />
          <div className="dialog-actions">
            <button
              className="secondary-action"
              type="button"
              onClick={onCancel}
            >
              取消
            </button>
            <button
              className="primary-action"
              type="submit"
              disabled={submitting || !name.trim() || minutes < 1}
              aria-busy={submitting}
            >
              {submitting ? (
                <LoaderCircle aria-hidden="true" className="spin" size={18} />
              ) : (
                <Check aria-hidden="true" size={18} />
              )}
              保存奖励
            </button>
          </div>
        </form>
      </section>
    </div>
  )
}

function RedeemDialog({
  reward,
  balanceSeconds,
  submitting,
  onCancel,
  onConfirm,
}: {
  reward: Reward
  balanceSeconds: number
  submitting: boolean
  onCancel(): void
  onConfirm(): Promise<void>
}) {
  return (
    <div className="modal-backdrop" role="presentation">
      <section
        className="dialog receipt-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="redeem-title"
      >
        <div className="receipt-mark">
          <Gift aria-hidden="true" size={24} />
        </div>
        <span className="receipt-kicker">确认兑换</span>
        <h2 id="redeem-title">{reward.name}</h2>
        <p>这会消耗 {formatBalance(reward.costSeconds)}，兑换后不可撤销。</p>
        <div className="receipt-balance">
          <span>兑换后可用时间</span>
          <strong>{formatBalance(balanceSeconds - reward.costSeconds)}</strong>
        </div>
        <div className="dialog-actions">
          <button className="secondary-action" type="button" onClick={onCancel}>
            再想想
          </button>
          <button
            className="primary-action"
            type="button"
            disabled={submitting}
            aria-busy={submitting}
            onClick={() => void onConfirm()}
          >
            {submitting ? (
              <LoaderCircle aria-hidden="true" className="spin" size={18} />
            ) : (
              <WalletCards aria-hidden="true" size={18} />
            )}
            确认兑换
          </button>
        </div>
      </section>
    </div>
  )
}

function RewardReceipt({
  redemption,
  balanceSeconds,
  onClose,
}: {
  redemption: { rewardName: string; costSeconds: number }
  balanceSeconds: number
  onClose(): void
}) {
  return (
    <div className="modal-backdrop" role="presentation">
      <section
        className="dialog receipt-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="reward-receipt-title"
      >
        <div className="receipt-mark">
          <ReceiptText aria-hidden="true" size={24} />
        </div>
        <span className="receipt-kicker">奖励已兑换</span>
        <h2 id="reward-receipt-title">{redemption.rewardName}</h2>
        <p>已从时间账户扣除 {formatBalance(redemption.costSeconds)}。</p>
        <div className="receipt-balance">
          <span>账户可用时间</span>
          <strong>{formatBalance(balanceSeconds)}</strong>
        </div>
        <button className="primary-action" type="button" onClick={onClose}>
          <Check aria-hidden="true" size={18} />
          收好凭证
        </button>
      </section>
    </div>
  )
}

function Toggle({
  checked,
  disabled,
  label,
  onChange,
}: {
  checked: boolean
  disabled?: boolean
  label: string
  onChange(checked: boolean): void
}) {
  return (
    <label className="toggle" data-disabled={disabled}>
      <input
        type="checkbox"
        checked={checked}
        disabled={disabled}
        aria-label={label}
        onChange={(event) => onChange(event.target.checked)}
      />
      <span />
    </label>
  )
}

function LoadingPanel({ label }: { label: string }) {
  return (
    <section className="empty-view">
      <LoaderCircle aria-hidden="true" className="spin" size={25} />
      <p>{label}</p>
    </section>
  )
}

function InlineError({
  message,
  onClose,
}: {
  message: string
  onClose(): void
}) {
  return (
    <div className="error-banner" role="alert">
      <AlertCircle aria-hidden="true" size={18} />
      <span>{message}</span>
      <button type="button" aria-label="关闭错误提示" onClick={onClose}>
        <X aria-hidden="true" size={17} />
      </button>
    </div>
  )
}

function FeedbackViewport({
  notices,
  onDismiss,
}: {
  notices: FeedbackNotice[]
  onDismiss(id: string): void
}) {
  return (
    <div className="feedback-viewport" aria-label="操作通知">
      {notices.map((notice) => (
        <FeedbackToast key={notice.id} notice={notice} onDismiss={onDismiss} />
      ))}
    </div>
  )
}

function FeedbackToast({
  notice,
  onDismiss,
}: {
  notice: FeedbackNotice
  onDismiss(id: string): void
}) {
  useEffect(() => {
    const timer = window.setTimeout(
      () => onDismiss(notice.id),
      notice.action ? 7_000 : 4_200,
    )
    return () => window.clearTimeout(timer)
  }, [notice.action, notice.id, onDismiss])

  return (
    <div
      className="feedback-toast"
      data-tone={notice.tone}
      data-action={Boolean(notice.action)}
      role="status"
      aria-atomic="true"
    >
      <span className="feedback-icon" aria-hidden="true">
        {notice.tone === 'success' ? (
          <Check size={17} strokeWidth={2.4} />
        ) : (
          <Sparkles size={17} />
        )}
      </span>
      <div>
        <strong>{notice.title}</strong>
        {notice.detail && <span>{notice.detail}</span>}
        {notice.action && (
          <button
            className="feedback-action"
            type="button"
            onClick={() => {
              onDismiss(notice.id)
              notice.action?.onSelect()
            }}
          >
            {notice.action.label}
          </button>
        )}
      </div>
      <button
        className="feedback-close"
        type="button"
        aria-label={`关闭${notice.title}提示`}
        onClick={() => onDismiss(notice.id)}
      >
        <X aria-hidden="true" size={15} />
      </button>
    </div>
  )
}

function WeeklyPlannerView() {
  const { showFeedback } = useFeedback()
  const {
    week,
    weekLoading,
    saving,
    error,
    loadWeek,
    move,
    defer,
    skip,
    clearError,
  } = usePlannerStore()
  const session = useSessionStore((store) => store.state?.session ?? null)
  const startSession = useSessionStore((store) => store.start)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editDate, setEditDate] = useState('')
  const [editTime, setEditTime] = useState('')
  const [editMinutes, setEditMinutes] = useState('')
  const [draggingBlockId, setDraggingBlockId] = useState<string | null>(null)
  const [dragTargetDate, setDragTargetDate] = useState<string | null>(null)

  const weekBlocks = useMemo(
    () => week?.days.flatMap((day) => day.blocks) ?? [],
    [week],
  )
  const draggingBlock = weekBlocks.find((block) => block.id === draggingBlockId)

  useEffect(() => {
    void loadWeek()
  }, [loadWeek])

  const getDropState = (targetDate: string) => {
    if (!draggingBlock) return null
    if (formatDateInput(new Date(draggingBlock.startsAtUtc)) === targetDate) {
      return 'same' as const
    }

    const targetStart = moveTimeBlockToLocalDate(draggingBlock, targetDate)
    return weekBlocks.some(
      (block) =>
        block.id !== draggingBlock.id &&
        block.status !== 'skipped' &&
        timeRangesOverlap(
          targetStart.getTime(),
          targetStart.getTime() + draggingBlock.plannedSeconds * 1000,
          new Date(block.startsAtUtc).getTime(),
          new Date(block.startsAtUtc).getTime() + block.plannedSeconds * 1000,
        ),
    )
      ? ('conflict' as const)
      : ('valid' as const)
  }

  const showMoveFeedback = (
    block: PlanningTimeBlock,
    movedStartsAtUtc: string,
    movedMinutes: number,
    title: string,
    detail: string,
  ) => {
    const originalStartsAtUtc = block.startsAtUtc
    const originalMinutes = block.plannedSeconds / 60
    showFeedback({
      tone: 'success',
      title,
      detail,
      action: {
        label: '撤销',
        onSelect: () => {
          void (async () => {
            const latestBlock = usePlannerStore
              .getState()
              .week?.days.flatMap((day) => day.blocks)
              .find((item) => item.id === block.id)
            const latestMatchesMove =
              latestBlock &&
              new Date(latestBlock.startsAtUtc).getTime() ===
                new Date(movedStartsAtUtc).getTime() &&
              latestBlock.plannedSeconds === movedMinutes * 60
            if (!latestMatchesMove) {
              showFeedback({
                tone: 'info',
                title: '这次移动已无法撤销',
                detail: '时间块之后又发生了变化，请使用调整功能。',
              })
              return
            }

            const restored = await move(
              block.id,
              originalStartsAtUtc,
              originalMinutes,
            )
            if (restored) {
              showFeedback({
                tone: 'info',
                title: '移动已撤销',
                detail: block.taskTitle,
              })
            }
          })()
        },
      },
    })
  }

  const handleBlockDragStart = (
    event: DragEvent<HTMLElement>,
    block: PlanningTimeBlock,
  ) => {
    clearError()
    event.dataTransfer.effectAllowed = 'move'
    event.dataTransfer.setData('text/plain', block.id)
    setDraggingBlockId(block.id)
    setDragTargetDate(null)
  }

  const handleDayDragOver = (
    event: DragEvent<HTMLElement>,
    targetDate: string,
  ) => {
    if (!draggingBlock) return
    event.preventDefault()
    const dropState = getDropState(targetDate)
    event.dataTransfer.dropEffect = dropState === 'valid' ? 'move' : 'none'
    if (dragTargetDate !== targetDate) setDragTargetDate(targetDate)
  }

  const clearDragState = () => {
    setDraggingBlockId(null)
    setDragTargetDate(null)
  }

  const handleDayDrop = async (
    event: DragEvent<HTMLElement>,
    targetDate: string,
  ) => {
    event.preventDefault()
    if (!draggingBlock) {
      clearDragState()
      return
    }

    const block = draggingBlock
    const dropState = getDropState(targetDate)
    clearDragState()
    if (dropState === 'same') {
      showFeedback({
        tone: 'info',
        title: '时间块仍在原日期',
        detail: block.taskTitle,
      })
      return
    }
    if (dropState === 'conflict') {
      showFeedback({
        tone: 'info',
        title: '该时段已有安排',
        detail: '原计划没有改变，可以先调整时间再移动。',
      })
      return
    }
    if (dropState !== 'valid') return

    const targetStart = moveTimeBlockToLocalDate(block, targetDate)
    const movedMinutes = block.plannedSeconds / 60
    const moved = await move(block.id, targetStart.toISOString(), movedMinutes)
    if (moved) {
      showMoveFeedback(
        block,
        targetStart.toISOString(),
        movedMinutes,
        '时间块已移动',
        `${formatWeekday(targetDate)} ${formatMonthDay(targetDate)} · 保留 ${formatTime(targetStart.toISOString())}`,
      )
    } else {
      showFeedback({
        tone: 'info',
        title: '时间块未移动',
        detail: usePlannerStore.getState().error ?? '请检查目标日期和时间。',
      })
    }
  }

  const beginEditing = (block: PlanningTimeBlock) => {
    const startsAt = new Date(block.startsAtUtc)
    setEditingId(block.id)
    setEditDate(formatDateInput(startsAt))
    setEditTime(
      `${String(startsAt.getHours()).padStart(2, '0')}:${String(
        startsAt.getMinutes(),
      ).padStart(2, '0')}`,
    )
    setEditMinutes(String(block.plannedSeconds / 60))
  }

  const submitMove = async (event: FormEvent, block: PlanningTimeBlock) => {
    event.preventDefault()
    const minutes = Number(editMinutes)
    const startsAt = new Date(`${editDate}T${editTime}:00`)
    if (
      !editDate ||
      !editTime ||
      !Number.isFinite(minutes) ||
      minutes <= 0 ||
      Number.isNaN(startsAt.getTime())
    ) {
      return
    }

    const movedMinutes = Math.floor(minutes)
    const movedStartsAtUtc = startsAt.toISOString()
    const ok = await move(block.id, movedStartsAtUtc, movedMinutes)
    if (ok) {
      setEditingId(null)
      showMoveFeedback(
        block,
        movedStartsAtUtc,
        movedMinutes,
        '时间块已调整',
        block.taskTitle,
      )
    }
  }

  if (weekLoading && !week) {
    return <LoadingPanel label="正在整理本周计划" />
  }

  if (!week) {
    return (
      <>
        {error && <InlineError message={error} onClose={clearError} />}
        <section className="empty-view">
          <CalendarRange aria-hidden="true" size={28} />
          <h2>本周计划暂时不可用</h2>
          <p>先在今日专注页创建任务并安排时间块。</p>
        </section>
      </>
    )
  }

  return (
    <>
      {error && <InlineError message={error} onClose={clearError} />}
      <section className="week-overview" aria-labelledby="week-plan-title">
        <div className="week-overview-heading">
          <div>
            <span>{formatWeekRange(week)}</span>
            <h2 id="week-plan-title">本周执行地图</h2>
          </div>
          <button
            className="icon-button"
            type="button"
            aria-label="刷新本周计划"
            disabled={weekLoading}
            onClick={() => void loadWeek()}
          >
            <RefreshCw
              aria-hidden="true"
              className={weekLoading ? 'spin' : undefined}
              size={16}
            />
          </button>
        </div>
        <div className="week-summary">
          <div>
            <span>计划投入</span>
            <strong>{formatBalance(week.plannedSeconds)}</strong>
          </div>
          <div>
            <span>实际投入</span>
            <strong>{formatBalance(week.actualSeconds)}</strong>
          </div>
          <div>
            <span>已完成</span>
            <strong>{week.completedCount} 个</strong>
          </div>
          <div>
            <span>已跳过</span>
            <strong>{week.skippedCount} 个</strong>
          </div>
        </div>
        <p className="week-interaction-tip">
          <GripVertical aria-hidden="true" size={14} />
          拖动未执行的时间块到另一天；开始时间和计划时长会保留。
        </p>
      </section>

      <div className="week-board">
        {week.days.map((day) => {
          const dropState =
            dragTargetDate === day.date ? getDropState(day.date) : null
          return (
            <section
              className="week-day-card"
              aria-label={`${formatWeekday(day.date)} ${formatMonthDay(day.date)}计划`}
              data-drop-state={dropState ?? undefined}
              data-today={day.date === formatDateInput(new Date())}
              key={day.date}
              onDragEnter={(event) => handleDayDragOver(event, day.date)}
              onDragOver={(event) => handleDayDragOver(event, day.date)}
              onDragLeave={(event) => {
                const relatedTarget = event.relatedTarget
                if (
                  !(relatedTarget instanceof Node) ||
                  !event.currentTarget.contains(relatedTarget)
                ) {
                  setDragTargetDate((current) =>
                    current === day.date ? null : current,
                  )
                }
              }}
              onDrop={(event) => void handleDayDrop(event, day.date)}
            >
              <div className="week-day-heading">
                <div>
                  <span>{formatWeekday(day.date)}</span>
                  <strong>{formatMonthDay(day.date)}</strong>
                </div>
                <span>{formatCompactDuration(day.plannedSeconds)}</span>
              </div>
              {dropState && (
                <p className="week-drop-hint" data-state={dropState}>
                  {dropState === 'valid'
                    ? `放到${formatWeekday(day.date)}`
                    : dropState === 'conflict'
                      ? '该时段已有安排'
                      : '当前所在日期'}
                </p>
              )}
              {day.blocks.length === 0 ? (
                <p className="week-day-empty">暂无安排</p>
              ) : (
                <div className="week-block-list">
                  {day.blocks.map((block) => {
                    const blockIsActive = session?.timeBlockId === block.id
                    const canDrag =
                      block.status === 'planned' &&
                      !blockIsActive &&
                      !saving &&
                      editingId !== block.id
                    return (
                      <article
                        className="week-block"
                        aria-label={`${block.taskTitle}，${formatTime(block.startsAtUtc)}，${blockIsActive ? '执行中' : timeBlockStatusLabel(block.status)}`}
                        data-dragging={draggingBlockId === block.id}
                        data-status={block.status}
                        draggable={canDrag}
                        key={block.id}
                        title={canDrag ? '拖动到其他日期' : undefined}
                        onDragEnd={clearDragState}
                        onDragStart={(event) =>
                          handleBlockDragStart(event, block)
                        }
                      >
                        <div className="week-block-heading">
                          <time>{formatTime(block.startsAtUtc)}</time>
                          <div className="week-block-state">
                            {canDrag && (
                              <GripVertical
                                aria-hidden="true"
                                className="week-block-grip"
                                size={12}
                              />
                            )}
                            <span>
                              {blockIsActive
                                ? '执行中'
                                : timeBlockStatusLabel(block.status)}
                            </span>
                          </div>
                        </div>
                        <strong>{block.taskTitle}</strong>
                        <p>
                          计划 {formatCompactDuration(block.plannedSeconds)}
                          {block.actualSeconds > 0
                            ? ` · 实际 ${formatCompactDuration(block.actualSeconds)}`
                            : ''}
                        </p>
                        {block.status === 'planned' && (
                          <div className="week-block-actions">
                            <button
                              type="button"
                              disabled={Boolean(session) || saving}
                              onClick={async () => {
                                const ok = await startSession(
                                  block.taskTitle,
                                  false,
                                  block.taskId,
                                  block.id,
                                )
                                if (ok) {
                                  showFeedback({
                                    tone: 'success',
                                    title: '计划时间块已开始',
                                    detail: block.taskTitle,
                                  })
                                }
                              }}
                            >
                              <Play aria-hidden="true" size={13} />
                              开始
                            </button>
                            <button
                              type="button"
                              disabled={saving || blockIsActive}
                              onClick={() =>
                                editingId === block.id
                                  ? setEditingId(null)
                                  : beginEditing(block)
                              }
                            >
                              <Pencil aria-hidden="true" size={13} />
                              调整
                            </button>
                            <button
                              type="button"
                              disabled={Boolean(session) || saving}
                              onClick={async () => {
                                const ok = await defer(block.id)
                                if (ok) {
                                  showFeedback({
                                    tone: 'info',
                                    title: '已延期到明天',
                                    detail: block.taskTitle,
                                  })
                                }
                              }}
                            >
                              明天
                            </button>
                            <button
                              type="button"
                              disabled={Boolean(session) || saving}
                              onClick={() => void skip(block.id)}
                            >
                              跳过
                            </button>
                          </div>
                        )}
                        {editingId === block.id && (
                          <form
                            className="week-block-edit"
                            onSubmit={(event) => void submitMove(event, block)}
                          >
                            <input
                              aria-label={`调整${block.taskTitle}日期`}
                              type="date"
                              value={editDate}
                              onChange={(event) =>
                                setEditDate(event.target.value)
                              }
                            />
                            <input
                              aria-label={`调整${block.taskTitle}时间`}
                              type="time"
                              value={editTime}
                              onChange={(event) =>
                                setEditTime(event.target.value)
                              }
                            />
                            <input
                              aria-label={`调整${block.taskTitle}分钟`}
                              inputMode="numeric"
                              value={editMinutes}
                              onChange={(event) =>
                                setEditMinutes(event.target.value)
                              }
                            />
                            <button type="submit" disabled={saving}>
                              保存调整
                            </button>
                          </form>
                        )}
                      </article>
                    )
                  })}
                </div>
              )}
            </section>
          )
        })}
      </div>
    </>
  )
}

function FocusView() {
  const { showFeedback } = useFeedback()
  const {
    state,
    receipt,
    loading,
    submitting,
    error,
    start,
    pause,
    resume,
    finish,
    clearError,
    dismissReceipt,
  } = useSessionStore()
  const {
    tasks,
    loading: tasksLoading,
    saving: tasksSaving,
    error: tasksError,
    load: loadTasks,
    create: createTask,
    setCompleted,
    clearError: clearTaskError,
  } = useTaskStore()
  const {
    overview: todayPlan,
    loading: plannerLoading,
    saving: plannerSaving,
    error: plannerError,
    load: loadPlanner,
    create: createTimeBlock,
    skip: skipTimeBlock,
    clearError: clearPlannerError,
  } = usePlannerStore()
  const trackingOverview = useTimelineStore((store) => store.overview)
  const loadTracking = useTimelineStore((store) => store.load)
  const relevance = useRelevanceStore((store) => store.snapshot)
  const relevanceLoading = useRelevanceStore((store) => store.loading)
  const loadRelevance = useRelevanceStore((store) => store.load)
  const clearRelevance = useRelevanceStore((store) => store.clear)
  const [goal, setGoal] = useState('')
  const [captureScreenshots, setCaptureScreenshots] = useState(false)
  const [showFinish, setShowFinish] = useState(false)
  const session = state?.session ?? null
  const focusStatus = session?.status ?? 'idle'
  const focusCharacterAsset = getFocusCharacterAsset(focusStatus)
  const elapsedSeconds = useElapsedSeconds(session, state?.asOfUtc)

  useEffect(() => {
    if (!trackingOverview) void loadTracking()
  }, [loadTracking, trackingOverview])

  useEffect(() => {
    if (session?.status !== 'running') {
      clearRelevance()
      return
    }

    void loadRelevance()
    const interval = window.setInterval(() => void loadRelevance(), 5000)
    return () => window.clearInterval(interval)
  }, [clearRelevance, loadRelevance, session?.status])

  useEffect(() => {
    void loadTasks()
  }, [loadTasks])

  useEffect(() => {
    void loadPlanner()
  }, [loadPlanner])

  useEffect(() => {
    if (receipt) {
      void loadTasks()
      void loadPlanner()
    }
  }, [loadPlanner, loadTasks, receipt])

  const handleStart = async (event: FormEvent) => {
    event.preventDefault()
    if (!goal.trim()) return
    const sessionGoal = goal.trim()
    const ok = await start(sessionGoal, captureScreenshots)
    if (ok) {
      showFeedback({
        tone: 'success',
        title: '学习计时已开始',
        detail: sessionGoal,
      })
    }
  }

  const toggleSession = async () => {
    if (!session) return
    const isPausing = session.status === 'running'
    const ok = isPausing ? await pause() : await resume()
    if (ok) {
      showFeedback({
        tone: 'info',
        title: isPausing ? '计时已暂停' : '已继续积累时间',
        detail: isPausing ? '休息结束后可以继续这笔时间存款。' : session.goal,
      })
    }
  }

  return (
    <>
      {error && (
        <div className="error-banner" role="alert">
          <AlertCircle aria-hidden="true" size={18} />
          <span>{error}</span>
          <button type="button" aria-label="关闭错误提示" onClick={clearError}>
            <X aria-hidden="true" size={17} />
          </button>
        </div>
      )}

      <TodayTasksPanel
        tasks={tasks}
        loading={tasksLoading}
        saving={tasksSaving}
        error={tasksError}
        plan={todayPlan}
        plannerLoading={plannerLoading}
        plannerSaving={plannerSaving}
        plannerError={plannerError}
        hasActiveSession={Boolean(session)}
        activeTaskId={session?.taskId ?? null}
        onCreate={async (title, estimatedMinutes, dueDate) => {
          await createTask(title, estimatedMinutes, dueDate)
        }}
        onStart={async (task) => {
          if (session) return
          const ok = await start(task.title, captureScreenshots, task.id)
          if (ok) {
            showFeedback({
              tone: 'success',
              title: '\u4ece\u4efb\u52a1\u5f00\u59cb\u4e13\u6ce8',
              detail: task.title,
            })
          }
        }}
        onComplete={(task) => void setCompleted(task.id, true)}
        onSchedule={async (task, startsAtUtc, plannedMinutes) => {
          const block = await createTimeBlock(
            task.id,
            startsAtUtc,
            plannedMinutes,
          )
          if (block) {
            showFeedback({
              tone: 'success',
              title: '时间块已安排',
              detail: `${task.title} · ${plannedMinutes} 分钟`,
            })
          }
        }}
        onStartBlock={async (block) => {
          if (session) return
          const ok = await start(
            block.taskTitle,
            captureScreenshots,
            block.taskId,
            block.id,
          )
          if (ok) {
            showFeedback({
              tone: 'success',
              title: '计划时间块已开始',
              detail: block.taskTitle,
            })
          }
        }}
        onSkipBlock={(block) => void skipTimeBlock(block.id)}
        onClearError={clearTaskError}
        onClearPlannerError={clearPlannerError}
      />

      <div className="focus-grid">
        <section
          className="focus-panel"
          data-status={focusStatus}
          aria-labelledby="focus-title"
        >
          <div className="panel-heading">
            <div>
              <span
                className="status-label"
                data-status={session?.status ?? 'idle'}
              >
                <span className="status-dot" />
                {session?.status === 'running'
                  ? '正在积累'
                  : session?.status === 'paused'
                    ? '已暂停'
                    : '等待存入'}
              </span>
              <h2 id="focus-title">
                {session?.goal ?? '今天的下一笔时间存款'}
              </h2>
            </div>
            <span className="session-count">
              {session ? '本次专注中' : '准备好再开始'}
            </span>
          </div>

          <div className="timer-stage">
            <div className="timer-value" aria-label="当前计时">
              {formatClock(elapsedSeconds)}
            </div>

            {loading ? (
              <div className="loading-state">
                <LoaderCircle aria-hidden="true" className="spin" size={18} />
                正在读取本地记录
              </div>
            ) : session ? (
              <>
                <p>
                  {session.status === 'paused'
                    ? '计时已经暂停，休息结束后继续这笔时间存款。'
                    : '保持当下的节奏，结束时记下实际完成的内容。'}
                </p>
                <div className="session-actions">
                  <button
                    className="secondary-action"
                    type="button"
                    disabled={submitting}
                    aria-busy={submitting}
                    onClick={() => void toggleSession()}
                  >
                    {submitting ? (
                      <LoaderCircle
                        aria-hidden="true"
                        className="spin"
                        size={17}
                      />
                    ) : session.status === 'running' ? (
                      <Pause aria-hidden="true" size={17} />
                    ) : (
                      <Play aria-hidden="true" size={17} />
                    )}
                    {session.status === 'running' ? '暂停' : '继续'}
                  </button>
                  <button
                    className="primary-action finish-action"
                    type="button"
                    disabled={submitting || elapsedSeconds < 1}
                    onClick={() => setShowFinish(true)}
                  >
                    <Square aria-hidden="true" size={16} />
                    完成并存入
                  </button>
                </div>
                {session.status === 'running' && (
                  <TaskRelevanceCard
                    loading={relevanceLoading}
                    snapshot={relevance}
                  />
                )}
              </>
            ) : (
              <form className="start-form" onSubmit={handleStart}>
                <label htmlFor="session-goal">这段时间准备推进什么？</label>
                <div className="goal-input-row">
                  <input
                    id="session-goal"
                    value={goal}
                    maxLength={120}
                    autoComplete="off"
                    placeholder="例如：完成高数极限章节例题"
                    onChange={(event) => setGoal(event.target.value)}
                  />
                  <button
                    className="primary-action"
                    type="submit"
                    disabled={submitting || !goal.trim()}
                    aria-busy={submitting}
                  >
                    {submitting ? (
                      <LoaderCircle
                        aria-hidden="true"
                        className="spin"
                        size={18}
                      />
                    ) : (
                      <Play aria-hidden="true" size={18} />
                    )}
                    开始专注
                  </button>
                </div>
                <div className="capture-disclosure">
                  <MonitorDot aria-hidden="true" size={15} />
                  <span>记录前台应用</span>
                  <strong>
                    {!trackingOverview
                      ? '读取中'
                      : trackingOverview.settings.captureActivity
                        ? '开启'
                        : '关闭'}
                  </strong>
                  <Camera aria-hidden="true" size={15} />
                  <span>
                    {!trackingOverview
                      ? '截图设置读取中'
                      : trackingOverview.settings.captureScreenshots
                        ? `截图每 ${trackingOverview.settings.screenshotIntervalMinutes} 分钟`
                        : '截图全局关闭'}
                  </span>
                  <label className="session-capture-choice">
                    <input
                      type="checkbox"
                      checked={captureScreenshots}
                      disabled={!trackingOverview?.settings.captureScreenshots}
                      onChange={(event) =>
                        setCaptureScreenshots(event.target.checked)
                      }
                    />
                    <span>允许本次截图</span>
                  </label>
                </div>
              </form>
            )}
          </div>

          <div className="day-track" aria-label="本次学习进度">
            <div className="track-line">
              <span
                className="track-progress"
                style={{
                  width: `${Math.min(100, (elapsedSeconds / 3600) * 100)}%`,
                }}
              />
              <span
                className="track-origin"
                style={{
                  left: `${Math.min(100, (elapsedSeconds / 3600) * 100)}%`,
                }}
              />
            </div>
            <div className="track-labels">
              <span>开始</span>
              <span>15 分钟</span>
              <span>30 分钟</span>
              <span>60 分钟</span>
            </div>
          </div>
          <div
            className="focus-character"
            data-status={focusStatus}
            data-asset={focusCharacterAsset.key}
            aria-hidden="true"
          >
            <img src={focusCharacterAsset.src} alt="" draggable="false" />
          </div>
        </section>

        <aside className="summary-panel" aria-labelledby="summary-title">
          <div className="summary-heading">
            <h2 id="summary-title">时间账户</h2>
            <WalletCards aria-hidden="true" size={20} />
          </div>
          <dl className="metric-list">
            <div>
              <dt>当前时间块</dt>
              <dd>{formatCompactDuration(elapsedSeconds)}</dd>
            </div>
            <div>
              <dt>计时状态</dt>
              <dd>
                {session?.status === 'running'
                  ? '积累中'
                  : session?.status === 'paused'
                    ? '已暂停'
                    : '未开始'}
              </dd>
            </div>
            <div>
              <dt>换算规则</dt>
              <dd>1 : 1</dd>
            </div>
          </dl>
          <div className="balance-row">
            <div className="balance-icon">
              <Clock3 aria-hidden="true" size={20} />
            </div>
            <div>
              <span>可用时间</span>
              <strong>{formatBalance(state?.balanceSeconds ?? 0)}</strong>
            </div>
          </div>
          <p className="privacy-note">学习记录与余额仅保存在本机。</p>
        </aside>
      </div>

      {showFinish && session && (
        <FinishDialog
          goal={session.goal}
          durationSeconds={elapsedSeconds}
          submitting={submitting}
          onCancel={() => setShowFinish(false)}
          onSubmit={async (note, tasks) => {
            const succeeded = await finish(note, tasks)
            if (succeeded) setShowFinish(false)
          }}
        />
      )}

      {receipt && (
        <ReceiptDialog
          durationSeconds={receipt.completedSession.effectiveDurationSeconds}
          balanceSeconds={receipt.state.balanceSeconds}
          goal={receipt.completedSession.goal}
          onClose={dismissReceipt}
        />
      )}
    </>
  )
}

function TaskRelevanceCard({
  snapshot,
  loading,
}: {
  snapshot: TaskRelevanceSnapshot | null
  loading: boolean
}) {
  if (loading && !snapshot) {
    return (
      <div className="task-relevance-card" data-level="loading">
        <LoaderCircle aria-hidden="true" className="spin" size={16} />
        <span>正在读取当前窗口证据</span>
      </div>
    )
  }

  if (!snapshot) {
    return (
      <div className="task-relevance-card" data-level="unknown">
        <div className="task-relevance-heading">
          <div className="task-relevance-label">
            <MonitorDot aria-hidden="true" size={16} />
            <span>当前任务关联度</span>
          </div>
          <strong>等待证据</strong>
        </div>
        <p>尚未采集到最近的工作窗口，切换到学习软件后会自动更新。</p>
      </div>
    )
  }

  return (
    <div className="task-relevance-card" data-level={snapshot.level}>
      <div className="task-relevance-heading">
        <div className="task-relevance-label">
          <MonitorDot aria-hidden="true" size={16} />
          <span>当前任务关联度</span>
        </div>
        <strong>{taskRelevanceLevelLabel(snapshot.level)}</strong>
      </div>
      {snapshot.available ? (
        <>
          <div className="task-relevance-window">
            <strong>{snapshot.processName}</strong>
            <span title={snapshot.windowTitle}>{snapshot.windowTitle}</span>
          </div>
          <p>{snapshot.reasons[0]}</p>
        </>
      ) : (
        <p>{snapshot.reasons[0]}</p>
      )}
      <span className="task-relevance-disclaimer">
        这是本地窗口证据，不代表对学习行为的绝对判断。
      </span>
    </div>
  )
}

function TodayTasksPanel({
  tasks,
  loading,
  saving,
  error,
  plan,
  plannerLoading,
  plannerSaving,
  plannerError,
  hasActiveSession,
  activeTaskId,
  onCreate,
  onStart,
  onComplete,
  onSchedule,
  onStartBlock,
  onSkipBlock,
  onClearError,
  onClearPlannerError,
}: {
  tasks: PlanningTask[]
  loading: boolean
  saving: boolean
  error: string | null
  plan: TodayPlan | null
  plannerLoading: boolean
  plannerSaving: boolean
  plannerError: string | null
  hasActiveSession: boolean
  activeTaskId: string | null
  onCreate(
    title: string,
    estimatedMinutes?: number,
    dueDate?: string,
  ): Promise<void>
  onStart(task: PlanningTask): Promise<void>
  onComplete(task: PlanningTask): void
  onSchedule(
    task: PlanningTask,
    startsAtUtc: string,
    plannedMinutes: number,
  ): Promise<void>
  onStartBlock(block: PlanningTimeBlock): Promise<void>
  onSkipBlock(block: PlanningTimeBlock): void
  onClearError(): void
  onClearPlannerError(): void
}) {
  const [title, setTitle] = useState('')
  const [estimatedMinutes, setEstimatedMinutes] = useState('')
  const [dueDate, setDueDate] = useState(() => formatDateInput(new Date()))
  const [schedulingTaskId, setSchedulingTaskId] = useState<string | null>(null)
  const [scheduleDate, setScheduleDate] = useState(() =>
    formatDateInput(new Date()),
  )
  const [scheduleTime, setScheduleTime] = useState(defaultScheduleTime)
  const [scheduleMinutes, setScheduleMinutes] = useState('')

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!title.trim()) return
    const minutes = Number(estimatedMinutes)
    await onCreate(
      title.trim(),
      Number.isFinite(minutes) && minutes > 0 ? Math.floor(minutes) : undefined,
      dueDate || undefined,
    )
    setTitle('')
    setEstimatedMinutes('')
  }

  const submitSchedule = async (event: FormEvent, task: PlanningTask) => {
    event.preventDefault()
    if (!scheduleDate || !scheduleTime) return
    const minutes = Number(scheduleMinutes || task.estimatedSeconds! / 60)
    if (!Number.isFinite(minutes) || minutes <= 0) return
    const startsAt = new Date(`${scheduleDate}T${scheduleTime}:00`)
    if (Number.isNaN(startsAt.getTime())) return
    await onSchedule(task, startsAt.toISOString(), Math.floor(minutes))
    setSchedulingTaskId(null)
    setScheduleMinutes('')
  }

  return (
    <section className="today-tasks-panel" aria-labelledby="today-tasks-title">
      {error && <InlineError message={error} onClose={onClearError} />}
      <div className="today-tasks-heading">
        <div>
          <span>今日排程</span>
          <h2 id="today-tasks-title">把任务放进真实时间</h2>
        </div>
        <span className="task-count">{tasks.length} 项待办</span>
      </div>

      {plannerError && (
        <InlineError message={plannerError} onClose={onClearPlannerError} />
      )}
      <div className="plan-summary" aria-label="今日计划摘要">
        <div>
          <span>计划</span>
          <strong>{formatBalance(plan?.plannedSeconds ?? 0)}</strong>
        </div>
        <div>
          <span>实际</span>
          <strong>{formatBalance(plan?.actualSeconds ?? 0)}</strong>
        </div>
        <div>
          <span>完成</span>
          <strong>
            {plan?.completedCount ?? 0} / {plan?.blocks.length ?? 0}
          </strong>
        </div>
      </div>

      <div className="today-plan-section">
        <div className="execution-section-heading">
          <strong>今日时间块</strong>
          <span>{plan?.plannedCount ?? 0} 个等待执行</span>
        </div>
        {plannerLoading ? (
          <div className="task-loading">
            <LoaderCircle aria-hidden="true" className="spin" size={17} />
            正在读取今日计划
          </div>
        ) : !plan || plan.blocks.length === 0 ? (
          <p className="task-empty">还没有时间块，从下方任务中安排一个。</p>
        ) : (
          <div className="time-block-list">
            {plan.blocks.map((block) => (
              <div
                className="time-block-row"
                data-status={block.status}
                key={block.id}
              >
                <time>{formatTime(block.startsAtUtc)}</time>
                <div>
                  <strong>{block.taskTitle}</strong>
                  <span>
                    计划 {formatCompactDuration(block.plannedSeconds)}
                    {block.status === 'completed'
                      ? ` · 实际 ${formatCompactDuration(block.actualSeconds)}`
                      : ` · ${timeBlockStatusLabel(block.status)}`}
                  </span>
                </div>
                {block.status === 'planned' && (
                  <div className="time-block-actions">
                    <button
                      className="secondary-action"
                      type="button"
                      disabled={hasActiveSession || plannerSaving}
                      onClick={() => void onStartBlock(block)}
                    >
                      <Play aria-hidden="true" size={14} />
                      开始
                    </button>
                    <button
                      className="time-block-skip"
                      type="button"
                      disabled={hasActiveSession || plannerSaving}
                      onClick={() => onSkipBlock(block)}
                    >
                      跳过
                    </button>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>

      <div className="execution-section-heading task-section-heading">
        <strong>待办任务</strong>
        <span>可直接专注，也可安排到今天</span>
      </div>

      {loading ? (
        <div className="task-loading">
          <LoaderCircle aria-hidden="true" className="spin" size={17} />
          正在读取本地任务
        </div>
      ) : tasks.length === 0 ? (
        <p className="task-empty">还没有任务，先添加今天最重要的一件事。</p>
      ) : (
        <div className="today-task-list">
          {tasks.map((task) => (
            <div className="today-task-entry" key={task.id}>
              <div className="today-task-row">
                <button
                  className="task-check-button"
                  type="button"
                  aria-label={`完成${task.title}`}
                  disabled={
                    saving ||
                    activeTaskId === task.id ||
                    Boolean(
                      plan?.blocks.some(
                        (block) =>
                          block.taskId === task.id &&
                          block.status === 'planned',
                      ),
                    )
                  }
                  onClick={() => onComplete(task)}
                >
                  <Check aria-hidden="true" size={15} />
                </button>
                <div className="today-task-copy">
                  <strong>{task.title}</strong>
                  <span>
                    {task.estimatedSeconds
                      ? `预计 ${formatCompactDuration(task.estimatedSeconds)}`
                      : '未设置预计时长'}
                    {task.actualSeconds > 0
                      ? ` · 已投入 ${formatCompactDuration(task.actualSeconds)}`
                      : ''}
                    {task.dueDate ? ` · ${formatDueDate(task.dueDate)}` : ''}
                  </span>
                </div>
                <div className="today-task-actions">
                  <button
                    className="secondary-action task-schedule-button"
                    type="button"
                    disabled={plannerSaving}
                    onClick={() =>
                      setSchedulingTaskId((current) =>
                        current === task.id ? null : task.id,
                      )
                    }
                  >
                    <CalendarDays aria-hidden="true" size={14} />
                    安排
                  </button>
                  <button
                    className="secondary-action task-start-button"
                    type="button"
                    disabled={hasActiveSession || saving}
                    onClick={() => void onStart(task)}
                  >
                    <Play aria-hidden="true" size={15} />
                    开始
                  </button>
                </div>
              </div>
              {schedulingTaskId === task.id && (
                <form
                  className="task-schedule-form"
                  onSubmit={(event) => void submitSchedule(event, task)}
                >
                  <label>
                    日期
                    <input
                      aria-label={`安排${task.title}日期`}
                      type="date"
                      value={scheduleDate}
                      onChange={(event) => setScheduleDate(event.target.value)}
                    />
                  </label>
                  <label>
                    开始时间
                    <input
                      aria-label={`安排${task.title}时间`}
                      type="time"
                      value={scheduleTime}
                      onChange={(event) => setScheduleTime(event.target.value)}
                    />
                  </label>
                  <label>
                    计划分钟
                    <input
                      aria-label={`安排${task.title}分钟`}
                      inputMode="numeric"
                      value={scheduleMinutes}
                      placeholder={String(
                        (task.estimatedSeconds ?? 25 * 60) / 60,
                      )}
                      onChange={(event) =>
                        setScheduleMinutes(event.target.value)
                      }
                    />
                  </label>
                  <button
                    className="primary-action"
                    type="submit"
                    disabled={plannerSaving || !scheduleDate || !scheduleTime}
                  >
                    <CalendarDays aria-hidden="true" size={15} />
                    加入计划
                  </button>
                </form>
              )}
            </div>
          ))}
        </div>
      )}

      <form className="quick-task-form" onSubmit={submit}>
        <input
          aria-label="新任务标题"
          value={title}
          maxLength={160}
          placeholder="添加一个任务，例如：整理高数错题"
          onChange={(event) => setTitle(event.target.value)}
        />
        <input
          aria-label="预计分钟"
          value={estimatedMinutes}
          inputMode="numeric"
          placeholder="分钟"
          onChange={(event) => setEstimatedMinutes(event.target.value)}
        />
        <input
          aria-label="任务截止日期"
          type="date"
          value={dueDate}
          onChange={(event) => setDueDate(event.target.value)}
        />
        <button
          className="primary-action"
          type="submit"
          disabled={saving || !title.trim()}
        >
          {saving ? (
            <LoaderCircle aria-hidden="true" className="spin" size={17} />
          ) : (
            <Plus aria-hidden="true" size={17} />
          )}
          添加任务
        </button>
      </form>
    </section>
  )
}

function FinishDialog({
  goal,
  durationSeconds,
  submitting,
  onCancel,
  onSubmit,
}: {
  goal: string
  durationSeconds: number
  submitting: boolean
  onCancel(): void
  onSubmit(note: string, tasks: SessionTaskInput[]): Promise<void>
}) {
  const [note, setNote] = useState('')
  const [tasks, setTasks] = useState<SessionTaskInput[]>([
    { content: '', isCompleted: true },
  ])

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!note.trim()) return
    await onSubmit(
      note.trim(),
      tasks.filter((task) => task.content.trim()),
    )
  }

  return (
    <div className="modal-backdrop" role="presentation">
      <section
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="finish-dialog-title"
      >
        <div className="dialog-heading">
          <div>
            <span>结算时间块</span>
            <h2 id="finish-dialog-title">记录这次真正完成的事</h2>
          </div>
          <button
            className="icon-button"
            type="button"
            aria-label="取消结算"
            onClick={onCancel}
          >
            <X aria-hidden="true" size={18} />
          </button>
        </div>

        <div className="settlement-strip">
          <Clock3 aria-hidden="true" size={20} />
          <div>
            <span>{goal}</span>
            <strong>{formatClock(durationSeconds)}</strong>
          </div>
        </div>

        <form onSubmit={submit}>
          <label className="field-label" htmlFor="completion-note">
            完成内容或未完成原因 <span>必填</span>
          </label>
          <textarea
            id="completion-note"
            value={note}
            rows={3}
            maxLength={500}
            autoFocus
            placeholder="例如：完成 12 道例题，整理了 3 道错题"
            onChange={(event) => setNote(event.target.value)}
          />

          <div className="task-heading">
            <span className="field-label">任务清单</span>
            <button
              type="button"
              onClick={() =>
                setTasks((current) => [
                  ...current,
                  { content: '', isCompleted: true },
                ])
              }
            >
              <Plus aria-hidden="true" size={16} />
              添加
            </button>
          </div>
          <div className="task-list">
            {tasks.map((task, index) => (
              <div className="task-row" key={index}>
                <label className="task-check" title="是否完成">
                  <input
                    type="checkbox"
                    checked={task.isCompleted}
                    onChange={(event) =>
                      setTasks((current) =>
                        current.map((item, itemIndex) =>
                          itemIndex === index
                            ? { ...item, isCompleted: event.target.checked }
                            : item,
                        ),
                      )
                    }
                  />
                  <span>
                    <Check aria-hidden="true" size={13} />
                  </span>
                </label>
                <input
                  aria-label={`任务 ${index + 1}`}
                  value={task.content}
                  maxLength={200}
                  placeholder="可选：拆分一项具体任务"
                  onChange={(event) =>
                    setTasks((current) =>
                      current.map((item, itemIndex) =>
                        itemIndex === index
                          ? { ...item, content: event.target.value }
                          : item,
                      ),
                    )
                  }
                />
                {tasks.length > 1 && (
                  <button
                    className="remove-task"
                    type="button"
                    aria-label={`删除任务 ${index + 1}`}
                    onClick={() =>
                      setTasks((current) =>
                        current.filter((_, itemIndex) => itemIndex !== index),
                      )
                    }
                  >
                    <X aria-hidden="true" size={16} />
                  </button>
                )}
              </div>
            ))}
          </div>

          <div className="dialog-actions">
            <button
              className="secondary-action"
              type="button"
              onClick={onCancel}
            >
              返回计时
            </button>
            <button
              className="primary-action"
              type="submit"
              disabled={submitting || !note.trim()}
              aria-busy={submitting}
            >
              {submitting ? (
                <LoaderCircle aria-hidden="true" className="spin" size={18} />
              ) : (
                <WalletCards aria-hidden="true" size={18} />
              )}
              确认存入
            </button>
          </div>
        </form>
      </section>
    </div>
  )
}

function ReceiptDialog({
  durationSeconds,
  balanceSeconds,
  goal,
  onClose,
}: {
  durationSeconds: number
  balanceSeconds: number
  goal: string
  onClose(): void
}) {
  return (
    <div className="modal-backdrop" role="presentation">
      <section
        className="dialog receipt-dialog deposit-receipt"
        role="dialog"
        aria-modal="true"
        aria-labelledby="receipt-title"
      >
        <img
          className="receipt-illustration"
          src="/assets/illustrations/time-bank-trainee-complete.png"
          alt=""
          aria-hidden="true"
          draggable="false"
        />
        <span className="receipt-kicker">时间存款已到账</span>
        <h2 id="receipt-title">+ {formatBalance(durationSeconds)}</h2>
        <p>{goal}</p>
        <div className="receipt-balance">
          <span>账户可用时间</span>
          <strong>{formatBalance(balanceSeconds)}</strong>
        </div>
        <button className="primary-action" type="button" onClick={onClose}>
          <Check aria-hidden="true" size={18} />
          收好凭证
        </button>
      </section>
    </div>
  )
}

function EmptyView({ view }: { view: Exclude<ViewKey, 'focus'> }) {
  const definition = views.find((item) => item.key === view)
  const Icon = definition?.icon ?? Clock3

  return (
    <section className="empty-view">
      <Icon aria-hidden="true" size={28} />
      <h2>{definition?.label}</h2>
      <p>{emptyStateCopy[view]}</p>
    </section>
  )
}

function App() {
  const [theme, setTheme] = useState<Theme>('light')
  const [feedbackNotices, setFeedbackNotices] = useState<FeedbackNotice[]>([])
  const [activeView, setActiveView] = useState<ViewKey>(() => {
    const requested = new URLSearchParams(window.location.search).get('view')
    return views.some((view) => view.key === requested)
      ? (requested as ViewKey)
      : 'focus'
  })
  const initialize = useSessionStore((store) => store.initialize)
  const balanceSeconds = useSessionStore(
    (store) => store.state?.balanceSeconds ?? 0,
  )
  const activeDefinition =
    views.find((item) => item.key === activeView) ?? views[0]
  const dismissFeedback = useCallback((id: string) => {
    setFeedbackNotices((current) =>
      current.filter((notice) => notice.id !== id),
    )
  }, [])
  const showFeedback = useCallback((notice: FeedbackInput) => {
    setFeedbackNotices((current) =>
      [
        ...current,
        {
          ...notice,
          id: crypto.randomUUID(),
        },
      ].slice(-3),
    )
  }, [])
  const feedback = useMemo(() => ({ showFeedback }), [showFeedback])
  const today = useMemo(
    () =>
      new Intl.DateTimeFormat('zh-CN', {
        month: 'long',
        day: 'numeric',
        weekday: 'long',
      }).format(new Date()),
    [],
  )

  useEffect(() => {
    document.documentElement.dataset.theme = theme
  }, [theme])

  useEffect(() => {
    void initialize()
  }, [initialize])

  return (
    <FeedbackContext.Provider value={feedback}>
      <div className="app-shell">
        <aside className="sidebar">
          <div className="brand">
            <div className="brand-mark">
              <Clock3 aria-hidden="true" size={20} strokeWidth={2.2} />
            </div>
            <div>
              <strong>TimeBet</strong>
              <span>时间银行</span>
            </div>
          </div>

          <nav className="primary-nav" aria-label="主导航">
            {views.map((view) => {
              const Icon = view.icon
              const isActive = view.key === activeView

              return (
                <button
                  key={view.key}
                  className="nav-item"
                  data-active={isActive}
                  type="button"
                  aria-current={isActive ? 'page' : undefined}
                  onClick={() => setActiveView(view.key)}
                >
                  <Icon aria-hidden="true" size={19} />
                  {view.label}
                </button>
              )
            })}
          </nav>

          <div className="sidebar-footer">
            <span>可用时间</span>
            <strong>{formatBalance(balanceSeconds)}</strong>
          </div>
        </aside>

        <main className="main-content" data-view={activeView}>
          <header className="topbar">
            <div>
              <span className="date-label">{today}</span>
              <h1>{activeDefinition.label}</h1>
            </div>
            <button
              className="icon-button"
              type="button"
              aria-label={
                theme === 'light' ? '切换到深色主题' : '切换到浅色主题'
              }
              title={theme === 'light' ? '深色主题' : '浅色主题'}
              onClick={() =>
                setTheme((current) => (current === 'light' ? 'dark' : 'light'))
              }
            >
              {theme === 'light' ? (
                <Moon aria-hidden="true" size={19} />
              ) : (
                <Sun aria-hidden="true" size={19} />
              )}
            </button>
          </header>

          <div className="content-area">
            {activeView === 'focus' && <FocusView />}
            {activeView === 'planner' && <WeeklyPlannerView />}
            {activeView === 'timeline' && <TimelineView />}
            {activeView === 'analytics' && <AnalyticsView />}
            {activeView === 'settings' && <SettingsView />}
            {activeView === 'rewards' && <RewardsView />}
            {activeView === 'challenge' && <ChallengeView />}
            {activeView !== 'focus' &&
              activeView !== 'planner' &&
              activeView !== 'timeline' &&
              activeView !== 'analytics' &&
              activeView !== 'settings' &&
              activeView !== 'rewards' &&
              activeView !== 'challenge' && <EmptyView view={activeView} />}
          </div>
        </main>
      </div>
      <FeedbackViewport notices={feedbackNotices} onDismiss={dismissFeedback} />
    </FeedbackContext.Provider>
  )
}

function useElapsedSeconds(session: StudySession | null, asOfUtc?: string) {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    if (session?.status !== 'running') return
    const timer = window.setInterval(() => setNow(Date.now()), 500)
    return () => window.clearInterval(timer)
  }, [session?.status])

  if (!session) return 0
  if (session.status === 'paused' || !asOfUtc) {
    return session.effectiveDurationSeconds
  }

  const driftSeconds = Math.max(
    0,
    Math.floor((now - new Date(asOfUtc).getTime()) / 1000),
  )
  return session.effectiveDurationSeconds + driftSeconds
}

function formatClock(seconds: number) {
  const safeSeconds = Math.max(0, Math.floor(seconds))
  const hours = Math.floor(safeSeconds / 3600)
  const minutes = Math.floor((safeSeconds % 3600) / 60)
  const remainder = safeSeconds % 60
  return [hours, minutes, remainder]
    .map((value) => value.toString().padStart(2, '0'))
    .join(':')
}

function formatBalance(seconds: number) {
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  if (hours > 0 && minutes > 0) return `${hours} 小时 ${minutes} 分钟`
  if (hours > 0) return `${hours} 小时`
  if (minutes > 0) return `${minutes} 分钟`
  return `${seconds} 秒`
}

function formatCompactDuration(seconds: number) {
  if (seconds < 60) return `${seconds} 秒`
  return `${Math.floor(seconds / 60)} 分钟`
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(new Date(value))
}

function formatArchiveDate(value: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    month: 'long',
    day: 'numeric',
    weekday: 'short',
  }).format(new Date(value))
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(new Date(value))
}

function formatDateInput(date: Date) {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function moveTimeBlockToLocalDate(
  block: PlanningTimeBlock,
  targetDate: string,
) {
  const [year, month, day] = targetDate.split('-').map(Number)
  const startsAt = new Date(block.startsAtUtc)
  startsAt.setFullYear(year, month - 1, day)
  return startsAt
}

function timeRangesOverlap(
  firstStartsAt: number,
  firstEndsAt: number,
  secondStartsAt: number,
  secondEndsAt: number,
) {
  return firstStartsAt < secondEndsAt && firstEndsAt > secondStartsAt
}

function defaultScheduleTime() {
  const date = new Date()
  date.setMinutes(Math.ceil(date.getMinutes() / 30) * 30, 0, 0)
  return `${String(date.getHours()).padStart(2, '0')}:${String(
    date.getMinutes(),
  ).padStart(2, '0')}`
}

function formatDueDate(value: string) {
  const today = new Date()
  const tomorrow = new Date(today)
  tomorrow.setDate(tomorrow.getDate() + 1)
  if (value === formatDateInput(today)) return '今天截止'
  if (value === formatDateInput(tomorrow)) return '明天截止'
  return `${new Intl.DateTimeFormat('zh-CN', {
    month: 'numeric',
    day: 'numeric',
  }).format(new Date(`${value}T00:00:00`))} 截止`
}

function formatWeekRange(week: WeekPlan) {
  const start = new Date(`${week.weekStartedDate}T00:00:00`)
  const end = new Date(start)
  end.setDate(end.getDate() + 6)
  const formatter = new Intl.DateTimeFormat('zh-CN', {
    month: 'numeric',
    day: 'numeric',
  })
  return `${formatter.format(start)} — ${formatter.format(end)}`
}

function formatWeekday(value: string) {
  return new Intl.DateTimeFormat('zh-CN', { weekday: 'short' }).format(
    new Date(`${value}T00:00:00`),
  )
}

function formatMonthDay(value: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    month: 'numeric',
    day: 'numeric',
  }).format(new Date(`${value}T00:00:00`))
}

function timeBlockStatusLabel(status: PlanningTimeBlock['status']) {
  if (status === 'completed') return '已完成'
  if (status === 'skipped') return '已跳过'
  return '等待执行'
}

function taskRelevanceLevelLabel(level: TaskRelevanceSnapshot['level']) {
  switch (level) {
    case 'high':
      return '高关联'
    case 'possible':
      return '可能相关'
    case 'low':
      return '可能偏离'
    case 'idle':
      return '检测到空闲'
    case 'disabled':
      return '活动记录已关闭'
    default:
      return '无法判断'
  }
}

function formatChartDuration(seconds: number) {
  if (seconds <= 0) return '0m'
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  return hours > 0
    ? `${hours}h${minutes > 0 ? ` ${minutes}m` : ''}`
    : `${minutes}m`
}

function formatTrendDay(value: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    weekday: 'short',
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
  }).format(new Date(value))
}

function ledgerTypeLabel(type: LedgerEntry['type']) {
  switch (type) {
    case 'earn':
      return '学习收入'
    case 'spend':
      return '奖励消费'
    case 'game-win':
      return '挑战收入'
    case 'game-loss':
      return '挑战支出'
    case 'adjustment':
      return '余额调整'
  }
}

function getChallengeDisabledReason(overview: ChallengeOverview) {
  if (overview.hasActiveStudySession)
    return '学习计时进行中，请先完成当前时间块。'
  if (overview.roundsRemainingToday <= 0)
    return '今天的 3 次星愿机会已经用完了。'
  if (overview.balanceSeconds < overview.minimumBalanceSeconds)
    return '至少积累 10 分钟可用时间后才能开启星愿。'
  if (overview.maximumWagerSeconds < overview.minimumWagerSeconds)
    return '当前可投入时间不足。'
  return null
}

function formatMultiplier(multiplier: number) {
  return Number.isInteger(multiplier)
    ? multiplier.toString()
    : multiplier.toFixed(1)
}

function formatPercentage(value: number) {
  const percentage = value * 100
  return `${percentage.toFixed(Number.isInteger(percentage) ? 0 : 1)}%`
}

function getRewardTier(costSeconds: number) {
  if (costSeconds <= 60 * 60) {
    return { key: 'quick', label: '轻松奖励', icon: Sparkles }
  }
  if (costSeconds <= 3 * 60 * 60) {
    return { key: 'milestone', label: '阶段奖励', icon: Gift }
  }
  return { key: 'long-term', label: '长期奖励', icon: Crown }
}

function getFocusCharacterAsset(status: string) {
  if (status === 'paused') {
    return {
      key: 'resting',
      src: '/assets/illustrations/time-bank-trainee-resting.png',
    }
  }
  return {
    key: status === 'running' ? 'focused' : 'idle',
    src:
      status === 'running'
        ? '/assets/illustrations/time-bank-trainee-focused.png'
        : '/assets/illustrations/time-bank-trainee.png',
  }
}

function formatPreciseDuration(seconds: number) {
  const safeSeconds = Math.max(0, Math.floor(seconds))
  const hours = Math.floor(safeSeconds / 3600)
  const minutes = Math.floor((safeSeconds % 3600) / 60)
  const remainder = safeSeconds % 60
  const parts: string[] = []
  if (hours > 0) parts.push(`${hours} 小时`)
  if (minutes > 0) parts.push(`${minutes} 分钟`)
  if (remainder > 0 || parts.length === 0) parts.push(`${remainder} 秒`)
  return parts.join(' ')
}

export default App
