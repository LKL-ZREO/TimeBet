using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TimeBet.Application.Analytics;
using TimeBet.Application.AI;
using TimeBet.Application.Challenges;
using TimeBet.Application.Leisure;
using TimeBet.Application.Rewards;
using TimeBet.Application.Reviews;
using TimeBet.Application.Sessions;
using TimeBet.Application.Tasks;
using TimeBet.Application.Timeline;
using TimeBet.Desktop.Export;
using TimeBet.Desktop.WebView;
using TimeBet.Tracker.Windows;

namespace TimeBet.Desktop;

public partial class MainWindow : Window
{
    private const string VirtualHostName = "app.timebet.local";
    private const string ScreenshotHostName = "screenshots.timebet.local";
    private readonly WebViewBridge bridge;
    private readonly string screenshotRoot;
    private readonly bool smokeTest = string.Equals(
        Environment.GetEnvironmentVariable("TIMEBET_SMOKE_TEST"),
        "1",
        StringComparison.Ordinal);

    public MainWindow(
        StudySessionService studySessions,
        TimelineService timeline,
        RewardService rewards,
        AnalyticsService analytics,
        ChallengeService challenges,
        string screenshotRoot,
        TaskService taskService,
        PlannerService plannerService,
        IActivityEvidenceSource activityEvidence,
        DailyReviewService dailyReviewService,
        AiReviewService aiReviewService,
        LeisureSessionService leisureSessionService)
    {
        InitializeComponent();
        var exporter = new SummaryExportService(analytics);
        bridge = new WebViewBridge(
            studySessions,
            timeline,
            rewards,
            analytics,
            challenges,
            () => exporter.ExportAsync(),
            taskService,
            plannerService,
            activityEvidence,
            dailyReviewService,
            aiReviewService,
            leisureSessionService);
        this.screenshotRoot = screenshotRoot;

        if (smokeTest)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Browser.EnsureCoreWebView2Async();
            Browser.NavigationCompleted += Browser_NavigationCompleted;
            bridge.Attach(Browser.CoreWebView2);
            Directory.CreateDirectory(screenshotRoot);
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                ScreenshotHostName,
                screenshotRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);

            var developmentUrl = Environment.GetEnvironmentVariable("TIMEBET_FRONTEND_DEV_URL");
            if (!string.IsNullOrWhiteSpace(developmentUrl))
            {
                if (!Uri.TryCreate(developmentUrl, UriKind.Absolute, out var uri) ||
                    !string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("TIMEBET_FRONTEND_DEV_URL must point to localhost.");
                }

                Browser.Source = uri;
                return;
            }

            var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(webRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Frontend assets were not found at '{webRoot}'. Run 'pnpm --dir frontend build' first.");
            }

            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                webRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);
            Browser.Source = new Uri($"https://{VirtualHostName}/index.html");
        }
        catch (Exception exception)
        {
            HandleStartupFailure(exception);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        bridge.Detach();
        base.OnClosed(e);
    }

    private void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (smokeTest)
        {
            System.Windows.Application.Current.Shutdown(e.IsSuccess ? 0 : 1);
        }
    }

    private void HandleStartupFailure(Exception exception)
    {
        if (smokeTest)
        {
            System.Windows.Application.Current.Shutdown(1);
            return;
        }

        MessageBox.Show(
            exception.Message,
            "TimeBet 无法加载界面",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        System.Windows.Application.Current.Shutdown(1);
    }
}
