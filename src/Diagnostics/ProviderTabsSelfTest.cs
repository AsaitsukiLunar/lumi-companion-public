// Version: 0.3.5-preview
// Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source type: RAW (user tabs request), LOCAL-WORK (synthetic regression inputs and WPF renders).
// Node: Windows / Codex Desktop / Codex. Original source: current Lumi conversation.
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumiCompanion.Animation;
using LumiCompanion.Infrastructure;
using LumiCompanion.Models;
using LumiCompanion.UI;
using Controls = System.Windows.Controls;

namespace LumiCompanion.Diagnostics;

/// <summary>Uses only synthetic quotas and a private temporary settings directory; never opens a browser.</summary>
public static class ProviderTabsSelfTest
{
    public static int Run(string reportPath)
    {
        var checks = new List<string>();
        var renders = new List<string>();
        string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath))!;
        string temporary = Path.Combine(Path.GetTempPath(), "Lumi-provider-tabs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? error = null;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        try
        {
            var store = new ProviderTabsStore(temporary);
            ProviderTabsDocument document = store.Load(out string? problem);
            Check(problem is null && document.Tabs.Count == 3 && !File.Exists(store.FilePath), "default numeric tabs load without writing user config");
            Check(document.Tabs[0].EffectiveDashboardUrl == "https://chatgpt.com/settings/usage" &&
                  document.Tabs[0].DashboardUrl.Length == 0, "Codex built-in dashboard is available without migrating saved configuration");
            Check(document.Tabs[2].EffectiveDashboardUrl == "https://www.workbuddy.cn/profile/plans-usage" &&
                  document.Tabs[2].DashboardUrl.Length == 0, "WorkBuddy official web entry is available without storing an account URL");
            var savedLinks = new ProviderTabsDocument
            {
                Tabs = [new ProviderTab { Id = "codex", Name = "我的Codex", DashboardUrl = "https://example.invalid/custom" }]
            };
            store.Save(savedLinks);
            savedLinks = store.Load(out problem);
            Check(savedLinks.Tabs.Count == 1 && savedLinks.Tabs[0].Name == "我的Codex" &&
                  savedLinks.Tabs[0].EffectiveDashboardUrl == "https://example.invalid/custom",
                "built-in defaults preserve custom URLs, renamed tabs and deleted providers");
            Check(ProviderTabsStore.TryGetDashboardUri("https://example.invalid/usage", out _), "https dashboard links accepted");
            Check(!ProviderTabsStore.TryGetDashboardUri("file:///C:/Windows", out _) &&
                  !ProviderTabsStore.TryGetDashboardUri("javascript:alert(1)", out _) &&
                  !ProviderTabsStore.TryGetDashboardUri("https://user:password@example.invalid/", out _) &&
                  !ProviderTabsStore.TryGetDashboardUri("relative/page", out _), "executable, file, credential-bearing and relative links rejected");
            document.Tabs.Add(new ProviderTab { Name = "自定义 API", DashboardUrl = "https://example.invalid/usage" });
            document.SelectedTabId = document.Tabs[^1].Id;
            store.Save(document);
            document = store.Load(out problem);
            Check(problem is null && document.Tabs.Count == 4 && document.Tabs[^1].Name == "自定义 API" &&
                  document.SelectedTabId == document.Tabs[^1].Id, "custom provider name, link and selected tab persist");
            document.Tabs.RemoveAt(3);
            store.Save(document);
            Check(store.Load(out _).SelectedTabId == "codex" && File.Exists(store.FilePath + ".bak"), "deleting selected custom tab returns to Codex with atomic backup");
            File.WriteAllText(store.FilePath, "invalid config");
            Check(store.Load(out problem).Tabs.Count == 3 && problem is not null &&
                  File.ReadAllText(store.FilePath) == "invalid config", "invalid config preserved without overwrite");
            store.Save(new ProviderTabsDocument());

            DateTimeOffset now = DateTimeOffset.Now;
            var quota = new CodexUsageSnapshot(new CodexUsageWindow(300, 22, now.AddHours(1)), null,
                now.AddMinutes(-12), "合成测试数据；本地快照并非实时官方 API。")
            { SourceLabel = "合成测试快照", IsStale = true };
            UsageBadgeProjection future = UsageBadgeProjection.At(quota, now.AddHours(2));
            Check(!future.HasAnyQuota && future.Status.Contains("重置"), "expired quota becomes unknown without another data refresh");
            Check(!UsageBadgeProjection.At(quota, now.AddDays(2)).HasAnyQuota, "snapshot older than 24 hours is not displayed as current quota");
            Check(UsageBadgeProjection.At(quota with { CapturedAt = now.AddMinutes(-11), IsStale = false }, now).IsStale,
                "freshness age is recomputed while card remains open");

            var catalog = new AnimationCatalog();
            using var companion = new CompanionWindow(catalog, new LumiSettings
            {
                PositionX = SystemParameters.WorkArea.Left + 80,
                PositionY = SystemParameters.WorkArea.Top + 80
            });
            companion.ShowWithoutActivation();
            using var badge = new CodexUsageBadgeWindow(companion, store);
            Check(!badge.IsExpanded && badge.DockWindow.Width == 144 && badge.DockWindow.Height == 38,
                "module starts collapsed to compact provider-and-usage window");
            badge.SetSnapshot(quota);
            Check(badge.IsHitTestVisible && !badge.ContainsScreenPoint(new Point(0, 0)), "card independently accepts input and hidden card has no screen hit region");
            badge.ApplySettings(new LumiSettings { ShowCodexUsage = true });
            badge.SetExpanded(true);
            Pump();
            var card = (FrameworkElement)badge.Content;
            Layout(card, badge.Width, badge.Height);
            var text = Descendants<Controls.TextBlock>(card).Select(block => block.Text).ToList();
            Check(text.Contains("78%") && text.Contains("--"), "valid single quota remains visible while missing quota is unknown");
            Check(text.Any(value => value.Contains("非实时")) && text.Any(value => value.Contains("旧快照")), "local observation and stale timestamp are visible in narrow card");
            Check(Descendants<Controls.ProgressBar>(card).Count(bar => bar.Visibility == Visibility.Visible) == 1,
                "missing quota hides its bar rather than showing zero remaining");
            Check(Descendants<Controls.Button>(card).Single(button => Equals(button.Content, "⋯")).ContextMenu!
                .Items.Cast<Controls.MenuItem>().Single(item => Equals(item.Header, "打开用量页")).IsEnabled,
                "Codex dashboard remains discoverable in the more menu without a crowded header");
            SaveRender(card, badge.Width, badge.Height, Path.Combine(directory, "tabs-1-codex-partial.png"));
            renders.Add("tabs-1-codex-partial.png");

            new WindowInteropHelper(badge).EnsureHandle();
            var editor = new ProviderTabsEditorWindow(badge, new ProviderTabsDocument());
            Check(Descendants<Controls.ListBox>(editor.Content as DependencyObject).Single().Items.Count == 3,
                "provider editor exposes all default tabs");
            Click((DependencyObject)editor.Content, "新增");
            Check(editor.Result.Tabs.Count == 4, "provider editor can add a custom dashboard");
            Controls.TextBox[] fields = Descendants<Controls.TextBox>(editor.Content as DependencyObject).ToArray();
            fields[0].Text = "自定义积分页";
            fields[1].Text = "https://example.invalid/balance";
            Check(editor.Result.Tabs[^1].Name == "自定义积分页" &&
                  editor.Result.Tabs[^1].DashboardUrl == "https://example.invalid/balance", "provider editor captures name and dashboard URL changes");
            Click((DependencyObject)editor.Content, "移除");
            Check(editor.Result.Tabs.Count == 3, "provider editor can remove a custom dashboard");
            editor.Close();

            Click(card, "2");
            Layout(card, badge.Width, badge.Height);
            Check(Descendants<Controls.TextBlock>(card).Any(block => block.Text == "2 · GPT Plus") &&
                  Descendants<Controls.TextBlock>(card).Any(block => block.Text.Contains("尚未自动读取")),
                "GPT Plus tab is explicitly a web entry without fabricated quota");
            Check(!Descendants<Controls.Button>(card).Single(button => Equals(button.Content, "打开用量页")).IsEnabled,
                "unconfigured web entry cannot open an arbitrary page");
            SaveRender(card, badge.Width, badge.Height, Path.Combine(directory, "tabs-2-gpt-plus.png"));
            renders.Add("tabs-2-gpt-plus.png");
            Click(card, "3");
            Layout(card, badge.Width, badge.Height);
            Check(Descendants<Controls.TextBlock>(card).Any(block => block.Text == "3 · WorkBuddy"), "WorkBuddy tab can be selected independently");
            Check(Descendants<Controls.Button>(card).Single(button => Equals(button.Content, "打开用量页")).IsEnabled,
                "WorkBuddy tab enables only an explicit browser action without fabricating credits");
            Check(Descendants<Controls.TextBlock>(card).Any(block => block.Text.Contains("用量页已配置") && block.Text.Contains("点击下方按钮")),
                "configured web entry explains that the user must click to open and no automatic balance was connected");
            SaveRender(card, badge.Width, badge.Height, Path.Combine(directory, "tabs-3-workbuddy.png"));
            renders.Add("tabs-3-workbuddy.png");
            Click(card, "−");
            var dock = (FrameworkElement)badge.DockWindow.Content;
            Layout(dock, 144, 38);
            Check(Descendants<Controls.TextBlock>(dock).Any(block => block.Text == "网页入口已配置"),
                "compact WorkBuddy entry clearly identifies its configured webpage instead of ambiguous unconnected status");
            Check(!badge.IsExpanded && badge.DockWindow.Width == 144, "side card collapses to compact floor window");
            SaveRender(dock, 144, 38, Path.Combine(directory, "tabs-collapsed.png"));
            renders.Add("tabs-collapsed.png");
            Descendants<Controls.Button>(dock).Single(button => button.Content is Controls.StackPanel)
                .RaiseEvent(new RoutedEventArgs(Controls.Button.ClickEvent));
            Check(badge.IsExpanded && badge.Height == 184 && store.Load(out _).SelectedTabId == "workbuddy", "floor switch expands card and remembers selected numeric tab");
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
            catch (IOException) { }
        }
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            _metadata = new TraceMetadata(), scope = "Synthetic quota + private temporary configuration + WPF button events and renders; no browser, account access or normal pet process.",
            ok = error is null, checks, renders, error
        }, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }

    private static void Click(DependencyObject root, string text) => Descendants<Controls.Button>(root)
        .Single(button => Equals(button.Content, text)).RaiseEvent(new RoutedEventArgs(Controls.Button.ClickEvent));

    private static IEnumerable<T> Descendants<T>(DependencyObject? node) where T : DependencyObject
    {
        if (node is null) yield break;
        if (node is T value) yield return value;
        foreach (object child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject dependency)
                foreach (T found in Descendants<T>(dependency)) yield return found;
    }

    private static void Layout(FrameworkElement content, double width, double height)
    {
        content.Measure(new System.Windows.Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
    }

    private static void SaveRender(FrameworkElement content, double width, double height, string path)
    {
        var bitmap = new RenderTargetBitmap((int)width * 2, (int)height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using Stream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Pump()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
}
