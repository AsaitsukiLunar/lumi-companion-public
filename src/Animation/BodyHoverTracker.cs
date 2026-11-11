// Version: 0.3.3-preview; Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// LOCAL-WORK / Windows / Codex Desktop / O2 integration review / Codex
// Original source: reproduced stationary-pointer task/Look mask feedback oscillation.
namespace LumiCompanion.Animation;

/// <summary>Prevents a pose change from repeatedly re-entering hover under a stationary cursor.</summary>
public sealed class BodyHoverTracker
{
    private bool _hovered;
    private bool _sampled;
    private int _lastX, _lastY;
    private (int X, int Y)? _exitPoint;

    public bool Update(int screenX, int screenY, bool currentBodyHit)
    {
        if (_hovered && !currentBodyHit && _sampled && DistanceSquared(screenX, screenY, _lastX, _lastY) < 4)
            _exitPoint = (screenX, screenY);
        if (_exitPoint is { } exit && DistanceSquared(screenX, screenY, exit.X, exit.Y) >= 4)
            _exitPoint = null;
        _hovered = currentBodyHit && _exitPoint is null;
        _sampled = true; _lastX = screenX; _lastY = screenY;
        return _hovered;
    }

    public void Reset() { _hovered = false; _sampled = false; _exitPoint = null; }

    private static double DistanceSquared(int x, int y, int otherX, int otherY) =>
        Math.Pow((double)x - otherX, 2) + Math.Pow((double)y - otherY, 2);
}
