/*
Traceability metadata
Version: 0.3.0-dev
Generated: 2026-08-09 (Asia/Shanghai)
Last updated: 2026-08-09
Content coverage through: 2026-08-09
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using LumiCompanion.Native;
using LumiCompanion.Sensors;

namespace LumiCompanion.Windowing;

public sealed class CodexWindowTargetTracker
{
    private TargetWindowSnapshot? _current;

    public event EventHandler<TargetWindowSnapshot?>? TargetChanged;

    public TargetWindowSnapshot? Current => _current;

    public bool ObserveForeground(WindowActivity window)
    {
        if (!CodexWindowTargetMatcher.IsMatch(window) || !window.Bounds.IsUsable || window.IsMinimized)
        {
            return false;
        }

        TargetWindowSnapshot next = FromActivity(window);
        bool changed = _current is null || _current.Handle != next.Handle;
        _current = next;
        if (changed)
        {
            TargetChanged?.Invoke(this, _current);
        }
        return true;
    }

    public bool Refresh()
    {
        TargetWindowSnapshot? current = _current;
        if (current is null)
        {
            return false;
        }

        if (!NativeMethods.IsWindow(current.Handle) ||
            !NativeMethods.IsWindowVisible(current.Handle) ||
            NativeMethods.IsIconic(current.Handle) ||
            !NativeMethods.GetWindowRect(current.Handle, out NativeMethods.RECT rect))
        {
            Clear();
            return false;
        }

        var bounds = new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (!bounds.IsUsable)
        {
            Clear();
            return false;
        }

        bool geometryChanged = bounds != current.Bounds;
        _current = current with
        {
            Bounds = bounds,
            IsMinimized = false,
            ObservedAt = DateTimeOffset.Now
        };
        if (geometryChanged)
        {
            TargetChanged?.Invoke(this, _current);
        }
        return true;
    }

    public void Clear()
    {
        if (_current is null)
        {
            return;
        }

        _current = null;
        TargetChanged?.Invoke(this, null);
    }

    private static TargetWindowSnapshot FromActivity(WindowActivity window) => new()
    {
        Handle = window.Handle,
        ProcessName = window.ProcessName,
        Title = window.Title,
        ExecutablePath = window.ExecutablePath,
        PackageFullName = window.PackageFullName,
        Bounds = window.Bounds,
        IsFullscreen = window.IsFullscreen,
        IsMinimized = window.IsMinimized,
        ObservedAt = DateTimeOffset.Now
    };
}
