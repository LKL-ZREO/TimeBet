using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TimeBet.Tracker.Windows;

public sealed record CapturedScreenshot(string FilePath, int Width, int Height, long FileSizeBytes);

public interface IWindowsScreenshotCapture
{
    CapturedScreenshot? Capture(ForegroundWindowSnapshot window, string filePath);
}

public sealed class WindowsScreenshotCapture : IWindowsScreenshotCapture
{
    public CapturedScreenshot? Capture(ForegroundWindowSnapshot window, string filePath)
    {
        if (window.Width <= 0 || window.Height <= 0)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var bitmap = new Bitmap(window.Width, window.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        var targetDc = graphics.GetHdc();
        var printed = false;
        try
        {
            printed = PrintWindow(window.Handle, targetDc, 2);
        }
        finally
        {
            graphics.ReleaseHdc(targetDc);
        }

        if (!printed || !HasPixelVariation(bitmap))
        {
            graphics.CopyFromScreen(
                window.Left,
                window.Top,
                0,
                0,
                new Size(window.Width, window.Height),
                CopyPixelOperation.SourceCopy);
        }

        bitmap.Save(filePath, ImageFormat.Jpeg);
        var fileInfo = new FileInfo(filePath);
        return new CapturedScreenshot(fileInfo.FullName, bitmap.Width, bitmap.Height, fileInfo.Length);
    }

    private static bool HasPixelVariation(Bitmap bitmap)
    {
        Color? first = null;
        for (var x = bitmap.Width / 8; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 8))
        {
            for (var y = bitmap.Height / 8; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 8))
            {
                var pixel = bitmap.GetPixel(Math.Min(x, bitmap.Width - 1), Math.Min(y, bitmap.Height - 1));
                first ??= pixel;
                if (Math.Abs(pixel.R - first.Value.R) +
                    Math.Abs(pixel.G - first.Value.G) +
                    Math.Abs(pixel.B - first.Value.B) > 12)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint targetDc, uint flags);
}
