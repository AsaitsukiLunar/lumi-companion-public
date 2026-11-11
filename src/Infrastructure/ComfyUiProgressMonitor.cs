/*
Traceability metadata
Version: 1.0
Generated / Last updated / Content coverage through: 2026-11-10 (Asia/Shanghai)
Source type: RAW | local ComfyUI progress request; LOCAL-WORK | implementation
Organizing node: Windows / Codex Desktop / Codex / Lumi independent companion
Original source: https://docs.comfy.org/development/comfyui-server/comms_messages
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LumiCompanion.Infrastructure;

public sealed record ComfyUiNodeProgress(string PromptId, string? Node, double? Value, double? Maximum);

/// <summary>Observes node progress when ComfyUI sends it to this local WebSocket client.</summary>
public sealed class ComfyUiProgressMonitor : IDisposable
{
    private const int MaximumMessageBytes = 1024 * 1024;
    private readonly object _gate = new();
    private ClientWebSocket? _socket;
    private ComfyUiNodeProgress? _progress;
    private bool _disposed;

    public async Task ListenAsync(CancellationToken cancellationToken)
    {
        ClientWebSocket socket;
        lock (_gate)
        {
            if (_disposed || _socket is not null || cancellationToken.IsCancellationRequested)
                return;

            socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.Zero;
            _socket = socket;
        }

        using (socket)
        {
            try
            {
                using var transport = new HttpMessageInvoker(new SocketsHttpHandler
                {
                    UseProxy = false,
                    AllowAutoRedirect = false,
                    UseCookies = false
                });
                var endpoint = new Uri($"ws://127.0.0.1:8188/ws?clientId={Guid.NewGuid():N}");
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    await socket.ConnectAsync(endpoint, transport, timeout.Token).ConfigureAwait(false);
                }

                await ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is OperationCanceledException or WebSocketException
                or HttpRequestException or IOException or ObjectDisposedException)
            {
                // The caller controls reconnection and can continue using the HTTP queue state.
            }
            finally
            {
                lock (_gate)
                {
                    _socket = null;
                    _progress = null;
                }
            }
        }
    }

    public ComfyUiNodeProgress? Read(IReadOnlyList<string> runningPromptIds)
    {
        lock (_gate)
        {
            return _progress is { } progress && runningPromptIds.Contains(progress.PromptId, StringComparer.Ordinal)
                ? progress
                : null;
        }
    }

    public void Dispose()
    {
        ClientWebSocket? socket;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _progress = null;
            socket = _socket;
        }

        socket?.Abort();
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        bool oversized = false;
        while (true)
        {
            var part = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (part.MessageType == WebSocketMessageType.Close)
                return;

            if (part.MessageType == WebSocketMessageType.Text && !oversized)
            {
                oversized = message.Length + part.Count > MaximumMessageBytes;
                if (!oversized)
                    message.Write(buffer, 0, part.Count);
            }

            if (!part.EndOfMessage)
                continue;

            if (part.MessageType == WebSocketMessageType.Text && !oversized && message.Length > 0)
                ApplyMessage(message.GetBuffer().AsMemory(0, (int)message.Length));

            message.SetLength(0);
            oversized = false;
        }
    }

    private void ApplyMessage(ReadOnlyMemory<byte> message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return;

            string? promptId = ReadId(data, "prompt_id");
            string? node = ReadId(data, "node");
            lock (_gate)
            {
                if (_disposed)
                    return;

                switch (type.GetString())
                {
                    case "progress" when promptId is not null:
                        if (TryReadNumber(data, "value", out double value) &&
                            TryReadNumber(data, "max", out double maximum) &&
                            maximum > 0 && value >= 0 && value <= maximum)
                            _progress = new ComfyUiNodeProgress(promptId, node, value, maximum);
                        else
                            _progress = new ComfyUiNodeProgress(promptId, node, null, null);
                        break;

                    case "execution_start":
                    case "executing" when node is not null && promptId is null:
                        _progress = null;
                        break;

                    case "executing" when node is not null && promptId is not null:
                        if (_progress?.PromptId != promptId || _progress?.Node != node)
                            _progress = new ComfyUiNodeProgress(promptId, node, null, null);
                        break;

                    case "executing": // A null node marks prompt completion.
                    case "execution_success":
                    case "execution_error":
                    case "execution_interrupted":
                        if (promptId is null || _progress?.PromptId == promptId)
                            _progress = null;
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // Other messages on this socket do not provide usable node progress.
        }
    }

    private static string? ReadId(JsonElement data, string property)
    {
        if (!data.TryGetProperty(property, out var element))
            return null;

        string? value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryReadNumber(JsonElement data, string property, out double number)
    {
        number = 0;
        return data.TryGetProperty(property, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetDouble(out number) && double.IsFinite(number);
    }
}
