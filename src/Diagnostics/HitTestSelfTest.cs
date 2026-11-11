// Version: 0.3.2-preview; Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// LOCAL-WORK / Windows / Codex Desktop / Lumi body-hit subtask / Codex
// Original source: current user body/cable interaction requirement; unchanged PNG masks; isolated Win32 QA.
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LumiCompanion.Animation;
using LumiCompanion.Models;
using LumiCompanion.Native;
using LumiCompanion.UI;

namespace LumiCompanion.Diagnostics;

public static class HitTestSelfTest
{
    public static int RunUnderlay(string reportPath)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window
        {
            Title = "Lumi isolated hit-test underlay", Width = 420, Height = 440,
            Left = SystemParameters.WorkArea.Left + 40, Top = SystemParameters.WorkArea.Top + 40,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true,
            Background = Brushes.DarkSlateBlue
        };
        window.Loaded += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            NativeMethods.GetWindowRect(handle, out var rect);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                _metadata = new TraceMetadata(), hwnd = handle.ToInt64(),
                left = rect.Left, top = rect.Top, width = rect.Width, height = rect.Height
            }));
        };
        var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        stop.Tick += (_, _) => application.Shutdown();
        stop.Start(); window.Show(); application.Run();
        return 0;
    }

    public static int Run(string reportPath)
    {
        var checks = new List<string>();
        var frameStats = new List<object>();
        string? error = null;
        Process? underlay = null;
        CompanionWindow? pet = null;
        string underlayReport = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "hit-test-underlay-" + Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(underlayReport)!);
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); checks.Add(label); }
        try
        {
            var catalog = new AnimationCatalog();
            // All existing frames, both resolution tiers: no alpha-background pixel is a body pixel.
            foreach (AnimationId id in Enum.GetValues<AnimationId>())
            {
                int count = id == AnimationId.Look ? 16 : catalog.Get(id).Frames.Count;
                for (int frame = 0; frame < count; frame++)
                {
                    string? key = id switch { AnimationId.RunningLeft => $"running-left/{frame:00}", AnimationId.RunningRight => $"running-right/{frame:00}", _ => null };
                    int[] counts = new int[2];
                    for (int tier = 0; tier < 2; tier++)
                    {
                        BitmapSource source = id == AnimationId.Look ? catalog.GetLookFrame(frame, tier == 1) : catalog.Get(id).GetFrame(frame, tier == 1);
                        var mask = SpriteHitMask.Create(source, key);
                        Check(mask.BodyPixelCount > source.PixelWidth * source.PixelHeight / 15 && mask.CablePixelCount > 0,
                            $"{id}/{frame:00}@{tier + 1}: body and cable are distinct");
                        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
                        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                        bgra.CopyPixels(pixels, source.PixelWidth * 4, 0);
                        for (int y = 0; y < mask.Height; y++) for (int x = 0; x < mask.Width; x++)
                        {
                            if (pixels[(y * mask.Width + x) * 4 + 3] < 32 && mask.GetPart(x, y) != SpriteHitPart.Transparent)
                                throw new InvalidOperationException($"Background is interactive in {id}/{frame:00}");
                        }
                        counts[tier] = mask.BodyPixelCount;
                        if (tier == 1) frameStats.Add(new { animation = id.ToString(), frame, mask.BodyPixelCount, mask.CablePixelCount });
                    }
                    Check(Math.Abs(counts[1] / 4.0 - counts[0]) / counts[0] < .15, $"{id}/{frame:00}: resolution tiers agree");
                }
            }

            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing executable path.");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add("--hit-test-underlay"); start.ArgumentList.Add(underlayReport);
            underlay = Process.Start(start) ?? throw new InvalidOperationException("Unable to start isolated underlay.");
            var wait = Stopwatch.StartNew();
            while (!File.Exists(underlayReport) && wait.ElapsedMilliseconds < 10000 && !underlay.HasExited) Thread.Sleep(20);
            Check(File.Exists(underlayReport), "independent underlay process is ready");
            using var data = JsonDocument.Parse(File.ReadAllText(underlayReport));
            IntPtr underlayHandle = new(data.RootElement.GetProperty("hwnd").GetInt64());
            int left = data.RootElement.GetProperty("left").GetInt32(), top = data.RootElement.GetProperty("top").GetInt32();
            var settings = new LumiSettings { AlwaysOnTop = true, PetWidth = 192, PositionX = SystemParameters.WorkArea.Left + 70, PositionY = SystemParameters.WorkArea.Top + 70 };
            pet = new CompanionWindow(catalog, settings);
            pet.ShowWithoutActivation();
            NativeMethods.SetWindowPos(pet.HwndSource!.Handle, IntPtr.Zero, left + 30, top + 30, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            foreach (int width in new[] { 96, 192, 320 })
            {
                settings.PetWidth = width; pet.ApplySettings(settings);
                pet.SetVisual(AnimationId.Look, 0, "身体命中诊断"); Pump();
                Check(NativeMethods.GetWindowRect(pet.HwndSource.Handle, out var rect), "read visual geometry");
                Check(NativeMethods.GetWindowRect(pet.BodyInputHandle, out var inputRect) && inputRect.Left == rect.Left &&
                      inputRect.Top == rect.Top && inputRect.Width == rect.Width && inputRect.Height == rect.Height,
                    $"{width}px: input geometry matches physical visual bounds");
                foreach (SpriteHitPart part in Enum.GetValues<SpriteHitPart>())
                {
                    var sample = FindSample(pet.CurrentHitMask!, part, rect.Width, rect.Height);
                    var at = new NativeMethods.POINT { X = rect.Left + sample.X, Y = rect.Top + sample.Y };
                    IntPtr hit = NativeMethods.WindowFromPoint(at);
                    Check(hit == (part == SpriteHitPart.Body ? pet.BodyInputHandle : underlayHandle),
                        $"{width}px: {part} resolves to correct HWND across processes (actual={hit}, input={pet.BodyInputHandle}, underlay={underlayHandle})");
                }
            }
            settings.PetWidth = 192; pet.ApplySettings(settings);
            for (int frame = 0; frame < 16; frame++)
            {
                pet.SetVisual(AnimationId.Look, frame, "方向命中诊断"); Pump();
                NativeMethods.GetWindowRect(pet.HwndSource.Handle, out var rect);
                var sample = FindSample(pet.CurrentHitMask!, SpriteHitPart.Cable, rect.Width, rect.Height);
                Check(NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = rect.Left + sample.X, Y = rect.Top + sample.Y }) == underlayHandle,
                    $"direction {frame}: cable remains cross-process click-through");
            }
            if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Released)
            {
                NativeMethods.GetWindowRect(pet.HwndSource.Handle, out var before);
                Check(pet.BeginBodyDrag(new NativeMethods.POINT { X = 100, Y = 100 }), "input layer captures body drag");
                try
                {
                    pet.SetVisual(AnimationId.Look, 4, "捕获期间切帧");
                    pet.MoveCapturedBody(new NativeMethods.POINT { X = 113, Y = 107 }); Pump();
                    NativeMethods.GetWindowRect(pet.HwndSource.Handle, out var moved);
                    Check(moved.Left == before.Left + 13 && moved.Top == before.Top + 7, "captured synthetic drag moves visual by physical delta");
                    Check(NativeMethods.GetWindowRect(pet.BodyInputHandle, out var input) && input.Left == moved.Left && input.Top == moved.Top,
                        "input layer follows captured visual drag and frame change");
                }
                finally { pet.EndBodyDrag(notify: false); }
            }
            settings.ClickThrough = true; pet.ApplySettings(settings); Pump();
            Check(!NativeMethods.IsWindowVisible(pet.BodyInputHandle), "pet click-through hides only body input layer");
            settings.ClickThrough = false; pet.ApplySettings(settings); Pump();
            Check(NativeMethods.IsWindowVisible(pet.BodyInputHandle), "body input restores when click-through is disabled");
            pet.SetSessionPaused(true); Pump();
            Check(!NativeMethods.IsWindowVisible(pet.BodyInputHandle), "session pause hides body input layer");
            pet.SetSessionPaused(false); Pump();
            Check(NativeMethods.IsWindowVisible(pet.BodyInputHandle), "session resume restores body input layer");
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            if (pet is not null) { pet.RequestExit(); pet.Dispose(); }
            if (underlay is not null)
            {
                if (!underlay.HasExited) { underlay.Kill(); underlay.WaitForExit(3000); }
                underlay.Dispose();
            }
            if (File.Exists(underlayReport)) File.Delete(underlayReport);
        }
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            _metadata = new TraceMetadata(),
            scope = "All 146 unchanged PNG masks, isolated cross-process HWND probes and synthetic captured drag. Cursor never moved.",
            ok = error is null, checks, frameStats, error
        }, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }

    private static (int X, int Y) FindSample(SpriteHitMask mask, SpriteHitPart part, int width, int height)
    {
        for (int y = 3; y < height - 3; y++) for (int x = 3; x < width - 3; x++)
        {
            if (mask.GetScaledPart(x, y, width, height) != part) continue;
            if (Enumerable.Range(-2, 5).All(dy => Enumerable.Range(-2, 5).All(dx => mask.GetScaledPart(x + dx, y + dy, width, height) == part)))
                return (x, y);
        }
        throw new InvalidOperationException("Missing safe sample for " + part);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
}
