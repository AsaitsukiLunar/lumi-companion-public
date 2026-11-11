/*
Traceability metadata
Version: 0.3.2-preview
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-10-30
Content coverage through: 2026-10-30
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LumiCompanion.Animation;

public sealed class SpritePresenter : Grid, IDisposable
{
    private readonly AnimationCatalog _catalog;
    private readonly DispatcherTimer _timer;
    private Image _front;
    private Image _back;
    private Image? _transitionIncoming;
    private AnimationDefinition? _definition;
    private AnimationPlayback? _playback;
    private readonly Stopwatch _frameClock = new();
    private AnimationId? _currentId;
    private int _currentLookIndex = -1;
    private int _frameIndex;
    private int _clipStart;
    private bool? _loopOverride;
    private int? _startFrameOverride;
    private int? _endFrameOverride;
    private int _transitionToken;
    private bool _highResolution;
    private bool _paused;
    private bool _disposed;
    private readonly Dictionary<BitmapSource, SpriteHitMask> _hitMasks = new();
    public SpriteHitMask? CurrentHitMask { get; private set; }
    public event Action? HitMaskChanged;

    public SpritePresenter(AnimationCatalog catalog)
    {
        _catalog = catalog;
        ClipToBounds = false;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _front = CreateImage();
        _back = CreateImage();
        _back.Opacity = 0;
        Children.Add(_front);
        Children.Add(_back);

        _timer = new DispatcherTimer(DispatcherPriority.Render);
        _timer.Tick += OnTick;
        SetAnimation(AnimationId.Idle, null, false);
    }

    public event Action<AnimationId>? AnimationCompleted;

    public void SetAnimation(AnimationId id, int? lookIndex, bool crossFade = true,
        bool? loopOverride = null, int? startFrameOverride = null, int? endFrameOverride = null)
    {
        if (_disposed)
        {
            return;
        }

        int normalizedLook = lookIndex.HasValue ? ((lookIndex.Value % 16) + 16) % 16 : -1;
        if (_currentId == id && _currentLookIndex == normalizedLook &&
            _loopOverride == loopOverride && _startFrameOverride == startFrameOverride && _endFrameOverride == endFrameOverride)
        {
            return;
        }

        bool wasLook = _currentId == AnimationId.Look;
        _currentId = id;
        _currentLookIndex = normalizedLook;
        _loopOverride = loopOverride;
        _startFrameOverride = startFrameOverride;
        _endFrameOverride = endFrameOverride;
        _frameIndex = 0;

        BitmapSource source;
        int fadeMilliseconds;
        if (id == AnimationId.Look)
        {
            source = _catalog.GetLookFrame(normalizedLook < 0 ? 0 : normalizedLook, _highResolution);
            _definition = null;
            _playback = null;
            fadeMilliseconds = wasLook ? 0 : 70;
        }
        else
        {
            _definition = _catalog.Get(id);
            _clipStart = Math.Clamp(startFrameOverride ?? 0, 0, _definition.Frames.Count - 1);
            int clipEnd = Math.Clamp(endFrameOverride ?? (_definition.Frames.Count - 1), _clipStart, _definition.Frames.Count - 1);
            _frameIndex = _clipStart;
            _playback = new AnimationPlayback(clipEnd - _clipStart + 1, _definition.FrameDuration, loopOverride ?? _definition.Loop);
            source = _definition.GetFrame(_frameIndex, _highResolution);
            // Whole-character gaze poses must not overlap on entering or leaving Look.
            fadeMilliseconds = wasLook ? 0 : 70;
        }

        ShowFrame(source, crossFade && id != AnimationId.Look ? fadeMilliseconds : 0);
        UpdateTimerState();
    }

    public void SetTargetPixelWidth(int physicalWidth)
    {
        if (_disposed)
        {
            return;
        }

        bool highResolution = physicalWidth > 192;
        if (_highResolution == highResolution)
        {
            return;
        }

        _highResolution = highResolution;
        RefreshCurrentFrame();
    }

    public void SetPaused(bool paused)
    {
        if (_disposed || _paused == paused)
        {
            return;
        }

        _paused = paused;
        if (paused) AdvancePlayback();
        UpdateTimerState();
    }

    private void UpdateTimerState()
    {
        _timer.Stop();
        _frameClock.Reset();
        if (_paused || _definition is null || _playback is null || _playback.Completed || _disposed)
        {
            return;
        }

        _timer.Interval = TimeSpan.FromMilliseconds(Math.Min(33, _definition.FrameDuration.TotalMilliseconds));
        _frameClock.Start();
        _timer.Start();
    }

    private void RefreshCurrentFrame()
    {
        if (!_currentId.HasValue)
        {
            return;
        }

        BitmapSource source = _currentId.Value == AnimationId.Look
            ? _catalog.GetLookFrame(_currentLookIndex < 0 ? 0 : _currentLookIndex, _highResolution)
            : _definition!.GetFrame(_frameIndex, _highResolution);
        ShowFrame(source, 0);
    }

    private static Image CreateImage()
    {
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(image, EdgeMode.Unspecified);
        return image;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_paused || _definition is null || _disposed)
        {
            UpdateTimerState();
            return;
        }

        AdvancePlayback();
    }

    private void AdvancePlayback()
    {
        if (_definition is null || _playback is null || !_frameClock.IsRunning) return;
        TimeSpan elapsed = _frameClock.Elapsed;
        _frameClock.Restart();
        bool completed = _playback.Advance(elapsed);
        _frameIndex = _clipStart + _playback.FrameIndex;
        BitmapSource source = _definition.GetFrame(_frameIndex, _highResolution);
        UpdateHitMask(source);
        if (_transitionIncoming is not null)
        {
            _transitionIncoming.Source = source;
        }
        else
        {
            _front.Source = source;
        }
        if (completed)
        {
            _timer.Stop();
            _frameClock.Reset();
            AnimationCompleted?.Invoke(_definition.Id);
        }
    }

    private void ShowFrame(BitmapSource source, int fadeMilliseconds)
    {
        UpdateHitMask(source);
        int token = ++_transitionToken;
        CancelAnimations(_front);
        CancelAnimations(_back);
        _transitionIncoming = null;

        if (_front.Source is null || fadeMilliseconds <= 0)
        {
            _front.Source = source;
            _front.Opacity = 1;
            _back.Opacity = 0;
            _back.Source = null;
            return;
        }

        Image outgoing = _front;
        Image incoming = _back;
        incoming.Source = source;
        incoming.Opacity = 0;
        _transitionIncoming = incoming;

        var duration = new Duration(TimeSpan.FromMilliseconds(fadeMilliseconds));
        var fadeIn = new DoubleAnimation(0, 1, duration)
        {
            FillBehavior = FillBehavior.Stop
        };
        var fadeOut = new DoubleAnimation(1, 0, duration)
        {
            FillBehavior = FillBehavior.Stop
        };
        fadeIn.Completed += (_, _) =>
        {
            if (_disposed || token != _transitionToken)
            {
                return;
            }

            CancelAnimations(outgoing);
            CancelAnimations(incoming);
            incoming.Opacity = 1;
            outgoing.Opacity = 0;
            outgoing.Source = null;
            _front = incoming;
            _back = outgoing;
            _transitionIncoming = null;
        };

        outgoing.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        incoming.BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
    }

    private void UpdateHitMask(BitmapSource source)
    {
        if (!_hitMasks.TryGetValue(source, out var mask))
        {
            string? frameKey = _currentId switch
            {
                AnimationId.RunningLeft => $"running-left/{_frameIndex:00}",
                AnimationId.RunningRight => $"running-right/{_frameIndex:00}",
                _ => null
            };
            mask = SpriteHitMask.Create(source, frameKey);
            _hitMasks.Add(source, mask);
        }
        if (ReferenceEquals(CurrentHitMask, mask)) return;
        CurrentHitMask = mask;
        HitMaskChanged?.Invoke();
    }

    private static void CancelAnimations(Image image)
    {
        image.BeginAnimation(OpacityProperty, null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transitionToken++;
        _timer.Stop();
        _frameClock.Reset();
        _timer.Tick -= OnTick;
        CancelAnimations(_front);
        CancelAnimations(_back);
        _transitionIncoming = null;
    }
}




