// Version: 0.3.19-preview; Generated: 2026-10-30; Updated/Coverage: 2026-11-16 (Asia/Shanghai)
// Source: RAW | user card layout, resizing and stable animation anchor requests; LOCAL-WORK | isolated WPF and native tests.
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LumiCompanion.Animation;
using LumiCompanion.Infrastructure;
using LumiCompanion.Integration;
using LumiCompanion.Models;
using LumiCompanion.Native;
using LumiCompanion.UI;
using Controls = System.Windows.Controls;

namespace LumiCompanion.Diagnostics;

public static class UsageDockSelfTest
{
    public static int Run(string reportPath)
    {
        var checks = new List<string>();
        var renders = new List<string>();
        string? error = null;
        string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(Path.GetTempPath(), "Lumi-dock-test-" + Guid.NewGuid().ToString("N"));
        string underlayReport = Path.Combine(directory, "dock-underlay-" + Guid.NewGuid().ToString("N") + ".json");
        Process? underlay = null;
        CompanionWindow? pet = null;
        CodexUsageBadgeWindow? badge = null;
        TaskStatusBubbleWindow? bubble = null;
        ComfyUiStatusWindow? comfy = null;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks.Add(name); }
        try
        {
            var work = new Rect(-900, -150, 900, 700);
            var switchSize = new System.Windows.Size(144, 38);
            var cardSize = new System.Windows.Size(224, 184);
            foreach (Rect body in new[] { new Rect(-700, 50, 192, 208), new Rect(-890, -140, 192, 208),
                new Rect(-202, 330, 192, 208), new Rect(-890, 330, 192, 208) })
            {
                Rect? dock = UsageDockLayout.FindSwitch(body, work, switchSize, null);
                Check(dock is { } fit && work.Contains(fit) && !fit.IntersectsWith(body), "floor switch stays inside work area without covering character canvas");
                Rect? plannedCard = UsageDockLayout.FindPopover(body, work, dock!.Value, cardSize, null);
                Check(plannedCard is { } pop && work.Contains(pop) && !pop.IntersectsWith(body) &&
                    (pop.Right <= body.Left || pop.Left >= body.Right),
                    "full card remains beside character within work area at monitor edges");
            }
            var centered = new Rect(-500, 30, 192, 208);
            Rect normalCard = UsageDockLayout.FindPopover(centered, work, Rect.Empty, cardSize, null)!.Value;
            Check(normalCard.Right < centered.Left, "full card prefers left of canvas rather than feet");
            Rect edgeCard = UsageDockLayout.FindPopover(new Rect(-895, 30, 192, 208), work, Rect.Empty, cardSize, null)!.Value;
            Check(edgeCard.Left > -703, "left monitor edge uses right-side fallback without covering character");
            Check(UsageDockLayout.FindPopover(centered, work, Rect.Empty, cardSize, normalCard) is { } avoiding && avoiding.Left > centered.Right,
                "full card uses safe right fallback when bubble occupies left candidate");
            var narrowWork = new Rect(0, 0, 330, 400);
            Check(UsageDockLayout.FindPopover(new Rect(70, 70, 192, 208), narrowWork, Rect.Empty, cardSize, null) is null,
                "no safe side placement reports unavailable instead of moving full card below feet");

            var tallCard = new System.Windows.Size(224, 214);
            var cornerPet = new Rect(work.Right - 195, work.Bottom - 211, 192, 208);
            Rect cornerCard = UsageDockLayout.FindPopover(cornerPet, work, Rect.Empty, tallCard, null)!.Value;
            var cornerBubble = new Rect(cornerCard.Right - 4, cornerCard.Top - 30, 178, 38);
            Rect? movedCard = UsageDockLayout.FindPopover(cornerPet, work, Rect.Empty, tallCard, cornerBubble);
            Check(movedCard is { } cardFit && work.Contains(cardFit) && cardFit.Right <= cornerPet.Left &&
                  !cardFit.IntersectsWith(cornerBubble),
                "expanded usage stays visible in the bottom-right corner when the task bubble clips its only side slot");
            Rect cornerComfy = UsageDockLayout.FindComfyUiPanel(cornerPet, work,
                new System.Windows.Size(286, 190), movedCard, cornerBubble)!.Value;
            Check(work.Contains(cornerComfy) && !cornerComfy.IntersectsWith(cornerPet) &&
                  !cornerComfy.IntersectsWith(movedCard!.Value) && !cornerComfy.IntersectsWith(cornerBubble),
                "detailed ComfyUI stays clear of full usage and the task bubble in the bottom-right corner");

            var topWork = new Rect(0, 0, 1920, 1040);
            var topPet = new Rect(904, 0, 192, 208);
            var topQuota = new Rect(672, 0, 224, 214);
            var sideBubble = new Rect(1064, 4, 178, 38);
            Rect? topComfy = UsageDockLayout.FindComfyUiPanel(topPet, topWork, new System.Windows.Size(286, 174),
                topQuota, sideBubble);
            Check(topComfy is { } clear && topWork.Contains(clear) && clear.Left > topPet.Right &&
                  !clear.IntersectsWith(sideBubble) && !clear.IntersectsWith(topQuota),
                "attached ComfyUI avoids the task bubble and quota instead of placing its header underneath");

            var comfySize = new System.Windows.Size(286, 170);
            var rightEdgePet = new Rect(-202, 120, 192, 208);
            Rect leftSideUsage = UsageDockLayout.FindPopover(rightEdgePet, work, Rect.Empty, cardSize, null)!.Value;
            Rect adaptiveComfy = UsageDockLayout.FindComfyUiPanel(rightEdgePet, work, comfySize, leftSideUsage)!.Value;
            Check(work.Contains(adaptiveComfy) && !adaptiveComfy.IntersectsWith(rightEdgePet) &&
                  !adaptiveComfy.IntersectsWith(leftSideUsage),
                "when right-side space is unavailable, ComfyUI finds another safe place without shifting Lumi");
            double fittedLeft = UsageDockLayout.FitPetBesideComfyUi(rightEdgePet, work, comfySize);
            Check(fittedLeft < rightEdgePet.Left &&
                UsageDockLayout.FitPetBesideComfyUi(centered, work, comfySize) == centered.Left,
                "right-side ComfyUI reserves only the horizontal space missing at the right screen edge");
            rightEdgePet.X = fittedLeft;
            Rect rightEdgeComfy = UsageDockLayout.FindComfyUiPanel(rightEdgePet, work, comfySize, null)!.Value;
            Check(work.Contains(rightEdgeComfy) && rightEdgeComfy.Left >= rightEdgePet.Right,
                "ComfyUI stays on the right after fitting a pet near the right edge of a negative-coordinate monitor");
            var leftEdgePet = new Rect(-895, 120, 192, 208);
            foreach (Rect rightSideUsage in new[]
            {
                UsageDockLayout.FindPopover(leftEdgePet, work, Rect.Empty, cardSize, null)!.Value,
                UsageDockLayout.FindSwitch(leftEdgePet, work, switchSize,
                    new Rect(leftEdgePet.Left, leftEdgePet.Bottom + 1, leftEdgePet.Width, switchSize.Height + 16))!.Value
            })
            {
                Rect placed = UsageDockLayout.FindComfyUiPanel(leftEdgePet, work, comfySize, rightSideUsage)!.Value;
                Check(rightSideUsage.Left >= leftEdgePet.Right && work.Contains(placed) &&
                      placed.Left >= leftEdgePet.Right && !placed.IntersectsWith(rightSideUsage),
                    "right-side ComfyUI avoids the Codex usage window when it also has to dock on the right");
            }
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing executable path.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("--hit-test-underlay"); start.ArgumentList.Add(underlayReport);
            underlay = Process.Start(start) ?? throw new InvalidOperationException("Unable to create isolated underlay.");
            var elapsed = Stopwatch.StartNew();
            while (!File.Exists(underlayReport) && elapsed.ElapsedMilliseconds < 10000 && !underlay.HasExited) Thread.Sleep(20);
            Check(File.Exists(underlayReport), "isolated underlying window is available");
            using var data = JsonDocument.Parse(File.ReadAllText(underlayReport));
            IntPtr underlayHandle = new(data.RootElement.GetProperty("hwnd").GetInt64());
            int left = data.RootElement.GetProperty("left").GetInt32(), top = data.RootElement.GetProperty("top").GetInt32();
            var catalog = new AnimationCatalog();
            pet = new CompanionWindow(catalog, new LumiSettings { PetWidth = 192 });
            pet.ShowWithoutActivation();
            DpiScale testDpi = VisualTreeHelper.GetDpi(pet);
            NativeMethods.SetWindowPos(pet.HwndSource!.Handle, IntPtr.Zero, left + (int)(220 * testDpi.DpiScaleX), top + 80, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            pet.SetVisual(AnimationId.Look, 0, "合成开关测试");
            badge = new CodexUsageBadgeWindow(pet, new ProviderTabsStore(temporary));
            DateTimeOffset previewAt = DateTimeOffset.Now;
            badge.SetSnapshot(new CodexUsageSnapshot(new CodexUsageWindow(300, 35, previewAt.AddHours(1)),
                new CodexUsageWindow(10080, 12, previewAt.AddDays(2)), previewAt, "合成测试数据") { SourceLabel = "合成快照" });
            badge.ApplySettings(new LumiSettings { ShowCodexUsage = true });
            bubble = new TaskStatusBubbleWindow(pet);
            badge.SetStatusBubble(bubble);
            bubble.SetTaskState(new AggregateTaskState(AgentTaskState.Running, 1, 0, 1, false));
            Pump(220);
            Check(badge.DockWindow.IsVisible && !badge.IsExpanded && !badge.IsVisible,
                "enabled module initially shows only its floor switch");
            Check(badge.DockWindow.Width == 144 && badge.DockWindow.Height == 38, "compact capsule shows provider, usage and manual expand/hide controls");
            Rect petBefore = Bounds(pet), dockBefore = Bounds(badge.DockWindow);
            Check(!dockBefore.IntersectsWith(petBefore) && !dockBefore.IntersectsWith(Bounds(bubble)), "switch is outside body, cable, canvas and bubble");
            badge.DockWindow.RaiseEvent(new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            Check(!badge.IsExpanded && !badge.IsVisible, "passing over floor switch does not open popover");
            Check(Descendants<Controls.TextBlock>((DependencyObject)badge.DockWindow.Content).Any(text => text.Text == "1 · Codex"),
                "compact floor window identifies selected provider");
            IntPtr compactHit = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = (int)dockBefore.Left + 14, Y = (int)dockBefore.Top + 14 });
            Check(compactHit == new WindowInteropHelper(badge.DockWindow).Handle, "native hit inside compact entry resolves only to its window");
            SaveComposition(Path.Combine(directory, "dock-collapsed.png"), pet, badge.DockWindow, bubble);
            renders.Add("dock-collapsed.png");

            ClickDock(badge);
            Pump(220);
            Check(badge.IsExpanded && badge.IsVisible && badge.Opacity > .99 && !badge.DockWindow.IsVisible,
                "compact click opens full popover and replaces compact state");
            badge.SetExpanded(false); badge.SetExpanded(true); Pump(220);
            Check(badge.IsExpanded && badge.IsVisible && badge.Opacity > .99,
                "rapid close then reopen cancels pending fade-out instead of leaving an invisible open card");
            Rect card = Bounds(badge);
            Check(card.Right <= petBefore.Left && card.Bottom > petBefore.Top && card.Top < petBefore.Bottom,
                "actual expanded WPF card sits to left of character with sufficient space");
            Check(!card.IntersectsWith(petBefore) && !card.IntersectsWith(Bounds(badge.DockWindow)) && !card.IntersectsWith(Bounds(bubble)),
                "expanded popover does not intercept character, floor switch or bubble");
            Check(Bounds(pet) == petBefore, "opening popover does not move or resize character");
            Check(badge.ContainsScreenPoint(new Point(card.Left + 10, card.Top + 10)) &&
                  !badge.ContainsScreenPoint(new Point(dockBefore.Left + 10, dockBefore.Top + 10)), "only current visible usage state accepts input");
            IntPtr dockHit = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = (int)dockBefore.Left + 14, Y = (int)dockBefore.Top + 14 });
            Check(dockHit == underlayHandle, "replaced compact window no longer intercepts its old location");
            foreach (SpriteHitPart part in new[] { SpriteHitPart.Body, SpriteHitPart.Cable, SpriteHitPart.Transparent })
            {
                var sample = Sample(pet.CurrentHitMask!, part, (int)petBefore.Width, (int)petBefore.Height);
                IntPtr hit = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = (int)petBefore.Left + sample.X, Y = (int)petBefore.Top + sample.Y });
                Check(hit == (part == SpriteHitPart.Body ? pet.BodyInputHandle : underlayHandle), $"popover leaves {part} native interaction unchanged");
            }
            SaveComposition(Path.Combine(directory, "dock-expanded.png"), pet, badge.DockWindow, badge, bubble);
            renders.Add("dock-expanded.png");
            badge.RaiseEvent(new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            Pump(2300);
            Check(badge.IsExpanded && badge.IsVisible && !badge.DockWindow.IsVisible,
                "leaving card for more than two seconds does not change manually selected expanded mode");
            var monitorInfo = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(pet.HwndSource!.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST), ref monitorInfo);
            NativeMethods.SetWindowPos(pet.HwndSource.Handle, IntPtr.Zero, monitorInfo.rcWork.Left + 3, (int)petBefore.Top, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Pump(220);
            Check(badge.IsExpanded && badge.IsVisible && Bounds(badge).Left >= Bounds(pet).Right,
                "actual left monitor edge keeps expanded mode and uses right-side fallback");
            SaveComposition(Path.Combine(directory, "dock-edge-right-fallback.png"), pet, badge, bubble);
            renders.Add("dock-edge-right-fallback.png");
            NativeMethods.SetWindowPos(pet.HwndSource.Handle, IntPtr.Zero, (int)petBefore.Left, (int)petBefore.Top, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Pump(220);
            badge.SetSnapshot(CodexUsageSnapshot.Loading()); Pump(220);
            Check(badge.IsExpanded && Bounds(badge) == card, "quota refresh does not shrink or relocate selected expanded card");
            pet.SetSessionPaused(true); Pump(220);
            Check(badge.IsExpanded && !badge.IsVisible && !badge.DockWindow.IsVisible,
                "temporary companion invisibility preserves expanded selection without displaying quota UI");
            pet.SetSessionPaused(false); Pump(220);
            Check(badge.IsExpanded && badge.IsVisible && !badge.DockWindow.IsVisible,
                "restored companion restores previously selected expanded mode");
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                foreach (Window editor in Application.Current.Windows.Cast<Window>().ToArray())
                    if (editor is ProviderTabsEditorWindow) editor.Close();
            }));
            Descendants<Controls.Button>((DependencyObject)badge.Content).Single(button => Equals(button.Content, "⋯"))
                .ContextMenu!.Items.Cast<Controls.MenuItem>().Single(item => Equals(item.Header, "编辑标签…"))
                .RaiseEvent(new RoutedEventArgs(Controls.MenuItem.ClickEvent));
            Pump(2300);
            Check(badge.IsExpanded && badge.IsVisible, "closing tag editor does not schedule an automatic collapse");
            NativeMethods.SetWindowPos(pet.HwndSource!.Handle, IntPtr.Zero, (int)petBefore.Left + 10, (int)petBefore.Top + 7, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Pump(220);
            Check(badge.IsExpanded && badge.IsVisible && Bounds(badge).Right <= Bounds(pet).Left,
                "character movement preserves expanded mode and left anchor");
            NativeMethods.SetWindowPos(pet.HwndSource!.Handle, IntPtr.Zero, (int)petBefore.Left, (int)petBefore.Top, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Pump(220);
            pet.SetVisual(AnimationId.Failed, null, "合成蹲姿", loopOverride: false, startFrameOverride: 4, endFrameOverride: 4);
            Pump(220);
            Check(Bounds(badge.DockWindow) == dockBefore && Bounds(badge) == card,
                "pose change does not shake floor switch or expanded button positions");
            badge.SetExpanded(false);
            Pump(220);
            Check(!badge.IsVisible && !badge.IsExpanded && badge.DockWindow.IsVisible,
                "full card collapses to compact state without disabling module");
            badge.SetSnapshot(new CodexUsageSnapshot(new CodexUsageWindow(300, 15, DateTimeOffset.Now.AddHours(1)), null,
                DateTimeOffset.Now, "合成关闭后刷新") { SourceLabel = "合成快照" });
            ClickDock(badge); Pump(220);
            Check(Descendants<Controls.TextBlock>((DependencyObject)badge.Content).Any(text => text.Text == "85%"),
                "collapsed card still accepts quota updates for next opening");
            DateTimeOffset sampleTime = DateTimeOffset.Now.AddMinutes(-12);
            var observed = new CodexUsageSnapshot(new CodexUsageWindow(300, 15, DateTimeOffset.Now.AddHours(1)), null,
                sampleTime, "合成本地观测") { SourceLabel = "合成快照" };
            DateTimeOffset firstCheck = DateTimeOffset.Now;
            badge.SetSnapshot(observed, firstCheck);
            badge.SetSnapshot(observed, firstCheck.AddSeconds(1));
            Check(badge.LastCheckedAt == firstCheck.AddSeconds(1) && badge.ToolTip.ToString()!.Contains(sampleTime.LocalDateTime.ToString("MM-dd HH:mm")),
                "new local check time is independent of unchanged source sampling time");
            Pump(1100);
            Check(badge.LastCheckedAt == firstCheck.AddSeconds(1) && badge.ToolTip.ToString()!.Contains("不会主动刷新服务器额度"),
                "per-second rendering does not fabricate a new check or server refresh");
            badge.SetSnapshot(CodexUsageSnapshot.Loading());
            Check(badge.LastCheckedAt == firstCheck.AddSeconds(1), "loading presentation is not recorded as a completed local check");
            Descendants<Controls.Button>((DependencyObject)badge.Content).Single(button => Equals(button.Content, "−"))
                .RaiseEvent(new RoutedEventArgs(Controls.Button.ClickEvent));
            Pump(220);
            Check(!badge.IsExpanded && !badge.IsVisible && badge.DockWindow.IsVisible,
                "full card minus control returns to compact state");
            ClickDock(badge); Pump(220);
            badge.RaiseEvent(new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
            Pump(2300);
            Check(badge.IsExpanded && badge.IsVisible && !badge.DockWindow.IsVisible, "expanded mode remains selected until explicit minus or hide");
            bool? requestedVisibility = null;
            badge.ModuleVisibilityRequested += visible =>
            {
                requestedVisibility = visible;
                badge.ApplySettings(new LumiSettings { ShowCodexUsage = visible });
            };
            Descendants<Controls.Button>((DependencyObject)badge.Content).Single(button => Equals(button.Content, "×"))
                .RaiseEvent(new RoutedEventArgs(Controls.Button.ClickEvent));
            Check(requestedVisibility == false && !badge.DockWindow.IsVisible && !badge.IsVisible && pet.IsVisible,
                "expanded close requests module-off for tray sync and fully hides quota UI without hiding pet");
            SaveComposition(Path.Combine(directory, "dock-hidden.png"), pet, bubble);
            renders.Add("dock-hidden.png");
            badge.ApplySettings(new LumiSettings { ShowCodexUsage = true });
            Check(badge.DockWindow.IsVisible && !badge.IsExpanded, "same module toggle restores collapsed entry");
            requestedVisibility = null;
            Descendants<Controls.Button>((DependencyObject)badge.DockWindow.Content).Single(button => Equals(button.Content, "×"))
                .RaiseEvent(new RoutedEventArgs(Controls.Button.ClickEvent));
            Check(requestedVisibility == false && !badge.IsVisible && !badge.DockWindow.IsVisible,
                "compact hide control also requests module-off and leaves no floating entry");
            badge.ApplySettings(new LumiSettings { ShowCodexUsage = true });
            requestedVisibility = null;
            badge.DockWindow.Close();
            Check(requestedVisibility == false && !badge.DockWindow.IsVisible,
                "compact native close follows module-off path instead of hiding only its HWND");
            badge.ApplySettings(new LumiSettings { ShowCodexUsage = true });
            pet.SetSessionPaused(true); Pump(220);
            Check(!badge.DockWindow.IsVisible && !badge.IsVisible, "hidden companion hides entire usage module");
            pet.SetSessionPaused(false); Pump(220);
            Check(badge.DockWindow.IsVisible && !badge.IsExpanded, "resumed companion restores collapsed floor entry");

            var actualWork = new Rect(monitorInfo.rcWork.Left, monitorInfo.rcWork.Top,
                monitorInfo.rcWork.Width, monitorInfo.rcWork.Height);
            NativeMethods.SetWindowPos(pet.HwndSource!.Handle, IntPtr.Zero,
                (int)(actualWork.Right - Bounds(pet).Width - 3),
                (int)(actualWork.Top + (actualWork.Height - Bounds(pet).Height) / 2), 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Pump(220);
            Rect beforeComfyReturn = Bounds(pet);
            comfy = new ComfyUiStatusWindow(pet, new ComfyUiPanelPlacement(Docked: false,
                Left: actualWork.Left + 12, Top: actualWork.Top + 12, ShowNodeSteps: false), badge, bubble);
            comfy.SyncVisibility();
            Pump(220);
            var returnButton = Descendants<Controls.Button>((DependencyObject)comfy.Content)
                .Single(button => Equals(button.Content, "归位"));
            Check(returnButton.IsEnabled && Bounds(pet) == beforeComfyReturn,
                "an undocked ComfyUI window keeps its saved position without moving Lumi");
            returnButton.RaiseEvent(new RoutedEventArgs(Controls.Button.ClickEvent));
            Pump(220);
            Rect afterComfyReturn = Bounds(pet), comfyBounds = Bounds(comfy);
            Check(!returnButton.IsEnabled && afterComfyReturn.Left < beforeComfyReturn.Left &&
                  afterComfyReturn.Top == beforeComfyReturn.Top && actualWork.Contains(comfyBounds) &&
                  comfyBounds.Left >= afterComfyReturn.Right,
                "the actual ComfyUI return button reserves room beside Lumi and attaches on the right");
            Check(badge.DockWindow.IsVisible && !comfyBounds.IntersectsWith(Bounds(badge.DockWindow)),
                "returned ComfyUI panel does not cover the compact Codex usage window");
            ClickDock(badge); Pump(220);
            Check(badge.IsVisible && Bounds(comfy).Left >= Bounds(pet).Right && !Bounds(comfy).IntersectsWith(Bounds(badge)),
                "opening the Codex usage card after ComfyUI returns leaves both windows visible and separate");

            bool movedToEdge = NativeMethods.SetWindowPos(pet.HwndSource.Handle, IntPtr.Zero, (int)beforeComfyReturn.Left,
                (int)beforeComfyReturn.Top, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Pump(220);
            Rect edgePet = Bounds(pet), edgeComfy = Bounds(comfy), edgeBadge = Bounds(badge);
            bool safeEdgePlacement = movedToEdge && edgePet == beforeComfyReturn && badge.IsVisible && comfy.IsVisible &&
                actualWork.Contains(edgeComfy) && !edgeComfy.IntersectsWith(edgePet) && !edgeComfy.IntersectsWith(edgeBadge);
            const string edgeCheck = "moving attached Lumi to the right edge adapts the ComfyUI card without shifting Lumi or covering expanded Codex usage";
            Check(safeEdgePlacement, safeEdgePlacement ? edgeCheck : $"{edgeCheck} [moved={movedToEdge}; requested pet={beforeComfyReturn}; " +
                $"actual pet={edgePet}; ComfyUI={edgeComfy} visible={comfy.IsVisible}; expanded Codex={edgeBadge} visible={badge.IsVisible}; " +
                $"work area={actualWork}; pet overlap={edgeComfy.IntersectsWith(edgePet)}; Codex overlap={edgeComfy.IntersectsWith(edgeBadge)}]");

            int availableLeft = (int)Math.Max(actualWork.Left, afterComfyReturn.Left - 32);
            NativeMethods.SetWindowPos(pet.HwndSource.Handle, IntPtr.Zero, availableLeft,
                (int)afterComfyReturn.Top, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Pump(220);
            Rect resumedPet = Bounds(pet), resumedComfy = Bounds(comfy);
            Check(resumedPet.Left == availableLeft && comfy.IsVisible && actualWork.Contains(resumedComfy) &&
                  resumedComfy.Left >= resumedPet.Right && badge.IsVisible && !resumedComfy.IntersectsWith(Bounds(badge)),
                "the attached ComfyUI card returns to the right automatically when there is room again");

            badge.ApplySettings(new LumiSettings { ShowCodexUsage = true, ShowCodexUsageResetTimes = true });
            Descendants<Controls.Button>((DependencyObject)comfy.Content)
                .Single(button => Equals(button.Content, "模式：队列统计"))
                .RaiseEvent(new RoutedEventArgs(Controls.Button.ClickEvent));
            const string cornerPrompt = "corner-fixture";
            var cornerStatus = new ComfyUiStatusSnapshot(DateTimeOffset.Now, 0, 1, [cornerPrompt], 9, 0, 9, "合成角落测试")
            {
                RunningEta = new(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(180))
            };
            comfy.ApplyStatus(cornerStatus, new ComfyUiMonitorRead(null, new ComfyUiNodeProgress(cornerPrompt, "349", 14, 15)));
            bubble.SetTaskState(new AggregateTaskState(AgentTaskState.Running, 2, 0, 2, false));
            double cornerRight = actualWork.Right - resumedPet.Width - 3;
            double cornerBottom = actualWork.Bottom - resumedPet.Height - 3;
            foreach (Point corner in new[] { new Point(actualWork.Left + 3, actualWork.Top + 3),
                new Point(cornerRight, actualWork.Top + 3), new Point(actualWork.Left + 3, cornerBottom),
                new Point(cornerRight, cornerBottom) })
            {
                NativeMethods.SetWindowPos(pet.HwndSource.Handle, IntPtr.Zero, (int)corner.X, (int)corner.Y, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                Pump(220);
                Rect usageAtCorner = Bounds(badge), comfyAtCorner = Bounds(comfy), bubbleAtCorner = Bounds(bubble);
                Check(badge.IsExpanded && badge.IsVisible && badge.Height == 214 && comfy.IsVisible && bubble.IsVisible &&
                      actualWork.Contains(usageAtCorner) && actualWork.Contains(comfyAtCorner) && actualWork.Contains(bubbleAtCorner) &&
                      !usageAtCorner.IntersectsWith(comfyAtCorner) && !comfyAtCorner.IntersectsWith(Bounds(pet)) &&
                      !usageAtCorner.IntersectsWith(bubbleAtCorner) && !comfyAtCorner.IntersectsWith(bubbleAtCorner),
                    $"full usage, task bubble and detailed ComfyUI stay visible and separate at ({corner.X}, {corner.Y})");
            }
            badge.SetExpanded(false); Pump(220);
            Check(badge.DockWindow.IsVisible && !Bounds(comfy).IntersectsWith(Bounds(badge.DockWindow)),
                "compact usage stays separate from detailed ComfyUI at the bottom-right corner");
            badge.SetExpanded(true); Pump(220);
            Check(badge.IsVisible && !Bounds(comfy).IntersectsWith(Bounds(badge)),
                "reopening full usage at the bottom-right corner moves ComfyUI clear");

            NativeMethods.SetWindowPos(pet.HwndSource.Handle, IntPtr.Zero,
                (int)(actualWork.Left + (actualWork.Width - resumedPet.Width) / 2),
                (int)(actualWork.Top + (actualWork.Height - resumedPet.Height) / 2), 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Pump(220);
            Rect baseBubble = Bounds(bubble), baseUsage = Bounds(badge), baseComfy = Bounds(comfy);
            var panelSettings = new LumiSettings
            {
                ShowCodexUsage = true, ShowCodexUsageResetTimes = true,
                TaskBubbleElevation = 24, CodexUsageElevation = 32, ComfyUiElevation = 40
            };
            ApplyPanelSettings();
            double dpiY = VisualTreeHelper.GetDpi(pet).DpiScaleY;
            Check(Math.Abs(baseBubble.Top - Bounds(bubble).Top - 24 * dpiY) <= 2 &&
                  Math.Abs(baseUsage.Top - Bounds(badge).Top - 32 * dpiY) <= 2 &&
                  Math.Abs(baseComfy.Top - Bounds(comfy).Top - 40 * dpiY) <= 2,
                "each panel follows its own user-set elevation");

            panelSettings.TaskBubbleScalePercent = 120;
            panelSettings.CodexUsageScalePercent = 110;
            panelSettings.ComfyUiScalePercent = 90;
            ApplyPanelSettings();
            Rect zoomedBubble = Bounds(bubble), zoomedUsage = Bounds(badge), zoomedComfy = Bounds(comfy);
            Check(bubble.IsVisible && badge.IsVisible && comfy.IsVisible &&
                  Math.Abs(zoomedBubble.Width - baseBubble.Width * 1.2) <= 2 &&
                  Math.Abs(zoomedUsage.Width - baseUsage.Width * 1.1) <= 2 &&
                  Math.Abs(zoomedComfy.Width - baseComfy.Width * .9) <= 2 &&
                  !zoomedBubble.IntersectsWith(zoomedUsage) && !zoomedBubble.IntersectsWith(zoomedComfy) &&
                  !zoomedUsage.IntersectsWith(zoomedComfy),
                "each panel follows its own user-set scale and keeps clear of the other panels");
            badge.SetExpanded(false); Pump(220);
            Check(badge.DockWindow.IsVisible && Math.Abs(badge.DockWindow.Width - 144 * 1.1) <= 1,
                "the compact usage entry uses the same display scale as the expanded card");
            badge.SetExpanded(true); Pump(220);

            Rect beforeJumpBubble = Bounds(bubble), beforeJumpUsage = Bounds(badge), beforeJumpComfy = Bounds(comfy);
            var jumpMasks = new HashSet<SpriteHitMask>();
            pet.SetVisual(AnimationId.Jumping, null, "合成跳跃浮窗测试", loopOverride: true);
            bool panelsStayedStill = true;
            for (int frame = 0; frame < 6; frame++)
            {
                Pump(110);
                jumpMasks.Add(pet.CurrentHitMask!);
                panelsStayedStill &= bubble.IsVisible && badge.IsVisible && comfy.IsVisible &&
                    Bounds(bubble) == beforeJumpBubble && Bounds(badge) == beforeJumpUsage && Bounds(comfy) == beforeJumpComfy;
            }
            Check(jumpMasks.Count > 1 && panelsStayedStill,
                "all three customized panels stay fixed across multiple visible jump frames");

            Check(pet.TryGetPanelLayout(out var layoutBeforeMove), "shared companion layout is available on screen");
            Vector movement = new(40, 24);
            NativeMethods.SetWindowPos(pet.HwndSource.Handle, IntPtr.Zero,
                (int)(layoutBeforeMove.Canvas.Left + movement.X), (int)(layoutBeforeMove.Canvas.Top + movement.Y), 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Pump(220);
            Check(pet.TryGetPanelLayout(out var layoutAfterMove) &&
                  layoutAfterMove.FootAnchor - layoutBeforeMove.FootAnchor == movement &&
                  Bounds(bubble).Location - beforeJumpBubble.Location == movement &&
                  Bounds(badge).Location - beforeJumpUsage.Location == movement &&
                  Bounds(comfy).Location - beforeJumpComfy.Location == movement,
                "moving the common ground anchor translates all three attached panels by the same amount");

            void ApplyPanelSettings()
            {
                bubble.ApplySettings(panelSettings);
                badge.ApplySettings(panelSettings);
                comfy.ApplySettings(panelSettings);
                Pump(280);
            }

            VerifyComfyUiPerimeter(pet, directory, Check, renders);
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            comfy?.Shutdown();
            bubble?.Dispose(); badge?.Dispose();
            if (pet is not null) { pet.RequestExit(); pet.Dispose(); }
            if (underlay is not null) { if (!underlay.HasExited) { underlay.Kill(); underlay.WaitForExit(3000); } underlay.Dispose(); }
            if (File.Exists(underlayReport)) File.Delete(underlayReport);
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); } catch (IOException) { }
        }
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            _metadata = new TraceMetadata(), scope = "Synthetic quota and node progress, isolated WPF usage and ComfyUI windows and independent native underlay; ComfyUI localhost status is read-only.",
            ok = error is null, checks, renders, error
        }, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }

    private static void VerifyComfyUiPerimeter(CompanionWindow pet, string directory, Action<bool, string> check, List<string> renders)
    {
        var panel = new ComfyUiStatusWindow(pet, new ComfyUiPanelPlacement(Hidden: true));
        try
        {
            const string promptId = "perimeter-fixture";
            var running = new ComfyUiStatusSnapshot(DateTimeOffset.Now, 0, 1, [promptId], 9, 0, 9, "合成测试数据");
            ComfyUiMonitorRead Steps(double value) => new(null, new ComfyUiNodeProgress(promptId, "349", value, 15));
            var content = (FrameworkElement)panel.Content;

            panel.ApplyStatus(running, Steps(1));
            var start = RenderPanel();
            check(Marked(start, .1, 0) && !Marked(start, .8, 0) && !Marked(start, 1, .5) &&
                !Marked(start, .5, 1) && !Marked(start, 0, .5),
                "1 of 15 steps starts at the upper-left and advances along the top edge only");

            panel.ApplyStatus(running, Steps(11));
            var partial = RenderPanel();
            check(Marked(partial, .5, 0) && Marked(partial, 1, .5) && Marked(partial, .65, 1) &&
                !Marked(partial, 0, .5),
                "11 of 15 steps follows the top and right edges onto the bottom edge clockwise");

            panel.ApplyStatus(running, Steps(15));
            var complete = RenderPanel();
            check(Marked(complete, .5, 0) && Marked(complete, 1, .5) && Marked(complete, .5, 1) &&
                Marked(complete, 0, .5) && !HasProgress(complete, new Int32Rect(12, 12,
                    complete.PixelWidth - 24, complete.PixelHeight - 24)) &&
                !Descendants<Controls.ProgressBar>(content).Any(),
                "a complete node lights all four edges without drawing the old progress bar inside the panel");

            panel.ApplyStatus(running, new ComfyUiMonitorRead(null, new ComfyUiNodeProgress("finished-prompt", "349", 15, 15)));
            check(!AnyProgress(RenderPanel()), "a stale prompt cannot keep the progress stroke visible");
            panel.ApplyStatus(running with { Running = 0, RunningPromptIds = [] }, Steps(15));
            check(!AnyProgress(RenderPanel()), "the idle panel clears the progress stroke");

            panel.ApplyStatus(running, Steps(14));
            bool EtaVisible(string text) => Descendants<Controls.TextBlock>(content).Any(label =>
                label.Visibility == Visibility.Visible && label.Text == text);
            check(EtaVisible("本任务预计剩余 暂无估计"),
                "a running job without timing evidence does not show a guessed completion time");
            var estimate = new ComfyUiEtaEstimate(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(50));
            panel.ApplyStatus(running with { RunningEta = estimate }, Steps(14));
            var preview = RenderPanel();
            check(Marked(preview, 0, .7) && !Marked(preview, 0, .1) &&
                Descendants<Controls.TextBlock>(content).Any(text => text.Text == "节点 349　14 / 15 步"),
                "14 of 15 steps lights the left edge upwards and still displays the exact step count");
            check(EtaVisible("本任务预计剩余 约 25–50 秒"),
                "the actual detailed panel shows the job estimate separately from node steps");
            const string previewName = "comfyui-eta-preview.png";
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(preview));
            using (Stream output = File.Create(Path.Combine(directory, previewName))) encoder.Save(output);
            renders.Add(previewName);

            Descendants<Controls.Button>(content).Single(button => Equals(button.Content, "模式：节点步数"))
                .RaiseEvent(new RoutedEventArgs(Controls.Button.ClickEvent));
            check(!AnyProgress(RenderPanel()), "queue-only mode hides the stroke even when valid node steps are available");
            check(!EtaVisible("本任务预计剩余 约 25–50 秒"),
                "queue-only mode hides estimated time");

            RenderTargetBitmap RenderPanel()
            {
                double width = panel.Width;
                content.Measure(new System.Windows.Size(width, double.PositiveInfinity));
                double height = Math.Ceiling(content.DesiredSize.Height);
                content.Arrange(new Rect(0, 0, width, height));
                content.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)(width * 2), (int)(height * 2), 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(content);
                return bitmap;
            }
        }
        finally { panel.Shutdown(); }
    }

    private static bool Marked(BitmapSource bitmap, double x, double y)
    {
        int left = Math.Max(0, (int)Math.Round(x * (bitmap.PixelWidth - 1)) - 6);
        int top = Math.Max(0, (int)Math.Round(y * (bitmap.PixelHeight - 1)) - 6);
        return HasProgress(bitmap, new Int32Rect(left, top,
            Math.Min(13, bitmap.PixelWidth - left), Math.Min(13, bitmap.PixelHeight - top)));
    }

    private static bool AnyProgress(BitmapSource bitmap) =>
        HasProgress(bitmap, new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));

    private static bool HasProgress(BitmapSource bitmap, Int32Rect region)
    {
        int stride = region.Width * 4;
        var pixels = new byte[stride * region.Height];
        bitmap.CopyPixels(region, pixels, stride, 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 255 && Math.Abs(pixels[i + 2] - 91) <= 4 &&
                Math.Abs(pixels[i + 1] - 218) <= 4 && Math.Abs(pixels[i] - 153) <= 4) return true;
        }
        return false;
    }

    private static void ClickDock(CodexUsageBadgeWindow badge) => Descendants<Controls.Button>((DependencyObject)badge.DockWindow.Content)
        .Single(button => button.Content is Controls.StackPanel).RaiseEvent(new RoutedEventArgs(Controls.Button.ClickEvent));
    private static Rect Bounds(Window window) { NativeMethods.GetWindowRect(new WindowInteropHelper(window).Handle, out var r); return new Rect(r.Left, r.Top, r.Width, r.Height); }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T value) yield return value;
        foreach (object child in LogicalTreeHelper.GetChildren(node)) if (child is DependencyObject dependency)
            foreach (T found in Descendants<T>(dependency)) yield return found;
    }
    private static (int X, int Y) Sample(SpriteHitMask mask, SpriteHitPart part, int width, int height)
    {
        for (int y = 3; y < height - 3; y++) for (int x = 3; x < width - 3; x++)
            if (mask.GetScaledPart(x, y, width, height) == part && Enumerable.Range(-2, 5)
                .All(dy => Enumerable.Range(-2, 5).All(dx => mask.GetScaledPart(x + dx, y + dy, width, height) == part))) return (x, y);
        throw new InvalidOperationException("Missing safe synthetic hit sample.");
    }
    private static void SaveComposition(string path, params Window[] windows)
    {
        var visible = windows.Where(window => window.IsVisible).Select(window => (Window: window, Bounds: Bounds(window))).ToArray();
        Rect union = visible.Select(item => item.Bounds).Aggregate(Rect.Empty, (all, bounds) => { all.Union(bounds); return all; });
        union.Inflate(8, 8);
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(UsageControlStyle.Brush(16, 18, 24), null, new Rect(0, 0, union.Width, union.Height));
            foreach (var item in visible)
            {
                var content = (FrameworkElement)item.Window.Content;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * 2), (int)Math.Ceiling(content.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(content);
                context.DrawImage(bitmap, new Rect(item.Bounds.Left - union.Left, item.Bounds.Top - union.Top, item.Bounds.Width, item.Bounds.Height));
            }
        }
        var result = new RenderTargetBitmap((int)Math.Ceiling(union.Width * 2), (int)Math.Ceiling(union.Height * 2), 192, 192, PixelFormats.Pbgra32);
        result.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(result));
        using Stream stream = File.Create(path); encoder.Save(stream);
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false; timer.Start();
        try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
}
