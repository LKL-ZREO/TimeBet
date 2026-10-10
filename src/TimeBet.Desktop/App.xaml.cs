using System.Net.Http;
using System.IO;
using System.Windows;
using TimeBet.Application.Analytics;
using TimeBet.Application.AI;
using TimeBet.Application.Challenges;
using TimeBet.Application.Leisure;
using TimeBet.Application.Rewards;
using TimeBet.Application.Reviews;
using TimeBet.Application.Sessions;
using TimeBet.Application.Tasks;
using TimeBet.Application.Timeline;
using TimeBet.Infrastructure;
using TimeBet.Infrastructure.Database;
using TimeBet.Infrastructure.AI;
using TimeBet.Tracker.Windows;

namespace TimeBet.Desktop;

public partial class App : System.Windows.Application, IAsyncDisposable
{
    private StudyActivityTracker? activityTracker;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            var connectionFactory = new SqliteConnectionFactory(ResolveDatabasePath());
            await new TimeBetDatabase(connectionFactory).InitializeAsync();

            var sessions = new SqliteStudySessionRepository(connectionFactory);
            var ledger = new SqliteLedgerRepository(connectionFactory);
            var timelineRepository = new SqliteActivityTimelineRepository(connectionFactory);
            var trackingSettings = new SqliteTrackingSettingsRepository(connectionFactory);
            var rewardRepository = new SqliteRewardRepository(connectionFactory);
            var analyticsRepository = new SqliteAnalyticsRepository(connectionFactory);
            var challengeRepository = new SqliteChallengeRepository(connectionFactory);
            var planningTaskRepository = new SqlitePlanningTaskRepository(connectionFactory);
            var planningTimeBlockRepository = new SqlitePlanningTimeBlockRepository(connectionFactory);
            var planningTaskMilestoneRepository = new SqlitePlanningTaskMilestoneRepository(connectionFactory);
            var leisureSessionRepository = new SqliteLeisureSessionRepository(connectionFactory);
            var clock = new SystemClock();
            var taskService = new TaskService(
                planningTaskRepository,
                clock,
                planningTimeBlockRepository,
                planningTaskMilestoneRepository);
            var plannerService = new PlannerService(
                planningTimeBlockRepository,
                planningTaskRepository,
                clock);
            var sessionService = new StudySessionService(
                sessions,
                ledger,
                clock,
                planningTaskRepository,
                planningTimeBlockRepository,
                leisureSessionRepository);
            var leisureSessionService = new LeisureSessionService(
                leisureSessionRepository,
                sessions,
                ledger,
                clock);
            var screenshotRoot = ResolveScreenshotRoot();
            var timelineService = new TimelineService(timelineRepository, trackingSettings, screenshotRoot);
            var rewardService = new RewardService(rewardRepository, ledger, clock);
            var analyticsService = new AnalyticsService(analyticsRepository, clock);
            var dailyReviewService = new DailyReviewService(
                analyticsService,
                plannerService,
                timelineService,
                clock);
            var aiSettingsRepository = new SqliteAiSettingsRepository(connectionFactory);
            var aiReviewService = new AiReviewService(
                dailyReviewService,
                aiSettingsRepository,
                new OpenAiCompatibleReviewProvider(new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(45),
                }));
            var challengeService = new ChallengeService(
                challengeRepository,
                clock,
                new CryptographicRandomSource());

            var activitySource = new WindowsActivitySource();
            var activityEvidence = new ActivityEvidenceBuffer(["TimeBet.Desktop"]);
            activityTracker = new StudyActivityTracker(
                sessions,
                timelineRepository,
                trackingSettings,
                clock,
                activitySource,
                new WindowsScreenshotCapture(),
                screenshotRoot,
                activityEvidence);
            activityTracker.Start();

            var window = new MainWindow(
                sessionService,
                timelineService,
                rewardService,
                analyticsService,
                challengeService,
                screenshotRoot,
                taskService,
                plannerService,
                activityEvidence,
                dailyReviewService,
                aiReviewService,
                leisureSessionService);
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "TimeBet 无法启动",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await DisposeAsync();
        base.OnExit(e);
    }

    public async ValueTask DisposeAsync()
    {
        if (activityTracker is null)
        {
            return;
        }

        await activityTracker.DisposeAsync();
        activityTracker = null;
        GC.SuppressFinalize(this);
    }

    private static string ResolveDatabasePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("TIMEBET_DATABASE_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localData, "TimeBet", "timebet.db");
    }

    private static string ResolveScreenshotRoot()
    {
        var configuredPath = Environment.GetEnvironmentVariable("TIMEBET_SCREENSHOT_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localData, "TimeBet", "Screenshots");
    }
}
