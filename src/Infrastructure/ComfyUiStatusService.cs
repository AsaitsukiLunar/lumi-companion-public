/*
Traceability metadata
Version: 0.3.15-preview
Generated: 2026-11-10 (Asia/Shanghai)
Last updated: 2026-11-13
Content coverage through: 2026-11-13
Source type: RAW | user request for local ComfyUI status and ETA; LOCAL-WORK | read-only HTTP implementation
Source: Codex Desktop / lumi-companion / independent Lumi companion chat / Codex
Organizing node: Windows / Codex Desktop / ComfyUI HTTP status subtask
Original sources: user requirements and ComfyUI's documented server routes and execution history.
https://docs.comfy.org/development/comfyui-server/comms_routes
https://github.com/Comfy-Org/ComfyUI/blob/master/execution.py
https://github.com/Comfy-Org/ComfyUI_frontend/blob/v1.53.10/src/components/queue/job/useQueueEstimates.ts
*/

using System.Net.Http;
using System.Text.Json;
using LumiCompanion.Models;

namespace LumiCompanion.Infrastructure;

public sealed class ComfyUiStatusService : IDisposable
{
    public const int HistoryLimit = 100;
    private const int EtaHistoryLimit = 20;

    private static readonly Uri ServerAddress = new("http://127.0.0.1:8188/");
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);
    private readonly HttpClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly object _queueGate = new();
    private readonly Dictionary<string, long> _runningSince = new(StringComparer.Ordinal);
    private long _queueReadSequence;
    private long _lastObservedQueueRead;

    public ComfyUiStatusService(HttpMessageHandler? handler = null, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _client = new HttpClient(handler ?? new HttpClientHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            UseCookies = false
        })
        {
            BaseAddress = ServerAddress,
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 32 * 1024 * 1024
        };
    }

    public async Task<ComfyUiStatusSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReadTimeout);

        var queueTask = ReadQueueAsync(deadline.Token, cancellationToken);
        var historyTask = ReadEndpointAsync($"history?max_items={HistoryLimit}", ParseHistory,
            deadline.Token, cancellationToken);
        await Task.WhenAll(queueTask, historyTask).ConfigureAwait(false);

        var (queue, runningSince) = await queueTask.ConfigureAwait(false);
        var history = await historyTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return new ComfyUiStatusSnapshot(
            _timeProvider.GetLocalNow(), queue?.Pending, queue?.Running, queue?.RunningPromptIds ?? [],
            history?.Succeeded, history?.Failed, history?.SavedOutputs, DescribeStatus(queue, history))
        {
            RunningEta = EstimateRemaining(runningSince, history?.Durations)
        };
    }

    private async Task<(QueueRead? Queue, long? RunningSince)> ReadQueueAsync(
        CancellationToken deadlineToken, CancellationToken callerToken)
    {
        long sequence = Interlocked.Increment(ref _queueReadSequence);
        var queue = await ReadEndpointAsync("queue", ParseQueue, deadlineToken, callerToken).ConfigureAwait(false);
        callerToken.ThrowIfCancellationRequested();
        if (queue is null) return (null, null);

        lock (_queueGate)
        {
            if (sequence <= _lastObservedQueueRead) return (queue, null);
            _lastObservedQueueRead = sequence;
            var active = queue.RunningPromptIds.ToHashSet(StringComparer.Ordinal);
            foreach (string id in _runningSince.Keys.Where(id => !active.Contains(id)).ToArray())
                _runningSince.Remove(id);
            long observedAt = _timeProvider.GetTimestamp();
            foreach (string id in active)
                _runningSince.TryAdd(id, observedAt);

            return (queue, queue.Running == 1 && queue.RunningPromptIds.Count == 1
                ? _runningSince[queue.RunningPromptIds[0]]
                : null);
        }
    }

    private ComfyUiEtaEstimate? EstimateRemaining(long? runningSince, IReadOnlyList<ExecutionDuration>? durations)
    {
        if (runningSince is null || durations is not { Count: > 0 }) return null;

        // ComfyUI estimates a job's remaining time from recent execution durations.
        var sorted = durations.OrderByDescending(duration => duration.EndedAt)
            .Take(EtaHistoryLimit).Select(duration => duration.Seconds).Order().ToArray();
        double mean = sorted.Average();
        double percentile75 = sorted[sorted.Length * 3 / 4];
        double elapsed = Math.Max(0, _timeProvider.GetElapsedTime(runningSince.Value).TotalSeconds);
        return new(
            TimeSpan.FromSeconds(Math.Max(0, Math.Min(mean, percentile75) - elapsed)),
            TimeSpan.FromSeconds(Math.Max(0, Math.Max(mean, percentile75) - elapsed)));
    }

    public void Dispose() => _client.Dispose();

    public async Task<ComfyUiMonitorRead?> ReadMonitorAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReadTimeout);
        return await ReadEndpointAsync("lumi/monitor", ParseMonitor, deadline.Token, cancellationToken)
            .ConfigureAwait(false);
    }

    private static ComfyUiMonitorRead? ParseMonitor(JsonElement root, CancellationToken cancellationToken)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("progress", out var data))
            return null;

        ComfyUiNodeProgress? progress = null;
        if (data.ValueKind == JsonValueKind.Object &&
            ReadString(data, "prompt_id") is { Length: > 0 } promptId &&
            data.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number &&
            data.TryGetProperty("max", out var maximum) && maximum.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out double current) && maximum.TryGetDouble(out double total) &&
            double.IsFinite(current) && double.IsFinite(total) && total > 0 && current >= 0 && current <= total)
        {
            progress = new ComfyUiNodeProgress(promptId, ReadString(data, "node"), current, total);
        }

        return new ComfyUiMonitorRead(ReadString(root, "output_directory"), progress);
    }

    private async Task<T?> ReadEndpointAsync<T>(
        string path, Func<JsonElement, CancellationToken, T?> parse,
        CancellationToken deadlineToken, CancellationToken callerToken)
        where T : class
    {
        try
        {
            // Use bounded response buffering and the same deadline for both reads and parsing.
            using var response = await _client.GetAsync(path, deadlineToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            using var stream = await response.Content.ReadAsStreamAsync(deadlineToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: deadlineToken)
                .ConfigureAwait(false);
            return parse(document.RootElement, deadlineToken);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        {
            return null;
        }
    }

    private static QueueRead? ParseQueue(JsonElement root, CancellationToken cancellationToken)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("queue_pending", out var pending) || pending.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("queue_running", out var running) || running.ValueKind != JsonValueKind.Array)
            return null;

        var promptIds = new List<string>();
        foreach (var entry in running.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.ValueKind == JsonValueKind.Array && entry.GetArrayLength() > 1 &&
                entry[1].ValueKind == JsonValueKind.String && entry[1].GetString() is { } id &&
                !string.IsNullOrWhiteSpace(id))
                promptIds.Add(id);
        }

        return new QueueRead(pending.GetArrayLength(), running.GetArrayLength(), promptIds.AsReadOnly());
    }

    private static HistoryRead? ParseHistory(JsonElement root, CancellationToken cancellationToken)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        int succeeded = 0;
        int failed = 0;
        var savedOutputs = new HashSet<(string Subfolder, string Filename)>();
        var durations = new List<ExecutionDuration>();

        foreach (var entry in root.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var task = entry.Value;
            if (task.ValueKind != JsonValueKind.Object)
                return null;

            switch (ReadOutcome(task))
            {
                case ExecutionOutcome.Failed:
                    failed++;
                    break;
                case ExecutionOutcome.Succeeded:
                    succeeded++;
                    if (ReadExecutionDuration(task, cancellationToken) is { } duration)
                        durations.Add(duration);
                    if (task.TryGetProperty("outputs", out var outputs))
                        CollectSavedOutputs(outputs, savedOutputs, cancellationToken);
                    break;
            }
        }

        return new HistoryRead(succeeded, failed, savedOutputs.Count, durations);
    }

    private static ExecutionDuration? ReadExecutionDuration(JsonElement task, CancellationToken cancellationToken)
    {
        if (!task.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object ||
            !status.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            return null;

        long startedAt = 0;
        foreach (var message in messages.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (message.ValueKind != JsonValueKind.Array || message.GetArrayLength() < 2 ||
                message[0].ValueKind != JsonValueKind.String || message[1].ValueKind != JsonValueKind.Object ||
                !message[1].TryGetProperty("timestamp", out var timestamp) || timestamp.ValueKind != JsonValueKind.Number ||
                !timestamp.TryGetInt64(out long at) || at <= 0 || at > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
                continue;

            switch (message[0].GetString())
            {
                case "execution_start":
                    startedAt = at;
                    break;
                case "execution_success" when startedAt > 0 && at > startedAt:
                    return new(at, (at - startedAt) / 1000.0);
            }
        }

        return null;
    }

    private static ExecutionOutcome ReadOutcome(JsonElement task)
    {
        if (!task.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object)
            return ExecutionOutcome.Unknown;

        switch (ReadString(status, "status_str"))
        {
            case "error":
                return ExecutionOutcome.Failed;
            case "success":
                return ExecutionOutcome.Succeeded;
        }

        if (status.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
        {
            foreach (var message in messages.EnumerateArray())
            {
                if (message.ValueKind != JsonValueKind.Array || message.GetArrayLength() == 0 ||
                    message[0].ValueKind != JsonValueKind.String)
                    continue;

                switch (message[0].GetString())
                {
                    case "execution_error":
                    case "execution_interrupted":
                        return ExecutionOutcome.Failed;
                    case "execution_success":
                        return ExecutionOutcome.Succeeded;
                }
            }
        }

        return ExecutionOutcome.Unknown;
    }

    private static void CollectSavedOutputs(
        JsonElement value, HashSet<(string Subfolder, string Filename)> savedOutputs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                if (ReadString(value, "type") == "output" &&
                    ReadString(value, "filename") is { } filename && !string.IsNullOrWhiteSpace(filename) &&
                    ReadString(value, "subfolder") is { } subfolder)
                {
                    savedOutputs.Add((subfolder, filename));
                    return;
                }

                foreach (var child in value.EnumerateObject())
                    CollectSavedOutputs(child.Value, savedOutputs, cancellationToken);
                break;
            case JsonValueKind.Array:
                foreach (var child in value.EnumerateArray())
                    CollectSavedOutputs(child, savedOutputs, cancellationToken);
                break;
        }
    }

    private static string? ReadString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string DescribeStatus(QueueRead? queue, HistoryRead? history)
    {
        if (queue is null)
            return history is null ? "本机 ComfyUI 暂不可读。" : "近期历史已读取，队列暂不可读。";

        string queueStatus = queue.Pending == 0 && queue.Running == 0 ? "队列为空" : "队列有任务";
        return history is null
            ? $"{queueStatus}，近期历史暂不可读。"
            : $"本机 ComfyUI 可读，{queueStatus}。";
    }

    private enum ExecutionOutcome
    {
        Unknown,
        Succeeded,
        Failed
    }

    private sealed record QueueRead(int Pending, int Running, IReadOnlyList<string> RunningPromptIds);
    private sealed record HistoryRead(int Succeeded, int Failed, int SavedOutputs,
        IReadOnlyList<ExecutionDuration> Durations);
    private readonly record struct ExecutionDuration(long EndedAt, double Seconds);
}

public sealed record ComfyUiMonitorRead(string? OutputDirectory, ComfyUiNodeProgress? Progress);
