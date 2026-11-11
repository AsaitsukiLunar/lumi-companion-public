/*
Traceability metadata
Version: 0.3.4-preview
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-10-30
Content coverage through: 2026-10-30
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LumiCompanion.Animation;
using LumiCompanion.Models;
using LumiCompanion.Native;
using Microsoft.Win32;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Color = System.Windows.Media.Color;

namespace LumiCompanion.UI;

public sealed class CompanionWindow : Window, IDisposable
{
    private readonly SpritePresenter _presenter;
    private LumiSettings _settings;
    private IntPtr _handle;
    private bool _allowClose;
    private bool _userHidden;
    private bool _sessionPaused;
    private bool _fullscreenHidden;
    private int _requestedPixelWidth;
    private string _hint = "待机";
    private BodyInputWindow? _bodyInput;
    private bool _dragging;
    private bool _bodyDragActive;
    private readonly BodyHoverTracker _bodyHover = new();
    private (AnimationId Animation, string Hint, bool? Loop, int? Start, int? End)? _hoverContext;
    private NativeMethods.POINT _dragPointerStart;
    private NativeMethods.POINT _dragMotionPointer;
    private NativeMethods.RECT _dragWindowStart;
    internal IntPtr BodyInputHandle => _bodyInput?.Handle ?? IntPtr.Zero;
    internal SpriteHitMask? CurrentHitMask => _presenter.CurrentHitMask;

    public CompanionWindow(AnimationCatalog catalog, LumiSettings settings)
    {
        _settings = settings;
        Title = "Lumi 独立桌宠";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = settings.AlwaysOnTop;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _presenter = new SpritePresenter(catalog);
        _presenter.AnimationCompleted += OnAnimationCompleted;
        _presenter.HitMaskChanged += SyncBodyInput;
        _presenter.SetPaused(true);
        Content = _presenter;
        ApplySize(settings.PetWidth);
        ToolTip = BuildToolTip();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        DpiChanged += OnDpiChanged;
        LocationChanged += OnPlacementChanged;
        SizeChanged += OnPlacementChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public event EventHandler<bool>? InteractionRequested;
    public event Action<AnimationId>? AnimationCompleted;

    private void OnAnimationCompleted(AnimationId animation) => AnimationCompleted?.Invoke(animation);
    public event EventHandler<bool>? Dragged;
    public event Action<bool>? DraggingChanged;
    public event Action<double, double>? DragMotionChanged;
    public bool IsBodyHovered
    {
        get
        {
            if (!IsVisible || _settings.ClickThrough || !NativeMethods.GetCursorPos(out var point))
            {
                _bodyHover.Reset();
                return false;
            }
            return _bodyHover.Update(point.X, point.Y, ContainsBodyScreenPoint(new Point(point.X, point.Y)));
        }
    }

    /// <summary>Tests physical screen pixels against the currently displayed semantic body.</summary>
    public bool ContainsBodyScreenPoint(Point screenPoint)
    {
        if (!IsVisible || _settings.ClickThrough || CurrentHitMask is null ||
            !NativeMethods.GetWindowRect(_handle, out var rect)) return false;
        return CurrentHitMask.GetScaledPart((int)Math.Floor(screenPoint.X - rect.Left),
            (int)Math.Floor(screenPoint.Y - rect.Top), rect.Width, rect.Height) == SpriteHitPart.Body;
    }
    public event EventHandler? ContextMenuRequested;
    public event EventHandler? PositionChangedByUser;
    public event EventHandler<int>? WidthChangedByUser;

    public HwndSource? HwndSource => _handle == IntPtr.Zero ? null : HwndSource.FromHwnd(_handle);

    public int PixelWidth => _requestedPixelWidth;

    public void ApplySettings(LumiSettings settings)
    {
        _settings = settings;
        Topmost = settings.AlwaysOnTop;
        ApplySize(settings.PetWidth);
        ScheduleClampToWorkingArea();
        if (!settings.HideInFullscreen)
        {
            _fullscreenHidden = false;
        }
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.SetClickThrough(_handle, true);
        }

        ApplyVisibility();
        ToolTip = BuildToolTip();
        SyncBodyInput();
    }

    public void SetVisual(AnimationId animation, int? lookIndex, string hint,
        bool? loopOverride = null, int? startFrameOverride = null, int? endFrameOverride = null)
    {
        // P1 hover changes its pose but not the underlying task/ambient context. Returning
        // to the same context must not re-arm hover at the same stationary physical point.
        // A genuinely different state/direct interaction re-arms it, as do hide/show resets.
        if (hint != "正在和你互动")
        {
            var context = (animation, hint, loopOverride, startFrameOverride, endFrameOverride);
            if (_hoverContext != context) { _bodyHover.Reset(); _hoverContext = context; }
        }
        _hint = hint;
        _presenter.SetAnimation(animation, lookIndex, loopOverride: loopOverride,
            startFrameOverride: startFrameOverride, endFrameOverride: endFrameOverride);
        ToolTip = BuildToolTip();
        if (_bodyInput is not null) _bodyInput.ToolTip = ToolTip;
    }

    public void SetSessionPaused(bool paused)
    {
        _sessionPaused = paused;
        ApplyVisibility();
    }

    public void SetFullscreenHidden(bool fullscreen)
    {
        _fullscreenHidden = _settings.HideInFullscreen && fullscreen;
        ApplyVisibility();
    }

    public void ToggleVisible()
    {
        _userHidden = !_userHidden;
        ApplyVisibility();
    }

    public void ShowWithoutActivation()
    {
        _userHidden = false;
        ApplyVisibility();
    }

    public void RequestExit()
    {
        _allowClose = true;
        Close();
    }

    public void ClampToWorkingArea()
    {
        if (_handle != IntPtr.Zero &&
            NativeMethods.GetWindowRect(_handle, out NativeMethods.RECT windowRect))
        {
            IntPtr monitor = NativeMethods.MonitorFromWindow(
                _handle,
                NativeMethods.MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new NativeMethods.MONITORINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
            };
            if (NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
            {
                NativeMethods.RECT area = monitorInfo.rcWork;
                int width = windowRect.Width;
                int height = windowRect.Height;
                int maximumLeft = Math.Max(area.Left, area.Right - width);
                int maximumTop = Math.Max(area.Top, area.Bottom - height);
                int left = Math.Clamp(windowRect.Left, area.Left, maximumLeft);
                int top = Math.Clamp(windowRect.Top, area.Top, maximumTop);
                if (left != windowRect.Left || top != windowRect.Top)
                {
                    NativeMethods.SetWindowPos(
                        _handle,
                        IntPtr.Zero,
                        left,
                        top,
                        0,
                        0,
                        NativeMethods.SWP_NOSIZE |
                        NativeMethods.SWP_NOZORDER |
                        NativeMethods.SWP_NOACTIVATE);
                }
                return;
            }
        }

        Rect fallback = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, fallback.Left, Math.Max(fallback.Left, fallback.Right - Width));
        Top = Math.Clamp(Top, fallback.Top, Math.Max(fallback.Top, fallback.Bottom - Height));
    }

    private void ApplyVisibility()
    {
        bool shouldShow = !_userHidden && !_sessionPaused && !_fullscreenHidden;
        if (shouldShow)
        {
            if (!IsVisible)
            {
                Show();
            }

            _presenter.SetPaused(false);
        }
        else
        {
            _presenter.SetPaused(true);
            if (IsVisible)
            {
                Hide();
            }
        }
        SyncBodyInput();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_settings.PositionX.HasValue && _settings.PositionY.HasValue)
        {
            Left = _settings.PositionX.Value;
            Top = _settings.PositionY.Value;
        }
        else
        {
            Rect work = SystemParameters.WorkArea;
            Left = work.Right - Width - 24;
            Top = work.Bottom - Height - 24;
        }
        ClampToWorkingArea();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        // The full visual is always transparent to mouse input. A separate region-shaped
        // input layer owns body gestures, so visible cable never blocks other applications.
        NativeMethods.SetClickThrough(_handle, true);
        _bodyInput = new BodyInputWindow(this);
        _bodyInput.MouseLeftButtonDown += OnMouseLeftButtonDown;
        _bodyInput.MouseLeftButtonUp += OnBodyLeftButtonUp;
        _bodyInput.MouseMove += OnBodyMouseMove;
        _bodyInput.LostMouseCapture += OnBodyLostCapture;
        _bodyInput.MouseRightButtonUp += OnMouseRightButtonUp;
        _bodyInput.MouseWheel += OnMouseWheel;
        ApplySize(_settings.PetWidth);
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        ApplySize(_requestedPixelWidth);
        Dispatcher.BeginInvoke(ClampToWorkingArea);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        try
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ApplySize(_requestedPixelWidth);
                    ClampToWorkingArea();
                });
            }
        }
        catch (InvalidOperationException)
        {
            // The dispatcher may be shutting down while Windows changes displays.
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.ClickThrough)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            InteractionRequested?.Invoke(this, true);
            e.Handled = true;
            return;
        }

        if (!NativeMethods.GetCursorPos(out var pointer)) return;
        _dragging = BeginBodyDrag(pointer);
        e.Handled = _dragging;
    }

    internal bool BeginBodyDrag(NativeMethods.POINT pointer)
    {
        if (_bodyInput is null || _settings.ClickThrough || !NativeMethods.GetWindowRect(_handle, out _dragWindowStart)) return false;
        _dragPointerStart = pointer;
        _dragMotionPointer = pointer;
        return _dragging = _bodyInput.CaptureMouse();
    }

    private void OnBodyMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed || !NativeMethods.GetCursorPos(out var pointer)) return;
        MoveCapturedBody(pointer);
        e.Handled = true;
    }

    internal void MoveCapturedBody(NativeMethods.POINT pointer)
    {
        if (!_dragging) return;
        int dx = pointer.X - _dragPointerStart.X, dy = pointer.Y - _dragPointerStart.Y;
        double threshold = Math.Max(3, SystemParameters.MinimumHorizontalDragDistance * VisualTreeHelper.GetDpi(this).DpiScaleX);
        if (!_bodyDragActive)
        {
            if (Math.Abs(dx) < threshold && Math.Abs(dy) < threshold) return;
            _bodyDragActive = true;
            DraggingChanged?.Invoke(true);
        }
        bool moved = NativeMethods.SetWindowPos(_handle, IntPtr.Zero,
            _dragWindowStart.Left + dx, _dragWindowStart.Top + dy, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        int stepX = pointer.X - _dragMotionPointer.X, stepY = pointer.Y - _dragMotionPointer.Y;
        if (moved && (stepX != 0 || stepY != 0))
        {
            _dragMotionPointer = pointer;
            DragMotionChanged?.Invoke(stepX, stepY);
        }
    }

    private void OnBodyLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        EndBodyDrag(notify: true);
        e.Handled = true;
    }

    internal void EndBodyDrag(bool notify)
    {
        bool wasActualDrag = _bodyDragActive;
        _dragging = false;
        if (_bodyDragActive) { _bodyDragActive = false; DraggingChanged?.Invoke(false); }
        _bodyInput?.ReleaseMouseCapture();
        if (!notify) return;
        if (!NativeMethods.GetWindowRect(_handle, out var current)) return;
        double deltaX = current.Left - _dragWindowStart.Left;
        double deltaY = current.Top - _dragWindowStart.Top;
        // A real drag that returns to its starting point is still not a confirming click.
        if (!wasActualDrag)
        {
            InteractionRequested?.Invoke(this, false);
        }
        else
        {
            Dragged?.Invoke(this, deltaX >= 0);
            ClampToWorkingArea();
            PositionChangedByUser?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnBodyLostCapture(object sender, MouseEventArgs e)
    {
        _dragging = false;
        if (_bodyDragActive) { _bodyDragActive = false; DraggingChanged?.Invoke(false); }
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_settings.ClickThrough || _dragging)
        {
            return;
        }

        ContextMenuRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_settings.ClickThrough || _dragging)
        {
            return;
        }

        int width = Math.Clamp(_settings.PetWidth + (e.Delta > 0 ? 8 : -8), 96, 320);
        _settings.PetWidth = width;
        ApplySize(width);
        ScheduleClampToWorkingArea();
        WidthChangedByUser?.Invoke(this, width);
        e.Handled = true;
    }

    private void ApplySize(int pixelWidth)
    {
        _requestedPixelWidth = Math.Clamp(pixelWidth, 96, 320);
        _presenter.SetTargetPixelWidth(_requestedPixelWidth);

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
        Width = _requestedPixelWidth / scaleX;
        Height = (_requestedPixelWidth * 208.0 / 192.0) / scaleY;
        SyncBodyInput();
    }

    private void OnPlacementChanged(object? sender, EventArgs e) => SyncBodyInput();

    private void SyncBodyInput()
    {
        if (_bodyInput is null || _handle == IntPtr.Zero) return;
        if (!IsVisible || _settings.ClickThrough)
        {
            _bodyHover.Reset();
            _dragging = false;
            _bodyInput.ReleaseMouseCapture();
            _bodyInput.Hide();
            return;
        }
        if (!NativeMethods.GetWindowRect(_handle, out var rect) || rect.Width <= 0 || rect.Height <= 0) return;
        _bodyInput.Topmost = Topmost;
        _bodyInput.ToolTip = ToolTip;
        _bodyInput.Sync(rect, _presenter.CurrentHitMask);
    }

    private void ScheduleClampToWorkingArea()
    {
        if (_handle == IntPtr.Zero || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(ClampToWorkingArea);
        }
        catch (InvalidOperationException)
        {
            // The dispatcher may be shutting down while a settings dialog closes.
        }
    }

    private string BuildToolTip()
    {
        string privacy = _settings.SensorsPaused ? "感知已暂停" : "离线感知中";
        return $"Lumi · {_hint}\n{privacy}\n右键打开设置；滚轮缩放；双击互动";
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            _userHidden = true;
            ApplyVisibility();
        }
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _presenter.Dispose();
        _presenter.AnimationCompleted -= OnAnimationCompleted;
        _presenter.HitMaskChanged -= SyncBodyInput;
        if (_bodyInput is not null)
        {
            _bodyInput.MouseLeftButtonDown -= OnMouseLeftButtonDown;
            _bodyInput.MouseLeftButtonUp -= OnBodyLeftButtonUp;
            _bodyInput.MouseMove -= OnBodyMouseMove;
            _bodyInput.LostMouseCapture -= OnBodyLostCapture;
            _bodyInput.MouseRightButtonUp -= OnMouseRightButtonUp;
            _bodyInput.MouseWheel -= OnMouseWheel;
            _bodyInput.Dispose();
            _bodyInput = null;
        }
        SourceInitialized -= OnSourceInitialized;
        Loaded -= OnLoaded;
        Closing -= OnClosing;
        DpiChanged -= OnDpiChanged;
        LocationChanged -= OnPlacementChanged;
        SizeChanged -= OnPlacementChanged;
    }
}

// The minimum nonzero alpha is required for a layered HWND to participate in Win32 hit testing.
// The visual sprite is elsewhere; SetWindowRgn clips only this nearly invisible input surface.
internal sealed class BodyInputWindow : Window, IDisposable
{
    private HwndSource? _source;
    private SpriteHitMask? _lastMask;
    private int _lastWidth, _lastHeight;
    internal IntPtr Handle { get; private set; }
    internal BodyInputWindow(Window owner)
    {
        Owner = owner;
        Title = "Lumi 身体交互区域";
        Width = 1; Height = 1;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        ShowInTaskbar = false; ShowActivated = false;
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(Handle);
            _source?.AddHook(Hook);
            long style = NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE, new IntPtr(style | 0x08000000L));
        };
    }

    internal void Sync(NativeMethods.RECT rect, SpriteHitMask? mask)
    {
        new WindowInteropHelper(this).EnsureHandle();
        if (mask is null) { Hide(); return; }
        if (!ReferenceEquals(mask, _lastMask) || _lastWidth != rect.Width || _lastHeight != rect.Height)
        {
            byte[] data = mask.CreateBodyRegionData(rect.Width, rect.Height);
            IntPtr region = NativeMethods.ExtCreateRegion(IntPtr.Zero, (uint)data.Length, data);
            if (region == IntPtr.Zero) { Hide(); return; }
            if (NativeMethods.SetWindowRgn(Handle, region, true) == 0)
            {
                NativeMethods.DeleteObject(region); Hide(); return;
            }
            _lastMask = mask; _lastWidth = rect.Width; _lastHeight = rect.Height;
        }
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        Width = rect.Width / dpi.DpiScaleX;
        Height = rect.Height / dpi.DpiScaleY;
        if (!IsVisible) Show();
        NativeMethods.SetWindowPos(Handle, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return new IntPtr(3); } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        ReleaseMouseCapture();
        _source?.RemoveHook(Hook);
        Close();
    }
}
