using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Web.WebView2.Core;
using TimeBet.Application.Analytics;
using TimeBet.Application.AI;
using TimeBet.Application.Challenges;
using TimeBet.Application.Rewards;
using TimeBet.Application.Relevance;
using TimeBet.Application.Reviews;
using TimeBet.Application.Sessions;
using TimeBet.Application.Tasks;
using TimeBet.Application.Timeline;
using TimeBet.Desktop.Export;
using TimeBet.Domain;
using TimeBet.Tracker.Windows;

namespace TimeBet.Desktop.WebView;

public sealed class WebViewBridge
{
    private readonly StudySessionService studySessions;
    private readonly TimelineService timeline;
    private readonly RewardService rewards;
    private readonly AnalyticsService analytics;
    private readonly ChallengeService challenges;
    private readonly DailyReviewService? dailyReview;
    private readonly AiReviewService? aiReview;
    private readonly IActivityEvidenceSource? activityEvidence;
    private readonly TaskService? planningTasks;
    private readonly PlannerService? planner;
    private readonly Func<Task<SummaryExportResult>> exportSummary;
    private CoreWebView2? webView;

    public WebViewBridge(
        StudySessionService studySessions,
        TimelineService timeline,
        RewardService rewards,
        AnalyticsService analytics,
        ChallengeService challenges,
        Func<Task<SummaryExportResult>>? exportSummary = null,
        TaskService? planningTasks = null,
        PlannerService? planner = null,
        IActivityEvidenceSource? activityEvidence = null,
        DailyReviewService? dailyReview = null,
        AiReviewService? aiReview = null)
    {
        this.studySessions = studySessions;
        this.timeline = timeline;
        this.rewards = rewards;
        this.analytics = analytics;
        this.challenges = challenges;
        this.activityEvidence = activityEvidence;
        this.planningTasks = planningTasks;
        this.planner = planner;
        this.dailyReview = dailyReview;
        this.aiReview = aiReview;
        this.exportSummary = exportSummary ?? (() => Task.FromResult(new SummaryExportResult(false, null)));
    }

    public void Attach(CoreWebView2 coreWebView)
    {
        if (webView is not null)
        {
            webView.WebMessageReceived -= OnWebMessageReceived;
            studySessions.StateChanged -= OnStateChanged;
        }

        webView = coreWebView;
        webView.WebMessageReceived += OnWebMessageReceived;
        studySessions.StateChanged += OnStateChanged;
    }

    public void Detach()
    {
        if (webView is null)
        {
            return;
        }

        webView.WebMessageReceived -= OnWebMessageReceived;
        studySessions.StateChanged -= OnStateChanged;
        webView = null;
    }

    public async Task<string> HandleMessageAsync(string messageJson)
    {
        ArgumentNullException.ThrowIfNull(messageJson);

        var requestId = "unknown";
        BridgeResponse response;

        try
        {
            var request = JsonSerializer.Deserialize<BridgeRequest>(messageJson, BridgeJson.Options)
                ?? throw new BridgeRequestException("bad_request", "请求不能为空。");
            requestId = string.IsNullOrWhiteSpace(request.Id) ? "unknown" : request.Id;
            var result = await DispatchAsync(request);
            response = new BridgeResponse(requestId, true, result);
        }
        catch (JsonException exception)
        {
            response = Failure(requestId, "bad_request", exception.Message);
        }
        catch (BridgeRequestException exception)
        {
            response = Failure(requestId, exception.Code, exception.Message);
        }
        catch (DomainRuleException exception)
        {
            response = Failure(requestId, "domain_rule", exception.Message);
        }
        catch (ArgumentException exception)
        {
            response = Failure(requestId, "bad_request", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            response = Failure(requestId, "state_conflict", exception.Message);
        }
        catch (SqliteException)
        {
            response = Failure(requestId, "storage_error", "本地数据操作失败，请稍后重试。");
        }
        catch (Exception)
        {
            response = Failure(requestId, "internal_error", "TimeBet 无法完成这次操作。");
        }

        return JsonSerializer.Serialize(response, BridgeJson.Options);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        PostJson(await HandleMessageAsync(args.WebMessageAsJson));
    }

    private async Task<object?> DispatchAsync(BridgeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Method))
        {
            throw new BridgeRequestException("bad_request", "请求缺少 method。");
        }

        return request.Method switch
        {
            "session.current" => await studySessions.GetStateAsync(),
            "session.start" => await studySessions.StartAsync(Deserialize<StartSessionRequest>(request.Payload)),
            "session.pause" => await studySessions.PauseAsync(),
            "session.resume" => await studySessions.ResumeAsync(),
            "session.finish" => await studySessions.FinishAsync(Deserialize<FinishSessionRequest>(request.Payload)),
            "session.relevance.current" => await GetCurrentRelevanceAsync(),
            "task.list" => await RequirePlanningTasks().GetActiveAsync(),
            "task.create" => await RequirePlanningTasks().CreateAsync(
                Deserialize<CreateTaskRequest>(request.Payload)),
            "task.set-completed" => await RequirePlanningTasks().SetCompletedAsync(
                Deserialize<SetTaskCompletedRequest>(request.Payload)),
            "planner.today" => await RequirePlanner().GetTodayAsync(),
            "planner.week" => await RequirePlanner().GetWeekAsync(),
            "timeblock.create" => await RequirePlanner().CreateAsync(
                Deserialize<CreateTimeBlockRequest>(request.Payload)),
            "timeblock.move" => await RequirePlanner().MoveAsync(
                Deserialize<MoveTimeBlockRequest>(request.Payload)),
            "timeblock.defer" => await RequirePlanner().DeferAsync(
                Deserialize<DeferTimeBlockRequest>(request.Payload)),
            "timeblock.skip" => await RequirePlanner().SkipAsync(
                Deserialize<SkipTimeBlockRequest>(request.Payload)),
            "ledger.balance" => new { balanceSeconds = await GetBalanceAsync() },
            "timeline.today" => await timeline.GetOverviewAsync(),
            "tracking.settings.get" => (await timeline.GetOverviewAsync()).Settings,
            "tracking.settings.update" => await timeline.UpdateSettingsAsync(
                Deserialize<UpdateTrackingSettingsRequest>(request.Payload)),
            "reward.catalog" => await rewards.GetCatalogAsync(),
            "reward.create" => await rewards.CreateAsync(
                Deserialize<CreateRewardRequest>(request.Payload)),
            "reward.update" => await rewards.UpdateAsync(
                Deserialize<UpdateRewardRequest>(request.Payload)),
            "reward.set-active" => await rewards.SetActiveAsync(
                Deserialize<SetRewardActiveRequest>(request.Payload)),
            "reward.redeem" => await rewards.RedeemAsync(
                Deserialize<RedeemRewardRequest>(request.Payload)),
            "analytics.overview" => await analytics.GetOverviewAsync(),
            "review.today.get" => await RequireAiReview().GetTodayAsync(),
            "ai.settings.get" => await RequireAiReview().GetSettingsAsync(),
            "ai.settings.update" => await RequireAiReview().UpdateSettingsAsync(
                Deserialize<UpdateAiSettingsRequest>(request.Payload)),
            "ledger.entries" => await analytics.GetLedgerEntriesAsync(),
            "export.summary" => await exportSummary(),
            "challenge.overview" => await challenges.GetOverviewAsync(),
            "challenge.settings.update" => await challenges.UpdateSettingsAsync(
                Deserialize<UpdateChallengeSettingsRequest>(request.Payload)),
            "challenge.play" => await challenges.PlayAsync(
                Deserialize<PlayChallengeRequest>(request.Payload)),
            _ => throw new BridgeRequestException("unknown_method", $"不支持的请求：{request.Method}"),
        };
    }

    private async Task<long> GetBalanceAsync()
    {
        var state = await studySessions.GetStateAsync();
        return state.BalanceSeconds;
    }

    private async Task<TaskRelevanceSnapshot?> GetCurrentRelevanceAsync()
    {
        var state = await studySessions.GetStateAsync();
        if (state.Session?.Status != "running")
        {
            return null;
        }

        var settings = await timeline.GetSettingsAsync();
        if (!settings.CaptureActivity)
        {
            return TaskRelevanceService.Disabled(state.Session.Goal, DateTimeOffset.UtcNow);
        }

        var evidence = activityEvidence?.GetLatest();
        if (evidence is null || DateTimeOffset.UtcNow - evidence.ObservedAtUtc > TimeSpan.FromSeconds(30))
        {
            return null;
        }

        var isIdle = evidence.IdleDuration >= TimeSpan.FromMinutes(settings.IdleThresholdMinutes);
        return TaskRelevanceService.Evaluate(
            state.Session.Goal,
            evidence.ProcessName,
            evidence.WindowTitle,
            isIdle,
            evidence.ObservedAtUtc);
    }

    private TaskService RequirePlanningTasks()
    {
        return planningTasks
            ?? throw new BridgeRequestException("feature_unavailable", "本地任务服务尚未启动。");
    }

    private PlannerService RequirePlanner()
    {
        return planner
            ?? throw new BridgeRequestException("feature_unavailable", "本地排程服务尚未启动。");
    }

    private DailyReviewService RequireDailyReview()
    {
        return dailyReview
            ?? throw new BridgeRequestException("feature_unavailable", "今日回顾服务尚未启动。");
    }

    private AiReviewService RequireAiReview()
    {
        return aiReview
            ?? throw new BridgeRequestException("feature_unavailable", "AI 回顾服务尚未启动。");
    }

    private static T Deserialize<T>(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new BridgeRequestException("bad_request", "请求缺少 payload。");
        }

        return payload.Deserialize<T>(BridgeJson.Options)
            ?? throw new BridgeRequestException("bad_request", "payload 格式无效。");
    }

    private void OnStateChanged(object? sender, StudyStateSnapshot state)
    {
        Post(new BridgeEvent("session.changed", state));
    }

    private void Post(object message)
    {
        PostJson(JsonSerializer.Serialize(message, BridgeJson.Options));
    }

    private void PostJson(string messageJson)
    {
        webView?.PostWebMessageAsJson(messageJson);
    }

    private static BridgeResponse Failure(string id, string code, string message)
    {
        return new BridgeResponse(id, false, Error: new BridgeError(code, message));
    }

    private sealed class BridgeRequestException : Exception
    {
        public BridgeRequestException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
