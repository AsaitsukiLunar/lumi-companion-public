/*
Version: 0.3.10-preview
Generated: 2026-10-30 (Asia/Shanghai)
Last updated / coverage: 2026-11-11 (Asia/Shanghai)
Source type: RAW | user persistent completion totals and WorkBuddy history; LOCAL-WORK | implementation
Source: Codex Desktop / lumi-companion / current Lumi conversation (title unverified)
Organizing node: Windows / Codex Desktop / Codex
Original sources: Lumi's Codex completion history window and WorkBuddy completion snapshots.
*/
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LumiCompanion.Models;
using Controls = System.Windows.Controls;

namespace LumiCompanion.UI;

public sealed class CompletionHistoryWindow : Window
{
    private readonly Func<CompletionHistorySnapshot> _read;
    private readonly Func<CompletionHistorySnapshot>? _readWorkBuddy;
    private readonly HistorySection _codex;
    private readonly HistorySection? _workBuddy;
    private readonly DispatcherTimer _timer;

    public CompletionHistoryWindow(Func<CompletionHistorySnapshot> read,
        Func<CompletionHistorySnapshot>? readWorkBuddy = null)
    {
        _read = read;
        _readWorkBuddy = readWorkBuddy;
        Title = "Lumi · 对话完成统计";
        Width = readWorkBuddy is null ? 370 : 650;
        Height = 430;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = UsageControlStyle.Surface;
        Foreground = Brushes.White;
        var panel = new Controls.StackPanel { Margin = new Thickness(22, 16, 22, 16) };
        panel.Children.Add(new Controls.TextBlock { Text = "对话完成统计", FontSize = 18, FontWeight = FontWeights.SemiBold });
        var sources = new Controls.Grid { Margin = new Thickness(0, 14, 0, 0) };
        sources.ColumnDefinitions.Add(new Controls.ColumnDefinition());
        _codex = new HistorySection("Codex");
        sources.Children.Add(_codex.Content);
        if (readWorkBuddy is not null)
        {
            sources.ColumnDefinitions.Add(new Controls.ColumnDefinition { Width = new GridLength(24) });
            sources.ColumnDefinitions.Add(new Controls.ColumnDefinition());
            _workBuddy = new HistorySection("WorkBuddy");
            Controls.Grid.SetColumn(_workBuddy.Content, 2);
            sources.Children.Add(_workBuddy.Content);
        }
        panel.Children.Add(sources);
        Content = new Controls.ScrollViewer { Content = panel, VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Render();
        Closed += (_, _) => _timer.Stop();
        Loaded += (_, _) => { Render(); _timer.Start(); };
        Render();
    }

    private void Render()
    {
        _codex.Render(_read(), "\n本批气泡确认不会清除累计。关闭“保存本地统计”会停止新增记录。");
        if (_readWorkBuddy is not null) _workBuddy!.Render(_readWorkBuddy());
    }

    private sealed class HistorySection
    {
        private readonly Controls.TextBlock _counts = new() { FontSize = 15, LineHeight = 28, Margin = new Thickness(0, 7, 0, 8) };
        private readonly Controls.TextBlock _status = new() { FontSize = 11, Foreground = UsageControlStyle.Muted,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        private readonly Controls.TextBlock _recent = new() { FontSize = 11, Foreground = UsageControlStyle.Muted, LineHeight = 18 };

        public Controls.StackPanel Content { get; } = new();

        public HistorySection(string title)
        {
            Content.Children.Add(new Controls.TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            Content.Children.Add(_counts);
            Content.Children.Add(_status);
            Content.Children.Add(new Controls.TextBlock { Text = "最近完成（最多 10 次）", FontSize = 12, Margin = new Thickness(0, 0, 0, 5) });
            Content.Children.Add(_recent);
        }

        public void Render(CompletionHistorySnapshot snapshot, string note = "")
        {
            static string Count(int? value) => value?.ToString() ?? "--";
            _counts.Text = $"本次启动　{Count(snapshot.ThisLaunch)} 回合\n今日完成　{Count(snapshot.Today)} 回合\n累计完成　{Count(snapshot.Total)} 回合";
            _status.Text = snapshot.Status + note;
            _recent.Text = !snapshot.Available ? "暂不可用" : snapshot.Recent.Count == 0 ? "尚无记录" :
                string.Join("\n", snapshot.Recent.Select(entry => $"{entry.EventAt.LocalDateTime:MM-dd HH:mm:ss}　回合结束"));
        }
    }
}
