/*
Traceability metadata
Version: 0.2.0
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-07-25
Content coverage through: 2026-07-25
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using System.Text.Json;
using LumiCompanion.Animation;
using LumiCompanion.Infrastructure;
using LumiCompanion.Models;
using LumiCompanion.Sensors;
using LumiCompanion.Windowing;

namespace LumiCompanion.Diagnostics;

public static class SelfTestRunner
{
    public static int Run(string reportPath)
    {
        var report = new SelfTestReport();
        string temporary = Path.Combine(Path.GetTempPath(), "LumiCompanion-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            report.Checks["windows"] = OperatingSystem.IsWindows();
            var catalog = new AnimationCatalog();
            report.Checks["embeddedFrames1x"] = catalog.TotalFrameCount;
            report.Checks["embeddedFrames2x"] = catalog.HighResolutionFrameCount;
            report.Checks["frameGeometry1x"] = "192x208";
            report.Checks["frameGeometry2x"] = "384x416";
            report.Checks["petWidthUnit"] = "physical-pixel";
            report.Checks["resolutionSwitchAt"] = 192;

            string expectedDataDirectory = StorageService.IsPortable
                ? Path.Combine(AppContext.BaseDirectory, "data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumiCompanion");
            bool storageProfile = StorageService.DefaultDataDirectory == expectedDataDirectory &&
                                  new ProviderTabsStore().FilePath == Path.Combine(expectedDataDirectory, "provider-tabs.json") &&
                                  StartupManager.IsSupported == !StorageService.IsPortable;
            report.Checks["portableMode"] = StorageService.IsPortable;
            report.Checks["storageProfile"] = storageProfile;

            var storage = new StorageService(temporary);
            LumiSettings settings = storage.LoadSettings();
            LumiPersistentState state = storage.LoadState();
            report.Checks["settingsRoundTrip"] = File.Exists(storage.SettingsPath) && settings.PetWidth == 192;
            report.Checks["stateRoundTrip"] = File.Exists(storage.StatePath) && state.Mood is >= 0 and <= 100;

            bool codexPackageMatcher = CodexWindowTargetMatcher.IsMatch(
                "ChatGPT",
                string.Empty,
                string.Empty,
                "OpenAI.Codex_26.730.8199.0_x64__2p2nqsd0c76g0");
            bool codexPathMatcher = CodexWindowTargetMatcher.IsMatch(
                "ChatGPT",
                string.Empty,
                @"C:\Program Files\WindowsApps\OpenAI.Codex_26.730.8199.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe",
                string.Empty);
            bool rejectsGenericChatGpt = !CodexWindowTargetMatcher.IsMatch(
                "ChatGPT",
                "Conversation",
                @"C:\Program Files\OpenAI\ChatGPT.exe",
                "OpenAI.ChatGPT_1.0_x64");
            EdgeAttachmentPlacement placement = EdgeAttachmentPlanner.Calculate(
                new PixelRect(100, 100, 1100, 900),
                EdgeAttachmentKind.LeftCling,
                192,
                new NormalizedArtAnchor(0.5, 0.5),
                0.5);
            bool attachmentPlanner = Math.Abs(placement.Left - 4) < 0.001 &&
                                     Math.Abs(placement.Top - 396) < 0.001;
            report.Checks["codexPackageMatcher"] = codexPackageMatcher;
            report.Checks["codexPathMatcher"] = codexPathMatcher;
            report.Checks["rejectsGenericChatGpt"] = rejectsGenericChatGpt;
            report.Checks["edgeAttachmentPlanner"] = attachmentPlanner;

            report.Checks["idleSeconds"] = Math.Round(DesktopActivityMonitor.GetIdleSeconds(), 3);
            report.Checks["cursorApi"] = DesktopActivityMonitor.TryGetCursorPosition(out _);

            bool audioLifecycle;
            using (var audio = new AudioPeakMonitor(enabled: true))
            {
                Thread.Sleep(300);
                bool enabled = audio.IsEnabled;
                report.Checks["audioSensorEnabled"] = enabled;
                report.Checks["audioMeterAvailable"] = audio.IsAvailable;
                report.Checks["audioPeak"] = audio.GetPeakValue();

                audio.SetEnabled(false);
                Thread.Sleep(50);
                audioLifecycle = enabled && !audio.IsEnabled && !audio.IsAvailable && audio.GetPeakValue() == 0f;
                report.Checks["audioSensorDisabled"] = !audio.IsEnabled;
                report.Checks["audioLifecycle"] = audioLifecycle;
            }

            report.Ok = OperatingSystem.IsWindows() &&
                        catalog.TotalFrameCount == 73 &&
                        catalog.HighResolutionFrameCount == 73 &&
                        storageProfile &&
                        File.Exists(storage.SettingsPath) &&
                        File.Exists(storage.StatePath) &&
                        audioLifecycle &&
                        codexPackageMatcher &&
                        codexPathMatcher &&
                        rejectsGenericChatGpt &&
                        attachmentPlanner;
        }
        catch (Exception ex)
        {
            report.Ok = false;
            report.Checks["exception"] = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporary))
                {
                    Directory.Delete(temporary, true);
                }
            }
            catch
            {
                // Self-test cleanup is best-effort.
            }
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return report.Ok ? 0 : 1;
    }
}
