using System.Drawing;
using System.Drawing.Imaging;
using static StepCapture.App.Native.NativeMethods;

namespace StepCapture.App.Imaging;

/// <summary>A raw screenshot of one monitor plus the mouse cursor at the moment it was taken.</summary>
internal sealed class CapturedScreen : IDisposable
{
    public required Bitmap Bitmap { get; init; }

    /// <summary>Monitor rectangle in screen coordinates (physical pixels).</summary>
    public required Rectangle Bounds { get; init; }

    /// <summary>Copy of the cursor image (owned, destroyed on dispose), or zero if the cursor was hidden.</summary>
    public IntPtr Cursor { get; init; }

    /// <summary>Where the cursor image's top-left goes, relative to the bitmap.</summary>
    public Point CursorOrigin { get; init; }

    public Point ToBitmap(int screenX, int screenY) => new(screenX - Bounds.X, screenY - Bounds.Y);

    public void Dispose()
    {
        Bitmap.Dispose();
        if (Cursor != IntPtr.Zero) DestroyIcon(Cursor);
    }
}

internal static class ScreenCapture
{
    /// <summary>Captures the monitor that contains the given screen point.</summary>
    public static CapturedScreen CaptureMonitorAt(int x, int y) =>
        CaptureMonitor(MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST));

    /// <summary>Captures the monitor that shows the foreground window.</summary>
    public static CapturedScreen CaptureForegroundMonitor() =>
        CaptureMonitor(MonitorFromWindow(GetForegroundWindow(), MONITOR_DEFAULTTONEAREST));

    private static CapturedScreen CaptureMonitor(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            throw new InvalidOperationException("Could not read monitor information.");

        var bounds = Rectangle.FromLTRB(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap))
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

        IntPtr cursor = IntPtr.Zero;
        Point origin = default;
        var ci = new CURSORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<CURSORINFO>() };
        if (GetCursorInfo(ref ci) && (ci.flags & CURSOR_SHOWING) != 0 && ci.hCursor != IntPtr.Zero)
        {
            cursor = CopyIcon(ci.hCursor);
            int hotX = 0, hotY = 0;
            if (cursor != IntPtr.Zero && GetIconInfo(cursor, out var ii))
            {
                hotX = ii.xHotspot;
                hotY = ii.yHotspot;
                if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
            }
            origin = new Point(ci.ptScreenPos.X - bounds.X - hotX, ci.ptScreenPos.Y - bounds.Y - hotY);
        }

        return new CapturedScreen { Bitmap = bitmap, Bounds = bounds, Cursor = cursor, CursorOrigin = origin };
    }
}
