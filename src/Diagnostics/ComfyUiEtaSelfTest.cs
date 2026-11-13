// Version: 0.3.15-preview | Generated/Updated/Coverage: 2026-11-13 (Asia/Shanghai)
// Original sources: RAW | user ETA request and ComfyUI execution event format.
// Organizing node: LOCAL-WORK | Windows / Codex Desktop / Lumi Companion / Codex.
using System.Net;
using System.Net.Http;
using System.Text.Json;
using LumiCompanion.Infrastructure;
using LumiCompanion.Models;

namespace LumiCompanion.Diagnostics;

internal static class ComfyUiEtaSelfTest
{
    public static void Run()
    {
        var clock = new TestClock();
        using var handler = new ComfyUiFixture();
        using var service = new ComfyUiStatusService(handler, clock);
        ComfyUiStatusSnapshot Read() => service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();

        handler.Running = ["first"];
        Check(Read() is { Running: 1, Pending: 1, RunningEta: null },
            "a running job without usable history has no ETA");

        const long timestamp = 1_790_000_000_000;
        handler.History = new()
        {
            ["old-outlier"] = Job(timestamp - 10_000_000, timestamp - 100_000),
            ["failed"] = Job(timestamp, timestamp + 100_000, success: false),
            ["bad-end"] = Job(timestamp + 30_000, timestamp + 20_000),
            ["missing-start"] = Job(null, timestamp + 35_000),
        };
        for (int index = 0; index < 20; index++)
        {
            // History order is not relied on. The twenty newest valid samples have
            // durations 10, 20, ..., 200 seconds, so their mean is 105 and P75 is 160.
            long end = timestamp + (20 - index) * 1000;
            handler.History[$"sample-{index}"] = Job(end - (index + 1) * 10_000, end);
        }
        clock.Advance(5);
        ComfyUiStatusSnapshot active = Read();
        Check(active is { Running: 1, Pending: 1, Succeeded: 23, Failed: 1, SavedOutputs: 1 } &&
            Matches(active, 100, 155),
            "ETA uses real start/success intervals of the last twenty valid runs and excludes queue age");

        handler.QueueUnavailable = true;
        clock.Advance(5);
        Check(Read() is { Running: null, RunningEta: null }, "unreadable queue suppresses ETA");
        handler.QueueUnavailable = false;
        clock.Advance(5);
        Check(Matches(Read(), 90, 145), "a transient queue read failure preserves elapsed execution observation");

        handler.HistoryUnavailable = true;
        clock.Advance(5);
        Check(Read() is { Running: 1, RunningEta: null }, "unreadable history suppresses ETA without hiding queue counts");
        handler.HistoryUnavailable = false;
        clock.Advance(5);
        Check(Matches(Read(), 80, 135), "history recovery does not reset the running clock");

        handler.Running = ["first", "second"];
        Check(Read() is { Running: 2, RunningEta: null }, "parallel jobs have no shared ETA");
        clock.Advance(5);
        handler.Running = ["second"];
        Check(Matches(Read(), 100, 155), "a different job keeps only its own elapsed observation");

        handler.Running = [];
        Check(Read() is { Running: 0, RunningEta: null }, "idle clears ETA");
        clock.Advance(5);
        handler.Running = ["second"];
        Check(Matches(Read(), 105, 160), "a job returning after a valid queue removal starts a new observation");
        clock.Advance(200);
        Check(Matches(Read(), 0, 0), "elapsed time exceeding the range produces a nonnegative overdue ETA");
    }

    private static bool Matches(ComfyUiStatusSnapshot snapshot, double lower, double upper) =>
        snapshot.RunningEta is { } eta && eta.Lower == TimeSpan.FromSeconds(lower) && eta.Upper == TimeSpan.FromSeconds(upper);

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private static object Job(long? start, long end, bool success = true)
    {
        var messages = new List<object>();
        if (start is { } executionStart)
            messages.Add(new object[] { "execution_start", new { timestamp = executionStart } });
        messages.Add(new object[] { success ? "execution_success" : "execution_error", new { timestamp = end } });
        return new
        {
            status = new { status_str = success ? "success" : "error", messages },
            outputs = new { preview = new { filename = "synthetic.png", subfolder = "", type = "output" } }
        };
    }

    private sealed class ComfyUiFixture : HttpMessageHandler
    {
        public string[] Running { get; set; } = [];
        public Dictionary<string, object> History { get; set; } = [];
        public bool QueueUnavailable { get; set; }
        public bool HistoryUnavailable { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object? response = request.RequestUri?.AbsolutePath switch
            {
                "/queue" when !QueueUnavailable => new
                {
                    queue_running = Running.Select(id => new object[] { 0, id, new { }, new { create_time = 1L } }),
                    queue_pending = new[] { new object[] { 1, "queued" } }
                },
                "/history" when !HistoryUnavailable => History,
                _ => null
            };
            return Task.FromResult(response is null
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response)) });
        }
    }

    private sealed class TestClock : TimeProvider
    {
        private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        private TimeSpan _elapsed;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => Start + _elapsed;
        public override long GetTimestamp() => _elapsed.Ticks;
        public void Advance(double seconds) => _elapsed += TimeSpan.FromSeconds(seconds);
    }
}
