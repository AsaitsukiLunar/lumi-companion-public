// Version: 0.3.14-preview; Generated: 2026-10-30; Updated/Coverage: 2026-11-11 (Asia/Shanghai)
// Source: RAW | user floor switch, right-side ComfyUI and automatic placement requests; LOCAL-WORK | Codex Desktop / Lumi / Codex.
using System.Windows;

namespace LumiCompanion.UI;

/// <summary>Physical screen pixel layout based on stable window ground, independent of animation frames.</summary>
internal static class UsageDockLayout
{
    private const double ComfyUiGap = 10;

    internal static double FitPetBesideComfyUi(Rect pet, Rect work, System.Windows.Size size)
    {
        double pairWidth = pet.Width + ComfyUiGap + size.Width;
        if (pairWidth > work.Width) return pet.Left;
        return Math.Min(pet.Left, work.Right - pairWidth);
    }

    internal static Rect FindComfyUiPanel(Rect pet, Rect work, System.Windows.Size size, Rect? usage)
    {
        double right = pet.Right + ComfyUiGap;
        double left = pet.Left - size.Width - ComfyUiGap;
        double middle = pet.Top + Math.Max(0, (pet.Height - size.Height) / 2);
        double workRight = work.Right - size.Width;
        var candidates = new List<Point> { new(right, middle) };
        if (usage is { } rightObstacle)
        {
            candidates.Add(new(right, rightObstacle.Top - size.Height - ComfyUiGap));
            candidates.Add(new(right, rightObstacle.Bottom + ComfyUiGap));
            candidates.Add(new(Math.Max(right, rightObstacle.Right + ComfyUiGap), middle));
        }

        candidates.Add(new(left, middle));
        if (usage is { } leftObstacle)
        {
            candidates.Add(new(left, leftObstacle.Top - size.Height - ComfyUiGap));
            candidates.Add(new(left, leftObstacle.Bottom + ComfyUiGap));
            candidates.Add(new(Math.Min(left, leftObstacle.Left - size.Width - ComfyUiGap), middle));
        }

        // A screen that cannot fit the pair side by side can still keep the panel at its right edge.
        candidates.Add(new(workRight, pet.Top - size.Height - ComfyUiGap));
        candidates.Add(new(workRight, pet.Bottom + ComfyUiGap));
        return Choose(pet, work, size, usage, candidates.ToArray()) ??
            new Rect(Math.Max(work.Left, workRight),
                Math.Clamp(middle, work.Top, Math.Max(work.Top, work.Bottom - size.Height)), size.Width, size.Height);
    }

    internal static Rect? FindSwitch(Rect pet, Rect work, System.Windows.Size size, Rect? bubble)
    {
        const double gap = 8;
        return Choose(pet, work, size, bubble,
        [
            new Point(pet.Left + (pet.Width - size.Width) / 2, pet.Bottom + gap),
            new Point(pet.Right + gap, pet.Bottom - size.Height),
            new Point(pet.Left - size.Width - gap, pet.Bottom - size.Height),
            new Point(pet.Left + (pet.Width - size.Width) / 2, pet.Top - size.Height - gap)
        ]);
    }

    internal static Rect? FindPopover(Rect pet, Rect work, Rect dock, System.Windows.Size size, Rect? bubble)
    {
        const double gap = 8;
        // The full card belongs beside the character, independently of compact layout.
        return Choose(pet, work, size, bubble,
        [
            new Point(pet.Left - size.Width - gap, pet.Top + (pet.Height - size.Height) / 2),
            new Point(pet.Right + gap, pet.Top + (pet.Height - size.Height) / 2)
        ]);
    }

    private static Rect? Choose(Rect pet, Rect work, System.Windows.Size size, Rect? bubble, Point[] candidates, Rect? dock = null)
    {
        if (size.Width <= 0 || size.Height <= 0 || size.Width > work.Width || size.Height > work.Height) return null;
        foreach (Point point in candidates)
        {
            var rect = new Rect(Math.Clamp(point.X, work.Left, work.Right - size.Width),
                Math.Clamp(point.Y, work.Top, work.Bottom - size.Height), size.Width, size.Height);
            if (rect.IntersectsWith(pet) || (bubble is { } occupied && rect.IntersectsWith(occupied)) ||
                (dock is { } button && rect.IntersectsWith(button))) continue;
            return rect;
        }
        return null;
    }
}
