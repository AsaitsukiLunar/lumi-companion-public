/*
Version: 0.3.10-preview
Generated / updated / coverage: 2026-11-11 (Asia/Shanghai)
Source type: LOCAL-WORK | synthetic WorkBuddy bridge completions
Source: Codex Desktop / lumi-companion / current Lumi conversation (title unverified)
Organizing node: Windows / Codex Desktop / Codex
Original sources: Lumi completion history and verified WorkBuddy event contract.
*/
using LumiCompanion.Infrastructure;
using LumiCompanion.Integration;

namespace LumiCompanion.Diagnostics;

public static class WorkBuddyCompletionHistorySelfTest
{
    public static void RunChecks(Action<bool, string> check)
    {
        var clock = new Clock();
        var history = new WorkBuddyCompletionHistory(true, clock);
        check(history.Read() is { Available: false, Total: null }, "WorkBuddy missing source has no numeric counts");

        var previous = Event("previous", clock.GetUtcNow().AddDays(-1));
        clock.Advance(TimeSpan.FromSeconds(10));
        var current = Event("current", clock.GetUtcNow());
        var repeated = current with { StateVersion = "duplicate", EventAt = current.EventAt.AddSeconds(1) };
        clock.Advance(TimeSpan.FromSeconds(1));
        var foreign = current with { Source = LocalCodexTaskSource.SourceId, TurnId = "foreign" };
        var invalid = current with { TurnId = "" };
        var future = Event("future", clock.GetUtcNow().AddMinutes(1));
        ObservedCompletionEvent[] log = [previous, repeated, current, foreign, invalid, future];
        history.Apply("workbuddy-a", log);
        check(history.Read() is { Available: true, ThisLaunch: 1, Today: 1, Total: 2 } counts &&
            counts.Recent[0].EventAt == current.EventAt && counts.Recent[0].EventKey.Length == 64,
            "WorkBuddy uses the first completion timestamp per turn and ignores foreign, invalid and future events");

        history.Apply("workbuddy-a", log);
        check(history.Read().Total == 2, "re-reading the same WorkBuddy log does not recount turns");
        var restarted = new WorkBuddyCompletionHistory(true, clock);
        restarted.Apply("workbuddy-a", log);
        check(restarted.Read() is { ThisLaunch: 0, Today: 1, Total: 2 },
            "WorkBuddy reprojects historical completions on restart without a Lumi ledger");

        clock.Advance(TimeSpan.FromMinutes(2));
        history.Apply("workbuddy-a", log);
        check(history.Read() is { ThisLaunch: 2, Today: 1, Total: 3 },
            "WorkBuddy counts the current local day and retries previously future events");

        history.Apply("workbuddy-b", [current]);
        check(history.Read() is { Today: 0, Total: 1 }, "WorkBuddy does not mix different source scopes");
        history.SetObservationPaused(true);
        history.Apply("workbuddy-a", log);
        check(history.Read() is { Enabled: true, Available: false, Total: null },
            "paused WorkBuddy observation hides all counts");
        history.SetObservationPaused(false);
        check(!history.Read().Available, "WorkBuddy resume waits for a current source read");
        history.Apply("workbuddy-a", log);
        check(history.Read().Total == 3, "WorkBuddy resume restores persisted completions");

        history.SetEnabled(false);
        check(history.Read() is { Enabled: false, Available: false, Today: null, ThisLaunch: null, Total: null },
            "disabling local statistics hides WorkBuddy counts");
        history.SetEnabled(true);
        check(!history.Read().Available, "re-enabling WorkBuddy statistics waits for a current source read");
        history.Apply("workbuddy-a", []);
        check(history.Read() is { Available: true, Total: 0 }, "empty valid WorkBuddy log shows zero");
        history.SetSourceUnavailable();
        check(history.Read() is { Available: false, Total: null }, "unavailable WorkBuddy log does not show zero");
    }

    private static ObservedCompletionEvent Event(string turn, DateTimeOffset at) =>
        new(LocalWorkBuddyTaskSource.SourceId, "synthetic-chat", turn, "version-" + turn, at);

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-11-11T15:59:30Z");
        private static readonly TimeZoneInfo TestZone = TimeZoneInfo.CreateCustomTimeZone(
            "Test+08", TimeSpan.FromHours(8), "Test+08", "Test+08");

        public override TimeZoneInfo LocalTimeZone => TestZone;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
