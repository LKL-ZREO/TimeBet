using TimeBet.Application.Relevance;

namespace TimeBet.Infrastructure.Tests;

public sealed class TaskRelevanceServiceTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MatchingTaskKeywordIsHighRelevance()
    {
        var result = TaskRelevanceService.Evaluate(
            "复习高等数学极限章节",
            "WINWORD",
            "高等数学极限错题.docx - Word",
            false,
            ObservedAt);

        Assert.Equal("high", result.Level);
        Assert.True(result.Score >= 80);
        Assert.Contains("关键词", Assert.Single(result.Reasons));
    }

    [Fact]
    public void StudyApplicationWithoutContentEvidenceIsOnlyPossible()
    {
        var result = TaskRelevanceService.Evaluate(
            "整理英语阅读笔记",
            "Code",
            "TimeBet - Visual Studio Code",
            false,
            ObservedAt);

        Assert.Equal("possible", result.Level);
        Assert.Equal(58, result.Score);
    }

    [Fact]
    public void DistractingApplicationWithoutKeywordIsLowRelevance()
    {
        var result = TaskRelevanceService.Evaluate(
            "完成物理习题",
            "WeChat",
            "文件传输助手",
            false,
            ObservedAt);

        Assert.Equal("low", result.Level);
        Assert.Equal(18, result.Score);
    }

    [Fact]
    public void DistractingApplicationWithTaskKeywordStillNeedsConfirmation()
    {
        var result = TaskRelevanceService.Evaluate(
            "复习高等数学极限章节",
            "WeChat",
            "高等数学学习群",
            false,
            ObservedAt);

        Assert.Equal("possible", result.Level);
        Assert.InRange(result.Score, 50, 70);
    }

    [Fact]
    public void IdleWindowIsNotTreatedAsLearningEvidence()
    {
        var result = TaskRelevanceService.Evaluate(
            "完成物理习题",
            "WINWORD",
            "物理习题.docx",
            true,
            ObservedAt);

        Assert.Equal("idle", result.Level);
        Assert.True(result.IsIdle);
        Assert.Equal(0, result.Score);
    }

    [Fact]
    public void UnknownApplicationRemainsUnknown()
    {
        var result = TaskRelevanceService.Evaluate(
            "背诵英语单词",
            "custom-reader",
            "Reader",
            false,
            ObservedAt);

        Assert.Equal("unknown", result.Level);
        Assert.Equal(50, result.Score);
    }
}
