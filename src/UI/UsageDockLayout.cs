// Version: 0.3.19-preview; Generated: 2026-10-30; Updated/Coverage: 2026-11-16 (Asia/Shanghai)
// Source: RAW | user floor switch, right-side ComfyUI and panel height requirements; LOCAL-WORK | Codex Desktop / Lumi / Codex.
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

    internal static Rect? FindComfyUiPanel(Rect pet, Rect work, System.Windows.Size size, Rect? usage, Rect? bubble = null,
        double elevation = 0)
    {
        double right = pet.Right + ComfyUiGap;
        double left = pet.Left - size.Width - ComfyUiGap;
        double middle = pet.Top + Math.Max(0, (pet.Height - size.Height) / 2) - elevation;
        double workRight = work.Right - size.Width;
        var rows = new List<double> { middle };
        var rightColumns = new List<double> { right };
        var leftColumns = new List<double> { left };
        foreach (Rect? candidate in new[] { usage, bubble })
        {
            if (candidate is not { } obstacle) continue;
            rows.Add(obstacle.Top - size.Height - ComfyUiGap);
            rows.Add(obstacle.Bottom + ComfyUiGap);
            rightColumns.Add(Math.Max(right, obstacle.Right + ComfyUiGap));
            leftColumns.Add(Math.Min(left, obstacle.Left - size.Width - ComfyUiGap));
        }

        var besidePet = from x in rightColumns.Concat(leftColumns)
                        from y in rows
                        select new Point(x, y);
        var placement = Choose(pet, work, size, usage, besidePet, bubble);
        if (placement.HasValue) return placement;

        // Check the remaining work area, including tight gaps between the windows.
        var fallbackColumns = new List<double> { workRight, work.Left };
        var fallbackRows = new List<double> { work.Top, work.Bottom - size.Height };
        foreach (Rect? candidate in new[] { (Rect?)pet, usage, bubble })
        {
            if (candidate is not { } obstacle) continue;
            fallbackColumns.Add(obstacle.Right + 1);
            fallbackColumns.Add(obstacle.Left - size.Width - 1);
            fallbackRows.Add(obstacle.Top - size.Height - 1);
            fallbackRows.Add(obstacle.Bottom + 1);
        }

        var fallback = from x in fallbackColumns
                       from y in fallbackRows
                       select new Point(x, y);
        return Choose(pet, work, size, usage, fallback, bubble);
    }

    internal static Rect? FindSwitch(Rect pet, Rect work, System.Windows.Size size, Rect? bubble, double elevation = 0,
        Point? footAnchor = null)
    {
        const double gap = 8;
        double centered = (footAnchor?.X ?? (pet.Left + pet.Width / 2)) - size.Width / 2;
        Point[] anchors =
        [
            new Point(centered, pet.Bottom + gap),
            new Point(pet.Right + gap, pet.Bottom - size.Height),
            new Point(pet.Left - size.Width - gap, pet.Bottom - size.Height),
            new Point(centered, pet.Top - size.Height - gap)
        ];
        if (elevation == 0) return Choose(pet, work, size, bubble, anchors);
        return Choose(pet, work, size, bubble, anchors.Select(point => new Point(point.X, point.Y - elevation))) ??
            Choose(pet, work, size, bubble, anchors);
    }

    internal static Rect? FindPopover(Rect pet, Rect work, Rect dock, System.Windows.Size size, Rect? bubble,
        double elevation = 0)
    {
        const double gap = 8;
        double left = pet.Left - size.Width - gap;
        double right = pet.Right + gap;
        double middle = pet.Top + (pet.Height - size.Height) / 2 - elevation;
        Rect? besidePet = Choose(pet, work, size, bubble, [new(left, middle), new(right, middle)]);
        if (besidePet.HasValue || bubble is not { } occupied) return besidePet;

        // At a corner the bubble can clip the only available side slot.
        double above = occupied.Top - size.Height - gap;
        double below = occupied.Bottom + gap;
        bool belowIsCloser = Math.Abs(middle - below) < Math.Abs(middle - above);
        double nearer = belowIsCloser ? below : above;
        double farther = belowIsCloser ? above : below;
        return Choose(pet, work, size, occupied,
        [
            new(left, nearer), new(left, farther), new(right, nearer), new(right, farther)
        ]);
    }

    private static Rect? Choose(Rect pet, Rect work, System.Windows.Size size, Rect? occupied, IEnumerable<Point> candidates,
        Rect? otherOccupied = null)
    {
        if (size.Width <= 0 || size.Height <= 0 || size.Width > work.Width || size.Height > work.Height) return null;
        foreach (Point point in candidates)
        {
            var rect = new Rect(Math.Clamp(point.X, work.Left, work.Right - size.Width),
                Math.Clamp(point.Y, work.Top, work.Bottom - size.Height), size.Width, size.Height);
            if (rect.IntersectsWith(pet) || (occupied is { } first && rect.IntersectsWith(first)) ||
                (otherOccupied is { } second && rect.IntersectsWith(second))) continue;
            return rect;
        }
        return null;
    }
}
