// Version: 0.3.4-preview; Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source: RAW | user drag-motion regression; LOCAL-WORK | O2 drag-motion subtask / Codex / Windows.
// Original source: actual incremental captured pointer movement; existing directional PNG animations.
namespace LumiCompanion.Behavior;

/// <summary>Classifies recent physical displacement, not the cumulative distance from drag start.</summary>
public sealed class DragMotionTracker
{
    public static readonly TimeSpan StillDelay = TimeSpan.FromMilliseconds(180);
    private int _direction;
    private DateTimeOffset _lastHorizontal;
    private DateTimeOffset? _pendingAt;
    private double _pendingX, _pendingY;

    public void Reset()
    {
        _direction = 0; _pendingAt = null; _pendingX = 0; _pendingY = 0;
    }

    public void Observe(double deltaX, double deltaY, DateTimeOffset now)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY) || (deltaX == 0 && deltaY == 0)) return;
        ReadDirection(now);
        if (Math.Abs(deltaY) >= 1 && Math.Abs(deltaY) > Math.Abs(deltaX) * 1.5)
        {
            // Vertical lifting, including a little horizontal hand jitter, stays airborne.
            Reset();
            return;
        }
        if (_pendingAt is null || now - _pendingAt >= StillDelay)
        {
            _pendingX = 0; _pendingY = 0;
        }
        _pendingAt = now;
        _pendingX += deltaX; _pendingY += deltaY;
        double threshold = _direction == 0 || Math.Sign(_pendingX) == _direction ? 2 : 3;
        if (Math.Abs(_pendingX) < threshold || Math.Abs(_pendingX) < Math.Abs(_pendingY) * .75) return;
        _direction = Math.Sign(_pendingX);
        _lastHorizontal = now;
        _pendingX = 0; _pendingY = 0;
    }

    public int ReadDirection(DateTimeOffset now)
    {
        if (_direction != 0 && now - _lastHorizontal >= StillDelay) Reset();
        return _direction;
    }
}
