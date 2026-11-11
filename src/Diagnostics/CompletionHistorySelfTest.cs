// Version: 0.3.7-preview; Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source: LOCAL-WORK | synthetic turn events, temporary private ledger and actual WPF render / completion_ledger.
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LumiCompanion.Infrastructure;
using LumiCompanion.Integration;
using LumiCompanion.Models;
using LumiCompanion.UI;

namespace LumiCompanion.Diagnostics;

public static class CompletionHistorySelfTest
{
    public static int Run(string reportPath)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "Lumi-completion-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        var checks = new List<string>(); string? error = null;
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); checks.Add(text); }
        try
        {
            WorkBuddyCompletionHistorySelfTest.RunChecks(Check);
            var clock = new Clock();
            var zone = TimeZoneInfo.CreateCustomTimeZone("Test+08", TimeSpan.FromHours(8), "Test+08", "Test+08");
            var service = new CompletionHistoryService(temporary, true, clock, zone);
            LocalCodexTaskReadResult Result(string scope, params ObservedCompletionEvent[] events) =>
                new([], true, scope, "synthetic source", 1, 1) { CompletionEvents = events };
            ObservedCompletionEvent Event(string turn, DateTimeOffset? at = null, string chat = "synthetic-chat", string? version = null) =>
                new(LocalCodexTaskSource.SourceId, chat, turn, version ?? turn, at ?? clock.GetUtcNow());
            Check(!service.Read().Available && service.Read().Total is null, "missing source is unavailable rather than zero");
            service.Apply(Result("account-A", Event("historical", clock.GetUtcNow().AddSeconds(-1))));
            Check(service.Read().Total == 0 && !File.Exists(service.FilePath), "startup historical completion does not create or backfill a ledger");
            clock.Advance(TimeSpan.FromSeconds(1));
            var first = Event("turn-1"); service.Apply(Result("account-A", first, first));
            Check(service.Read() is { Total: 1, Today: 1, ThisLaunch: 1 }, "same-batch duplicate completion writes one current-account round");
            service.Apply(Result("account-A", first));
            Check(service.Read().Total == 1, "unchanged tail replay does not count twice");
            clock.Advance(TimeSpan.FromSeconds(1));
            service.Apply(Result("account-A", Event("turn-1", version: "changed-time-version")));
            Check(service.Read().Total == 1, "same chat/turn with a later timestamp or state version remains one round");
            var second = Event("turn-2"); var third = Event("turn-3");
            service.Apply(Result("account-A", second, third));
            Check(service.Read() is { Total: 3, ThisLaunch: 3 }, "different rounds in one chat and polling interval each count once");
            Check(service.Read().Recent.All(entry => entry.EventAt == first.EventAt || entry.EventAt == second.EventAt),
                "recorded timestamps are original event timestamps");
            string ledger = File.ReadAllText(service.FilePath);
            Check(!ledger.Contains("account-A") && !ledger.Contains("synthetic-chat") && !ledger.Contains("turn-1"),
                "ledger contains only hashed identity and event keys, not raw account/chat/turn identifiers");
            var restarted = new CompletionHistoryService(temporary, true, clock, zone);
            restarted.Apply(Result("account-A", first, second, third));
            Check(restarted.Read() is { Total: 3, ThisLaunch: 0 }, "restart preserves cumulative counts without recounting replayed completions");
            clock.Advance(TimeSpan.FromSeconds(1)); restarted.Apply(Result("account-A", Event("turn-4")));
            Check(restarted.Read() is { Total: 4, ThisLaunch: 1 }, "restart counts a genuinely new round for this launch");
            restarted.Apply(Result("account-B", Event("turn-4")));
            Check(restarted.Read() is { Total: 1, ThisLaunch: 1 }, "account switch isolates totals even with identical chat/turn identifiers");
            restarted.Apply(Result("account-A"));
            Check(restarted.Read().Total == 4, "returning to original account restores its separate total");
            restarted.Apply(new([], false, null, "offline", 0, 0));
            Check(!restarted.Read().Available && restarted.Read().Total is null, "source loss hides unverified current totals and preserves disk history");
            restarted.Apply(Result("account-A"));
            restarted.SetEnabled(false); clock.Advance(TimeSpan.FromSeconds(1)); var disabled = Event("disabled-turn");
            restarted.Apply(Result("account-A", disabled));
            Check(restarted.Read() is { Enabled: false, Total: 4 }, "SaveLocalStats off retains history but writes no new completion");
            clock.Advance(TimeSpan.FromSeconds(1)); restarted.SetEnabled(true);
            restarted.Apply(Result("account-A", disabled));
            Check(restarted.Read().Total == 4, "re-enabling does not backfill rounds from the disabled interval");
            restarted.SetObservationPaused(true); clock.Advance(TimeSpan.FromSeconds(1)); var paused = Event("paused-turn");
            restarted.Apply(Result("account-A", paused));
            Check(!restarted.Read().Available, "paused observation is unavailable and does not record");
            clock.Advance(TimeSpan.FromSeconds(1)); restarted.SetObservationPaused(false);
            restarted.Apply(Result("account-A", paused));
            Check(restarted.Read().Total == 4, "resume does not backfill rounds from paused observation");
            restarted.Apply(Result("account-A", Event("future", clock.GetUtcNow().AddSeconds(1))));
            Check(restarted.Read().Total == 4, "future event timestamp is not counted prematurely");
            clock.Advance(TimeSpan.FromHours(2));
            restarted.Apply(Result("account-A", Event("next-local-day")));
            Check(restarted.Read() is { Total: 5, Today: 1, ThisLaunch: 2 }, "today uses original event time in the selected timezone across local midnight");
            new StorageService(temporary).PruneHistory(1);
            new StorageService(temporary).DeleteHistoryAndLogs();
            Check(File.Exists(restarted.FilePath), "ordinary event retention and clearing do not erase persistent completion ledger");
            var other = Event("other-source") with { Source = "verified-other" };
            restarted.ApplyVerified("verified-other", "account-A", [other]);
            Check(restarted.Read().Total == 1, "verified adapter entry namespaces scope and turn keys by source");
            restarted.Apply(Result("account-A"));
            Check(restarted.Read().Total == 5, "another source cannot merge into Codex completion totals");

            var workBuddy = new WorkBuddyCompletionHistory(true, clock);
            workBuddy.Apply("fixture-workspace", [Event("fixture-workbuddy-turn") with
                { Source = LocalWorkBuddyTaskSource.SourceId }]);
            var imageWindow = new CompletionHistoryWindow(restarted.Read, workBuddy.Read) { ShowActivated = false };
            var content = (FrameworkElement)imageWindow.Content;
            content.Measure(new System.Windows.Size(650, 430)); content.Arrange(new Rect(0, 0, 650, 430)); content.UpdateLayout();
            var renderedText = TextIn(content).ToArray();
            Check(renderedText.Contains("Codex") && renderedText.Contains("WorkBuddy") &&
                renderedText.Any(line => line.Contains("累计完成　5 回合")) &&
                renderedText.Any(line => line.Contains("累计完成　1 回合")),
                "actual WPF stats window keeps Codex and WorkBuddy totals in separate sections");
            var bitmap = new RenderTargetBitmap(1300, 860, 192, 192, PixelFormats.Pbgra32); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "completion-history.png"))) encoder.Save(output);
            imageWindow.Close();
            Check(bitmap.PixelWidth == 1300, "actual WPF stats window renders current-launch/day/total and recent records");
            string preserved = File.ReadAllText(restarted.FilePath) + "{partial";
            File.WriteAllText(restarted.FilePath, preserved);
            restarted.Apply(Result("account-A", Event("after-external-change")));
            Check(!restarted.Read().Available && File.ReadAllText(restarted.FilePath) == preserved,
                "external truncate/replacement/append is detected and original changed file is not written over");
            var corrupted = new CompletionHistoryService(temporary, true, clock, zone);
            corrupted.Apply(Result("account-A", Event("after-corrupt-restart")));
            Check(!corrupted.Read().Available && corrupted.Read().Total is null && File.ReadAllText(corrupted.FilePath) == preserved,
                "corrupt startup ledger is preserved and reported unavailable, never silently reset to zero");
            string noNewlineDirectory = Path.Combine(temporary, "no-newline"); Directory.CreateDirectory(noNewlineDirectory);
            string noNewlinePath = Path.Combine(noNewlineDirectory, "completion-history.jsonl");
            string noNewline = ledger.TrimEnd('\r', '\n'); File.WriteAllText(noNewlinePath, noNewline);
            var incomplete = new CompletionHistoryService(noNewlineDirectory, true, clock, zone);
            incomplete.Apply(Result("account-A", Event("must-not-join-json")));
            Check(!incomplete.Read().Available && File.ReadAllText(noNewlinePath) == noNewline,
                "valid final JSON without newline is preserved and rejected rather than concatenated with the next record");
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            string full = Path.GetFullPath(temporary);
            string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "Lumi-completion-test-";
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) Directory.Delete(full, true);
        }
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { _metadata = new TraceMetadata(),
            scope = "Synthetic verified completions and isolated temporary ledgers; no real account reads, chat content or normal pet startup.",
            ok = error is null, checks, error }, new JsonSerializerOptions { WriteIndented = true }));
        return error is null ? 0 : 1;
    }

    private static IEnumerable<string> TextIn(DependencyObject node)
    {
        if (node is System.Windows.Controls.TextBlock text) yield return text.Text;
        foreach (object child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject element)
                foreach (string label in TextIn(element)) yield return label;
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-10-30T15:00:00Z");
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
