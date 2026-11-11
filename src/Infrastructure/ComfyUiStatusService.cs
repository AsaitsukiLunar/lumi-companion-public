/*
Traceability metadata
Version: 0.3.9-preview
Generated: 2026-11-10 (Asia/Shanghai)
Last updated: 2026-11-10
Content coverage through: 2026-11-10
Source type: RAW | user request for local ComfyUI status; LOCAL-WORK | read-only HTTP implementation
Source: Codex Desktop / lumi-companion / independent Lumi companion chat / Codex
Organizing node: Windows / Codex Desktop / ComfyUI HTTP status subtask
Original sources: user requirements and ComfyUI's documented server routes and execution history.
https://docs.comfy.org/development/comfyui-server/comms_routes
https://github.com/Comfy-Org/ComfyUI/blob/master/execution.py
*/

using System.Net.Http;
using System.Text.Json;
using LumiCompanion.Models;

namespace LumiCompanion.Infrastructure;

public sealed class ComfyUiStatusService : IDisposable
{
    public const int HistoryLimit = 100;

    private static readonly Uri ServerAddress = new("http://127.0.0.1:8188/");
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);
    private readonly HttpClient _client;

    public ComfyUiStatusService(HttpMessageHandler? handler = null)
    {
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

        var queueTask = ReadEndpointAsync("queue", ParseQueue, deadline.Token, cancellationToken);
        var historyTask = ReadEndpointAsync($"history?max_items={HistoryLimit}", ParseHistory,
            deadline.Token, cancellationToken);
        await Task.WhenAll(queueTask, historyTask).ConfigureAwait(false);

        var queue = await queueTask.ConfigureAwait(false);
        var history = await historyTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return new ComfyUiStatusSnapshot(
            DateTimeOffset.Now, queue?.Pending, queue?.Running, queue?.RunningPromptIds ?? [],
            history?.Succeeded, history?.Failed, history?.SavedOutputs, DescribeStatus(queue, history));
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
                    if (task.TryGetProperty("outputs", out var outputs))
                        CollectSavedOutputs(outputs, savedOutputs, cancellationToken);
                    break;
            }
        }

        return new HistoryRead(succeeded, failed, savedOutputs.Count);
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
    private sealed record HistoryRead(int Succeeded, int Failed, int SavedOutputs);
}

public sealed record ComfyUiMonitorRead(string? OutputDirectory, ComfyUiNodeProgress? Progress);
