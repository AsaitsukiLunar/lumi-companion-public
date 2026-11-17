/*
Traceability metadata
Version: 0.3.8-preview
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-10-30
Content coverage through: 2026-10-30
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using System.Text;
using System.Text.Json;
using System.Windows;
using LumiCompanion.Diagnostics;
using LumiCompanion.Infrastructure;
using LumiCompanion.Integration;
using LumiCompanion.Models;

namespace LumiCompanion;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (TryGetArgumentValue(args, "--workbuddy-status", out string? workBuddyReport))
        {
            var history = new WorkBuddyCompletionHistory(enabled: true);
            WorkBuddyTaskReadResult result = new LocalWorkBuddyTaskSource().ReadAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            if (result.CompletionEventsAvailable && result.ScopeKey is { } scope)
                history.Apply(scope, result.CompletionEvents);
            CompletionHistorySnapshot snapshot = history.Read();
            string path = Path.GetFullPath(workBuddyReport ?? "workbuddy-status.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                _metadata = new TraceMetadata(),
                statusAvailable = result.Snapshot is not null,
                status = result.BridgeState,
                result.TurnSeq,
                completionEventsAvailable = snapshot.Available,
                snapshot.ThisLaunch, snapshot.Today, snapshot.Total
            }, new JsonSerializerOptions { WriteIndented = true }));
            return result.Snapshot is not null && snapshot.Available ? 0 : 2;
        }

        if (TryGetArgumentValue(args, "--comfyui-status", out string? comfyUiReport))
        {
            using var service = new ComfyUiStatusService();
            Task<ComfyUiStatusSnapshot> statusTask = service.ReadAsync(CancellationToken.None);
            Task<ComfyUiMonitorRead?> monitorTask = service.ReadMonitorAsync(CancellationToken.None);
            Task.WhenAll(statusTask, monitorTask).GetAwaiter().GetResult();
            ComfyUiStatusSnapshot snapshot = statusTask.Result;
            ComfyUiMonitorRead? monitor = monitorTask.Result;
            var progress = monitor?.Progress is { } current && snapshot.RunningPromptIds.Contains(current.PromptId)
                ? new { current.Node, current.Value, current.Maximum } : null;
            string path = Path.GetFullPath(comfyUiReport ?? "comfyui-status.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                _metadata = new TraceMetadata(),
                historyLimit = ComfyUiStatusService.HistoryLimit,
                progressBridgeAvailable = monitor is not null,
                nodeProgress = progress,
                runningEtaSeconds = snapshot.RunningEta is { } eta
                    ? new { lower = eta.Lower.TotalSeconds, upper = eta.Upper.TotalSeconds }
                    : null,
                snapshot.CapturedAt, snapshot.Pending, snapshot.Running,
                snapshot.Succeeded, snapshot.Failed, snapshot.SavedOutputs, snapshot.Status
            }, new JsonSerializerOptions { WriteIndented = true }));
            return snapshot.Pending.HasValue && snapshot.Succeeded.HasValue ? 0 : 2;
        }

        if (args.Contains("--integration-self-test"))
        {
            IntegrationSelfTest.Run();
            GitHubUpdateSelfTest.Run();
            CodexUsageSelfTest.Run();
            LocalTaskSourceSelfTest.Run();
            LocalWorkBuddyTaskSourceSelfTest.Run();
            TaskCoordinatorSelfTest.Run();
            BodyHoverSelfTest.Run();
            ComfyUiEtaSelfTest.Run();
            return 0;
        }

        if (TryGetArgumentValue(args, "--usage-probe", out string? usageReport))
        {
            CodexUsageSnapshot snapshot = new CodexUsageService().ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            string path = Path.GetFullPath(usageReport ?? "usage-probe.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                _metadata = new TraceMetadata(),
                scope = "Read-only local quota observation; not a live service request. Account identifiers omitted.",
                snapshot.FiveHour, snapshot.Weekly, snapshot.CapturedAt, snapshot.SourceLabel,
                snapshot.IsStale, snapshot.HasAnyQuota, snapshot.StatusMessage, snapshot.TodayStatus
            }, new JsonSerializerOptions { WriteIndented = true }));
            return snapshot.HasAnyQuota ? 0 : 2;
        }

        if (TryGetArgumentValue(args, "--codex-task-status", out string? taskReport))
        {
            LocalCodexTaskReadResult status = new LocalCodexTaskSource().ReadAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            string path = Path.GetFullPath(taskReport ?? "codex-task-status.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var states = status.Observations.GroupBy(observation => observation.Snapshot.State)
                .ToDictionary(group => group.Key.ToString(), group => group.Count());
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                _metadata = new TraceMetadata(),
                status.SourceAvailable, status.StatusMessage, status.InspectedFiles, status.MatchedFiles,
                states, completions = status.CompletionEvents.Count
            }, new JsonSerializerOptions { WriteIndented = true }));
            return status.SourceAvailable ? 0 : 2;
        }

        if (TryGetArgumentValue(args, "--hit-test-underlay", out string? underlayReport))
        {
            return HitTestSelfTest.RunUnderlay(underlayReport ?? "hit-test-underlay.json");
        }

        if (TryGetArgumentValue(args, "--hit-test-self-test", out string? hitReport))
        {
            var hitApplication = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            int result = HitTestSelfTest.Run(hitReport ?? "hit-test-self-test.json");
            hitApplication.Shutdown();
            return result;
        }

        if (TryGetArgumentValue(args, "--provider-tabs-self-test", out string? tabsReport))
        {
            var tabsApplication = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            int result = ProviderTabsSelfTest.Run(tabsReport ?? "provider-tabs-self-test.json");
            tabsApplication.Shutdown();
            return result;
        }

        if (TryGetArgumentValue(args, "--task-behavior-self-test", out string? behaviorReport))
        {
            return TaskBehaviorSelfTest.Run(behaviorReport ?? "task-behavior-self-test.json");
        }

        if (TryGetArgumentValue(args, "--task-bubble-self-test", out string? bubbleReport))
        {
            var bubbleApplication = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            int result = TaskStatusBubbleSelfTest.Run(bubbleReport ?? "task-bubble-self-test.json");
            bubbleApplication.Shutdown();
            return result;
        }

        if (TryGetArgumentValue(args, "--usage-dock-self-test", out string? dockReport))
        {
            var dockApplication = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            int result = UsageDockSelfTest.Run(dockReport ?? "usage-dock-self-test.json");
            dockApplication.Shutdown();
            return result;
        }

        if (TryGetArgumentValue(args, "--drag-motion-self-test", out string? dragReport))
        {
            return DragMotionSelfTest.Run(dragReport ?? "drag-motion-self-test.json");
        }

        if (TryGetArgumentValue(args, "--animation-self-test", out string? animationReport))
        {
            var animationApplication = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            int result = AnimationSelfTest.Run(animationReport ?? "animation-self-test.json");
            animationApplication.Shutdown();
            return result;
        }

        if (TryGetArgumentValue(args, "--self-test", out string? reportPath))
        {
            var diagnosticApplication = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            int result = SelfTestRunner.Run(reportPath ?? Path.Combine(Environment.CurrentDirectory, "self-test.json"));
            diagnosticApplication.Shutdown();
            return result;
        }

        if (TryGetArgumentValue(args, "--completion-history-self-test", out string? completionReport))
        {
            var diagnosticApplication = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            int result = CompletionHistorySelfTest.Run(completionReport ?? "completion-history-self-test.json");
            diagnosticApplication.Shutdown();
            return result;
        }

        using var mutex = new Mutex(true, "Local\\LumiCompanion-7D4362A7-FA43-4F68-B684-7EA77D5D1A58", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Lumi 已经在运行，请查看任务栏右下角的小图标。", "Lumi", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }

        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        var storage = new StorageService();
        LumiApplicationHost? host = null;
        application.DispatcherUnhandledException += (_, e) =>
        {
            storage.WriteError("dispatcher", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                storage.WriteError("app-domain", exception);
            }
        };

        try
        {
            host = new LumiApplicationHost(application);
            host.Start();
            return application.Run();
        }
        catch (Exception ex)
        {
            storage.WriteError("startup", ex);
            MessageBox.Show(
                "Lumi 启动失败。错误记录已保存在本机数据目录。\n\n" + ex.Message,
                "Lumi",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }
        finally
        {
            host?.Dispose();
        }
    }

    private static bool TryGetArgumentValue(string[] args, string name, out string? value)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                value = i + 1 < args.Length ? args[i + 1] : null;
                return true;
            }

            string prefix = name + "=";
            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = args[i][prefix.Length..];
                return true;
            }
        }

        value = null;
        return false;
    }
}
