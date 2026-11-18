using System.Drawing;

namespace DarkShot.Core;

public sealed record MonitorDescriptor(
    IntPtr Handle,
    string DeviceName,
    Rectangle Bounds,
    Rectangle WorkArea,
    bool IsPrimary,
    uint Dpi)
{
    public double ScaleFactor => Dpi / 96.0;
    public string DisplayName => string.IsNullOrWhiteSpace(DeviceName)
        ? $"Display {Handle}"
        : DeviceName.Replace(@"\\.\", "");
}

public static class MonitorLayout
{
    public static Rectangle VirtualBounds
    {
        get
        {
            try
            {
                int x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
                int y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
                int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
                int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
                if (w > 0 && h > 0) return new Rectangle(x, y, w, h);
            }
            catch { /* fall through */ }

            var monitors = GetMonitors();
            if (monitors.Count == 0) return SystemInformation.VirtualScreen;
            var union = monitors[0].Bounds;
            foreach (var m in monitors.Skip(1)) union = Rectangle.Union(union, m.Bounds);
            return union;
        }
    }

    public static IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        var result = new List<MonitorDescriptor>();
        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
            {
                var info = new NativeMethods.MONITORINFOEX
                {
                    cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
                    szDevice = string.Empty
                };

                if (!NativeMethods.GetMonitorInfoEx(hMonitor, ref info))
                    return true;

                uint dpi = 96;
                try
                {
                    if (NativeMethods.GetDpiForMonitor(hMonitor, 0, out uint dx, out _) == 0 && dx > 0)
                        dpi = dx;
                }
                catch { /* older OS */ }

                result.Add(new MonitorDescriptor(
                    hMonitor,
                    info.szDevice ?? string.Empty,
                    rect.ToRectangle(),
                    info.rcWork.ToRectangle(),
                    (info.dwFlags & 1) != 0,
                    dpi));
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "EnumDisplayMonitors failed; falling back to WinForms screens");
        }

        if (result.Count == 0)
        {
            foreach (var screen in Screen.AllScreens)
            {
                result.Add(new MonitorDescriptor(
                    IntPtr.Zero,
                    screen.DeviceName,
                    screen.Bounds,
                    screen.WorkingArea,
                    screen.Primary,
                    96));
            }
        }

        return result;
    }

    public static MonitorDescriptor? GetMonitorAt(Point screenPoint)
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0) return null;

        try
        {
            var hmon = NativeMethods.MonitorFromPoint(NativeMethods.POINT.From(screenPoint), NativeMethods.MONITOR_DEFAULTTONEAREST);
            var match = monitors.FirstOrDefault(m => m.Handle == hmon);
            if (match != null) return match;
        }
        catch { /* ignore */ }

        return monitors.FirstOrDefault(m => m.Bounds.Contains(screenPoint))
               ?? monitors.FirstOrDefault(m => m.IsPrimary)
               ?? monitors[0];
    }

    public static MonitorDescriptor? GetMonitorForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return GetMonitorAt(Cursor.Position);
        try
        {
            var hmon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            return GetMonitors().FirstOrDefault(m => m.Handle == hmon) ?? GetMonitorAt(Cursor.Position);
        }
        catch
        {
            return GetMonitorAt(Cursor.Position);
        }
    }

    public static MonitorDescriptor? Primary => GetMonitors().FirstOrDefault(m => m.IsPrimary) ?? GetMonitors().FirstOrDefault();

    public static Rectangle AllMonitorsBounds
    {
        get
        {
            var monitors = GetMonitors();
            if (monitors.Count == 0) return VirtualBounds;
            var union = monitors[0].Bounds;
            foreach (var m in monitors.Skip(1)) union = Rectangle.Union(union, m.Bounds);
            return union;
        }
    }

    public static string Describe(Point screenPoint)
    {
        var m = GetMonitorAt(screenPoint);
        return m == null ? "virtual desktop" : $"{m.DisplayName} ({m.Bounds.Width}×{m.Bounds.Height} @ {m.Dpi} DPI)";
    }
}
