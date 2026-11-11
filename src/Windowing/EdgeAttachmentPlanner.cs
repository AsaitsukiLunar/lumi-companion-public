/*
Traceability metadata
Version: 0.3.0-dev
Generated: 2026-08-09 (Asia/Shanghai)
Last updated: 2026-08-09
Content coverage through: 2026-08-09
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

namespace LumiCompanion.Windowing;

public enum EdgeAttachmentKind
{
    None,
    LeftCling,
    RightCling,
    LeftClimb,
    RightClimb,
    TopSit
}

public readonly record struct NormalizedArtAnchor(double X, double Y)
{
    public NormalizedArtAnchor Clamp() => new(Math.Clamp(X, 0, 1), Math.Clamp(Y, 0, 1));
}

public readonly record struct EdgeAttachmentPlacement(
    double Left,
    double Top,
    EdgeAttachmentKind Kind,
    double EdgeProgress);

public static class EdgeAttachmentPlanner
{
    public static EdgeAttachmentPlacement Calculate(
        PixelRect target,
        EdgeAttachmentKind kind,
        int petPixelWidth,
        NormalizedArtAnchor artAnchor,
        double edgeProgress)
    {
        if (!target.IsUsable)
        {
            throw new ArgumentException("Target bounds are not usable.", nameof(target));
        }
        if (kind == EdgeAttachmentKind.None)
        {
            throw new ArgumentException("Attachment kind cannot be None.", nameof(kind));
        }

        int width = Math.Clamp(petPixelWidth, 96, 320);
        double height = width * 208.0 / 192.0;
        NormalizedArtAnchor anchor = artAnchor.Clamp();
        double progress = Math.Clamp(edgeProgress, 0, 1);

        return kind switch
        {
            EdgeAttachmentKind.LeftCling or EdgeAttachmentKind.LeftClimb => new(
                target.Left - anchor.X * width,
                target.Top + progress * target.Height - anchor.Y * height,
                kind,
                progress),
            EdgeAttachmentKind.RightCling or EdgeAttachmentKind.RightClimb => new(
                target.Right - anchor.X * width,
                target.Top + progress * target.Height - anchor.Y * height,
                kind,
                progress),
            EdgeAttachmentKind.TopSit => new(
                target.Left + progress * target.Width - anchor.X * width,
                target.Top - anchor.Y * height,
                kind,
                progress),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }
}
