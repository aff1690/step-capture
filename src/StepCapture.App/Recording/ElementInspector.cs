using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Automation;
using StepCapture.Core.Model;
using DrawingRect = System.Drawing.Rectangle;

namespace StepCapture.App.Recording;

/// <summary>Looks up what is under the mouse or has focus using Windows UI Automation.</summary>
internal static class ElementInspector
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);
    private static readonly ConcurrentDictionary<int, string> AppNames = new();

    public readonly record struct Result(ElementInfo? Info, DrawingRect? Bounds);

    public static Result AtPoint(int x, int y) =>
        WithTimeout(() => Describe(AutomationElement.FromPoint(new System.Windows.Point(x, y))));

    public static Result Focused() => WithTimeout(() => Describe(AutomationElement.FocusedElement));

    // UI Automation calls go to the target app and can hang if it is busy, so never wait long.
    private static Result WithTimeout(Func<Result> lookup)
    {
        try
        {
            var task = Task.Run(lookup);
            return task.Wait(Timeout) ? task.Result : default;
        }
        catch (Exception ex)
        {
            Log.Error("UI Automation lookup", ex);
            return default;
        }
    }

    private static Result Describe(AutomationElement? element)
    {
        if (element is null) return default;
        try
        {
            var c = element.Current;
            var info = new ElementInfo
            {
                Name = c.Name ?? "",
                ControlType = c.LocalizedControlType ?? "",
                IsPassword = c.IsPassword,
                WindowTitle = TopLevelWindowName(element),
                AppName = AppNameFor(c.ProcessId),
            };

            DrawingRect? bounds = null;
            var r = c.BoundingRectangle;
            if (!r.IsEmpty && !double.IsInfinity(r.Width) && r.Width > 0 && r.Height > 0)
                bounds = new DrawingRect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);

            return new Result(info, bounds);
        }
        catch (ElementNotAvailableException)
        {
            return default; // the element went away while we looked at it (menus closing, etc.)
        }
    }

    private static string TopLevelWindowName(AutomationElement element)
    {
        var walker = TreeWalker.ControlViewWalker;
        var root = AutomationElement.RootElement;
        var current = element;
        for (int depth = 0; depth < 40 && current is not null; depth++)
        {
            var parent = walker.GetParent(current);
            if (parent is null || parent == root) return current.Current.Name ?? "";
            current = parent;
        }
        return "";
    }

    private static string AppNameFor(int pid) => AppNames.GetOrAdd(pid, static id =>
    {
        try
        {
            using var p = Process.GetProcessById(id);
            try
            {
                string? description = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            }
            catch
            {
                // Access denied for elevated/protected processes: fall back to the process name.
            }
            return p.ProcessName;
        }
        catch
        {
            return "";
        }
    });
}
