using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace DarkShot.Core;

public sealed class CaptureMetadata
{
    public CaptureKind Kind { get; init; } = CaptureKind.Region;
    public Rectangle ScreenBounds { get; init; }
    public DateTime CapturedAt { get; init; } = DateTime.Now;
    public string? SourceProcess { get; init; }
    public string? SourceTitle { get; init; }
    public int MonitorDpi { get; init; } = 96;
    public IntPtr SourceWindow { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
}

public sealed class CaptureFrame : IDisposable
{
    public Bitmap Image { get; }
    public CaptureMetadata Metadata { get; }
    public bool Disposed { get; private set; }

    public CaptureFrame(Bitmap image, CaptureMetadata metadata)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        Metadata = metadata;
    }

    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        Image.Dispose();
    }
}

public sealed class FrozenDesktop : IDisposable
{
    public Bitmap Image { get; }
    public Rectangle Bounds { get; }
    public bool Disposed { get; private set; }

    public FrozenDesktop(Bitmap image, Rectangle bounds)
    {
        Image = image;
        Bounds = bounds;
    }

    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        Image.Dispose();
    }
}

public sealed record WindowInfo(
    IntPtr Handle,
    Rectangle Bounds,
    string Title,
    string ProcessName,
    int ProcessId)
{
    public string DisplayLabel
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(Title) ? "(untitled window)" : Title;
            var app = string.IsNullOrWhiteSpace(ProcessName) ? "" : ProcessName + " — ";
            return app + title;
        }
    }
}

public static class ScreenCapture
{
    private static readonly object ProcCacheGate = new();
    private static readonly Dictionary<int, string> ProcNameCache = new();
    private static readonly string[] DesktopClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    public static FrozenDesktop FreezeDesktop(bool includeCursor = false)
    {
        var bounds = MonitorLayout.VirtualBounds;
        var bmp = CaptureScreenRectNative(bounds, includeCursor);
        return new FrozenDesktop(bmp, bounds);
    }

    public static CaptureFrame CaptureRect(Rectangle screenRect, CaptureKind kind, bool includeCursor = false,
        string? sourceProcess = null, string? sourceTitle = null, IntPtr sourceWindow = default)
    {
        var virtualBounds = MonitorLayout.VirtualBounds;
        var clipped = Rectangle.Intersect(screenRect, virtualBounds);
        if (clipped.Width <= 0 || clipped.Height <= 0)
            throw new InvalidOperationException("Capture rectangle is outside the virtual desktop.");

        var bmp = CaptureScreenRectNative(clipped, includeCursor);
        var dpi = MonitorLayout.GetMonitorAt(new Point(clipped.Left + clipped.Width / 2, clipped.Top + clipped.Height / 2))?.Dpi ?? 96;
        var meta = new CaptureMetadata
        {
            Kind = kind,
            ScreenBounds = clipped,
            CapturedAt = DateTime.Now,
            SourceProcess = sourceProcess,
            SourceTitle = sourceTitle,
            MonitorDpi = (int)dpi,
            SourceWindow = sourceWindow,
            ImageWidth = bmp.Width,
            ImageHeight = bmp.Height
        };
        return new CaptureFrame(bmp, meta);
    }

    public static CaptureFrame CaptureMonitor(MonitorDescriptor monitor, bool includeCursor = false)
        => CaptureRect(monitor.Bounds, CaptureKind.Fullscreen, includeCursor);

    public static CaptureFrame CaptureAllMonitors(bool includeCursor = false)
        => CaptureRect(MonitorLayout.AllMonitorsBounds, CaptureKind.AllMonitors, includeCursor);

    public static CaptureFrame CaptureWindow(WindowInfo window, bool includeCursor = false)
    {
        return CaptureRect(window.Bounds, CaptureKind.Window, includeCursor,
            window.ProcessName, window.Title, window.Handle);
    }

    public static CaptureFrame CaptureRegionFromFrozen(FrozenDesktop frozen, Rectangle selection,
        string? sourceProcess = null, string? sourceTitle = null, IntPtr sourceWindow = default)
    {
        var clipped = Rectangle.Intersect(selection, frozen.Bounds);
        if (clipped.Width <= 0 || clipped.Height <= 0)
            throw new InvalidOperationException("Selection is empty.");

        int localX = clipped.X - frozen.Bounds.X;
        int localY = clipped.Y - frozen.Bounds.Y;
        var cropRect = new Rectangle(localX, localY, clipped.Width, clipped.Height);
        var bmp = frozen.Image.Clone(cropRect, frozen.Image.PixelFormat);
        bmp.SetResolution(96f, 96f);

        var dpi = MonitorLayout.GetMonitorAt(new Point(clipped.Left + clipped.Width / 2, clipped.Top + clipped.Height / 2))?.Dpi ?? 96;
        var meta = new CaptureMetadata
        {
            Kind = CaptureKind.Region,
            ScreenBounds = clipped,
            CapturedAt = DateTime.Now,
            SourceProcess = sourceProcess,
            SourceTitle = sourceTitle,
            MonitorDpi = (int)dpi,
            SourceWindow = sourceWindow,
            ImageWidth = bmp.Width,
            ImageHeight = bmp.Height
        };
        return new CaptureFrame(bmp, meta);
    }

    public static CaptureFrame CaptureForegroundWindow(bool includeCursor = false)
    {
        var info = WindowEnumerator.GetForegroundWindowInfo();
        if (info == null) throw new InvalidOperationException("No capturable foreground window.");
        return CaptureWindow(info, includeCursor);
    }

    private static Bitmap CaptureScreenRectNative(Rectangle rect, bool includeCursor)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new ArgumentException("Capture rectangle must have positive size.", nameof(rect));

        IntPtr screenDc = IntPtr.Zero;
        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldObj = IntPtr.Zero;
        Bitmap? result = null;

        try
        {
            screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
                throw new InvalidOperationException("GetDC failed (no desktop access).");

            memDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero)
                throw new InvalidOperationException("CreateCompatibleDC failed.");

            hBitmap = NativeMethods.CreateCompatibleBitmap(screenDc, rect.Width, rect.Height);
            if (hBitmap == IntPtr.Zero)
                throw new InvalidOperationException("CreateCompatibleBitmap failed.");

            oldObj = NativeMethods.SelectObject(memDc, hBitmap);

            if (!NativeMethods.BitBlt(memDc, 0, 0, rect.Width, rect.Height, screenDc, rect.X, rect.Y,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
            {
                throw new InvalidOperationException("BitBlt failed: " + Marshal.GetLastWin32Error());
            }

            if (includeCursor)
                DrawCursor(memDc, rect);

            result = Image.FromHbitmap(hBitmap);
            result.SetResolution(96f, 96f);
        }
        catch
        {
            result?.Dispose();
            throw;
        }
        finally
        {
            if (memDc != IntPtr.Zero && oldObj != IntPtr.Zero)
                NativeMethods.SelectObject(memDc, oldObj);
            if (hBitmap != IntPtr.Zero)
                NativeMethods.DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero)
                NativeMethods.DeleteDC(memDc);
            if (screenDc != IntPtr.Zero)
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }

        if (result == null)
            throw new InvalidOperationException("Screen capture produced no image.");
        return result;
    }

    private static void DrawCursor(IntPtr memDc, Rectangle screenRect)
    {
        var ci = new NativeMethods.CURSORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>()
        };
        if (!NativeMethods.GetCursorInfo(ref ci) || (ci.flags & NativeMethods.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero)
            return;

        var pt = ci.ptScreenPos;
        if (!screenRect.Contains(pt.X, pt.Y))
            return;

        int hotX = 0, hotY = 0;
        try
        {
            if (NativeMethods.GetIconInfo(ci.hCursor, out NativeMethods.ICONINFO icon))
            {
                hotX = icon.xHotspot;
                hotY = icon.yHotspot;
                if (icon.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(icon.hbmMask);
                if (icon.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(icon.hbmColor);
            }
        }
        catch { /* hotspot defaults to 0 */ }

        NativeMethods.DrawIconEx(memDc, pt.X - screenRect.X - hotX, pt.Y - screenRect.Y - hotY,
            ci.hCursor, 0, 0, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
    }

    public static string GetProcessName(int pid)
    {
        if (pid <= 0) return string.Empty;
        lock (ProcCacheGate)
        {
            if (ProcNameCache.TryGetValue(pid, out var cached)) return cached;
        }

        string name = "";
        try
        {
            using var proc = Process.GetProcessById(pid);
            name = proc.ProcessName;
        }
        catch { /* access denied / exited */ }

        lock (ProcCacheGate)
        {
            if (ProcNameCache.Count > 256) ProcNameCache.Clear();
            ProcNameCache[pid] = name;
        }
        return name;
    }

    public static bool IsDesktopClass(string className)
        => DesktopClasses.Any(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase));
}

public static class WindowEnumerator
{
    private static readonly object Gate = new();
    private static readonly Dictionary<IntPtr, (WindowInfo Info, DateTime Stamp)> Cache = new();

    public static WindowInfo? GetForegroundWindowInfo()
    {
        var hwnd = NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GA_ROOT);
        if (hwnd == IntPtr.Zero) return null;
        return BuildInfo(hwnd, allowOwnProcess: true);
    }

    public static WindowInfo? GetWindowUnderCursor(int excludeProcessId = 0)
    {
        return GetTopWindowAt(NativeMethods.GetCursorPosition(), excludeProcessId);
    }

    /// <summary>
    /// Finds the topmost top-level window containing the point, ignoring our own process.
    /// Plain WindowFromPoint cannot be used while the full-screen capture overlay is visible
    /// because the overlay itself is under the cursor.
    /// </summary>
    public static WindowInfo? GetTopWindowAt(Point screenPoint, int excludeProcessId = 0)
    {
        int ownPid = excludeProcessId != 0 ? excludeProcessId : Environment.ProcessId;
        var candidates = new List<IntPtr>();

        try
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd) || NativeMethods.IsCloaked(hwnd))
                    return true;
                if (NativeMethods.GetWindowPid(hwnd) == ownPid)
                    return true;

                var cls = NativeMethods.GetWindowClass(hwnd);
                if (ScreenCapture.IsDesktopClass(cls))
                    return true;

                var bounds = NativeMethods.GetExtendedWindowBounds(hwnd);
                if (bounds.Width < 24 || bounds.Height < 24) return true;
                if (!bounds.Contains(screenPoint)) return true;

                candidates.Add(hwnd);
                return false;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "EnumWindows failed while detecting window under cursor");
        }

        if (candidates.Count == 0)
        {
            var direct = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(NativeMethods.POINT.From(screenPoint)), NativeMethods.GA_ROOT);
            if (direct != IntPtr.Zero && NativeMethods.GetWindowPid(direct) != ownPid)
                return BuildInfo(direct, allowOwnProcess: false);
            return null;
        }

        return BuildInfo(candidates[0], allowOwnProcess: false);
    }

    public static void InvalidateCache() { lock (Gate) Cache.Clear(); }

    public static WindowInfo? GetWindowInfo(IntPtr hwnd, bool allowOwnProcess = false)
        => BuildInfo(hwnd, allowOwnProcess);

    private static WindowInfo? BuildInfo(IntPtr hwnd, bool allowOwnProcess)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            return null;
        if (NativeMethods.IsCloaked(hwnd)) return null;

        int pid = NativeMethods.GetWindowPid(hwnd);
        if (!allowOwnProcess && pid == Environment.ProcessId) return null;

        var cls = NativeMethods.GetWindowClass(hwnd);
        if (ScreenCapture.IsDesktopClass(cls)) return null;

        var bounds = NativeMethods.GetExtendedWindowBounds(hwnd);
        if (bounds.Width < 8 || bounds.Height < 8) return null;

        lock (Gate)
        {
            if (Cache.TryGetValue(hwnd, out var cached) &&
                cached.Info.Bounds == bounds &&
                (DateTime.UtcNow - cached.Stamp).TotalSeconds < 1.0)
            {
                return cached.Info;
            }
        }

        var title = CleanTitle(NativeMethods.GetWindowTitle(hwnd));
        var info = new WindowInfo(hwnd, bounds, title, ScreenCapture.GetProcessName(pid), pid);
        lock (Gate)
        {
            if (Cache.Count > 128) Cache.Clear();
            Cache[hwnd] = (info, DateTime.UtcNow);
        }
        return info;
    }

    private static string CleanTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var sb = new StringBuilder(title.Length);
        foreach (char c in title)
        {
            if (c == '\r' || c == '\n' || c == '\t') sb.Append(' ');
            else if (!char.IsControl(c)) sb.Append(c);
        }
        var s = sb.ToString().Trim();
        return s.Length > 200 ? s[..200] : s;
    }
}
