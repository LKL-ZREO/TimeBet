using TimeBet.Application.Timeline;

namespace TimeBet.Infrastructure.Tests;

public sealed class TimelineServiceTests
{
    [Fact]
    public async Task ScreenshotCannotBeEnabledWhenActivityTrackingIsOff()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var service = new TimelineService(
            database.Timeline,
            database.TrackingSettings,
            Path.Combine(Path.GetTempPath(), "TimeBet.Tests.Screenshots"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateSettingsAsync(
            new UpdateTrackingSettingsRequest(false, true, 5, 7, 5)));
    }

    [Fact]
    public async Task SettingsValidationRejectsUnsupportedIntervals()
    {
        await using var database = new TemporaryDatabase();
        await database.InitializeAsync();
        var service = new TimelineService(
            database.Timeline,
            database.TrackingSettings,
            Path.Combine(Path.GetTempPath(), "TimeBet.Tests.Screenshots"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.UpdateSettingsAsync(
            new UpdateTrackingSettingsRequest(true, true, 2, 7, 5)));
    }
}
