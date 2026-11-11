// Version: 0.3.3-preview; Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// LOCAL-WORK / Windows / Codex Desktop / O2 integration review / Codex
// Original source: fixed-pointer pose/mask feedback reproduction and pointer re-entry requirements.
using LumiCompanion.Animation;

namespace LumiCompanion.Diagnostics;

public static class BodyHoverSelfTest
{
    public static void Run()
    {
        static void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Body hover: " + name);
        }
        var tracker = new BodyHoverTracker();
        Check(tracker.Update(100, 100, true), "current visible body can enter hover");
        Check(!tracker.Update(100, 100, false), "new pose outside body exits immediately");
        for (int i = 0; i < 8; i++)
            Check(!tracker.Update(100, 100, true), "restoring the task pose cannot re-enter under a stationary pointer");
        Check(!tracker.Update(101, 100, true), "one pixel of jitter does not re-arm hover");
        Check(tracker.Update(102, 100, true), "real pointer movement re-arms current body hover");
        Check(!tracker.Update(104, 100, false), "cable/background never remains hovered after pointer movement");
        Check(tracker.Update(104, 100, true), "moving out of body does not create a stationary-pose lock");
        tracker.Update(104, 100, false);
        Check(!tracker.Update(104, 100, true), "another pose change is guarded");
        tracker.Reset();
        Check(tracker.Update(104, 100, true), "new task context or hide/show can reset without forcing pointer movement");
        Check(!tracker.Update(104, 100, false), "reset does not convert a cable/background point to body");
    }
}
