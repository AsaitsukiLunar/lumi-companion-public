// Version: 0.3.1-preview
// Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source type: LOCAL-WORK; Node: Windows / Codex Desktop / Codex
// Original source: Lumi local playback implementation; current user repair request.
namespace LumiCompanion.Animation;

/// <summary>Elapsed-time playback. Non-looping clips hold their final frame until replaced.</summary>
public sealed class AnimationPlayback
{
    private readonly int _frameCount;
    private readonly TimeSpan _frameDuration;
    private readonly bool _loop;
    private TimeSpan _elapsed;

    public AnimationPlayback(int frameCount, TimeSpan frameDuration, bool loop)
    {
        if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount));
        if (frameDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(frameDuration));
        _frameCount = frameCount;
        _frameDuration = frameDuration;
        _loop = loop;
    }

    public int FrameIndex { get; private set; }
    public bool Completed { get; private set; }

    /// <returns>True only on the advance that completes a non-looping clip.</returns>
    public bool Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (Completed) return false;
        _elapsed += elapsed;
        long frame = _elapsed.Ticks / _frameDuration.Ticks;
        if (!_loop && frame >= _frameCount)
        {
            FrameIndex = _frameCount - 1;
            Completed = true;
            return true;
        }
        FrameIndex = (int)(frame % _frameCount);
        return false;
    }
}
