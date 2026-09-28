using TimeBet.Domain;

namespace TimeBet.Domain.Timeline;

public sealed record ScreenshotRecord(
    Guid Id,
    Guid SessionId,
    Guid? ActivitySegmentId,
    DateTimeOffset CapturedAtUtc,
    string FilePath,
    string? ThumbnailPath,
    int Width,
    int Height,
    long FileSizeBytes)
{
    public static ScreenshotRecord Create(
        Guid sessionId,
        DateTimeOffset capturedAtUtc,
        string filePath,
        int width,
        int height,
        long fileSizeBytes,
        Guid? activitySegmentId = null,
        string? thumbnailPath = null,
        Guid? id = null)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
        }

        if (width <= 0 || height <= 0 || fileSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Screenshot dimensions and size are invalid.");
        }

        return new ScreenshotRecord(
            id ?? Guid.NewGuid(),
            sessionId,
            activitySegmentId,
            DomainGuards.Utc(capturedAtUtc, nameof(capturedAtUtc)),
            DomainGuards.NonEmpty(filePath, nameof(filePath)),
            thumbnailPath?.Trim(),
            width,
            height,
            fileSizeBytes);
    }
}
