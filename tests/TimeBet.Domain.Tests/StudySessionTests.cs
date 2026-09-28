using TimeBet.Domain;
using TimeBet.Domain.Ledger;
using TimeBet.Domain.Sessions;

namespace TimeBet.Domain.Tests;

public sealed class StudySessionTests
{
    private static readonly DateTimeOffset StartTime =
        new(2026, 8, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PauseResumeAndCompleteCalculateEffectiveDuration()
    {
        var started = StudySession.Start("完成极限章节", StartTime);
        var paused = started.Session.Pause(StartTime.AddMinutes(10));
        var resumed = paused.Session.Resume(StartTime.AddMinutes(15));
        var completed = resumed.Session.Complete(
            StartTime.AddMinutes(35),
            "完成例题并整理错题",
            [new SessionTaskDraft("完成例题 12 道", true)]);

        Assert.Equal(StudySessionStatus.Completed, completed.Session.Status);
        Assert.Equal(30 * 60, completed.Session.EffectiveDurationSeconds);
        Assert.Equal(30 * 60, completed.Credit.AmountSeconds);
        Assert.Null(completed.Session.PausedAtUtc);
        Assert.Single(completed.Tasks);
        Assert.Equal(SessionEventType.Completed, completed.Event.Type);
    }

    [Fact]
    public void CompletionRequiresANoteAndEffectiveTime()
    {
        var started = StudySession.Start("阅读资料", StartTime);

        Assert.Throws<ArgumentException>(() =>
            started.Session.Complete(StartTime.AddMinutes(1), " ", []));
        Assert.Throws<DomainRuleException>(() =>
            started.Session.Complete(StartTime, "没有经过有效时间", []));
    }

    [Fact]
    public void NonUtcTimestampsAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            StudySession.Start("目标", new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.FromHours(8))));
    }

    [Fact]
    public void StudyCreditIsPositiveAndSourceBoundToSession()
    {
        var started = StudySession.Start("背诵单词", StartTime);
        var completed = started.Session.Complete(StartTime.AddMinutes(5), "完成一组单词", []);
        var credit = LedgerEntry.ForStudySession(completed.Session, StartTime.AddMinutes(5));

        Assert.Equal(LedgerEntryType.Earn, credit.Type);
        Assert.Equal(300, credit.AmountSeconds);
        Assert.Equal("study-session", credit.SourceType);
        Assert.Equal(completed.Session.Id.ToString("D"), credit.SourceId);
    }
}
