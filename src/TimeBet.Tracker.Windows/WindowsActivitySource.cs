using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TimeBet.Tracker.Windows;

public interface IWindowsActivitySource
{
    ForegroundWindowSnapshot? GetCurrent();
}

public sealed class WindowsActivitySource : IWindowsActivitySource
{
    public ForegroundWindowSnapshot? GetCurrent()
    {
        var handle = GetForegroundWindow();
        if (handle == nint.Zero || !GetWindowRect(handle, out var rect))
        {
            return null;
        }

        var titleBuffer = new char[Math.Max(1, GetWindowTextLength(handle) + 1)];
        var titleLength = GetWindowText(handle, titleBuffer, titleBuffer.Length);
        var title = titleLength > 0 ? new string(titleBuffer, 0, titleLength) : string.Empty;
        _ = GetWindowThreadProcessId(handle, out var processId);

        string processName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (ArgumentException)
        {
            processName = "unknown";
        }
        catch (InvalidOperationException)
        {
            processName = "unknown";
        }
        catch (Win32Exception)
        {
            processName = "unknown";
        }

        var idleInfo = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        var idleDuration = GetLastInputInfo(ref idleInfo)
            ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - idleInfo.Time))
            : TimeSpan.Zero;

        return new ForegroundWindowSnapshot(
            handle,
            processName,
            string.IsNullOrWhiteSpace(title) ? processName : title,
            idleDuration,
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint window, [Out] char[] text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo inputInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }
}
