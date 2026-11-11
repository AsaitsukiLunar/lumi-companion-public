/*
Traceability metadata
Version: 0.3.1-preview
Generated: 2026-07-25 (Asia/Shanghai)
Last updated: 2026-10-30
Content coverage through: 2026-10-30
Source type: LOCAL-WORK
Source: Codex Desktop / workspace new-chat / current Lumi independent companion task / Codex
*/

using System.Windows;
using System.Windows.Media.Imaging;

namespace LumiCompanion.Animation;

public enum AnimationId
{
    Idle,
    RunningRight,
    RunningLeft,
    Waving,
    Jumping,
    Failed,
    Waiting,
    Running,
    Review,
    Look
}

public sealed class AnimationDefinition
{
    public required AnimationId Id { get; init; }
    public required IReadOnlyList<BitmapSource> Frames { get; init; }
    public required IReadOnlyList<BitmapSource> HighResolutionFrames { get; init; }
    public required TimeSpan FrameDuration { get; init; }
    public bool Loop { get; init; } = true;

    public BitmapSource GetFrame(int index, bool highResolution)
    {
        IReadOnlyList<BitmapSource> frames = highResolution ? HighResolutionFrames : Frames;
        return frames[index];
    }
}

public sealed class AnimationCatalog
{
    private readonly Dictionary<AnimationId, AnimationDefinition> _animations = new();
    private readonly List<BitmapSource> _lookFrames = new();
    private readonly List<BitmapSource> _highResolutionLookFrames = new();

    public AnimationCatalog()
    {
        Add(AnimationId.Idle, "idle", 6, 320);
        Add(AnimationId.RunningRight, "running-right", 8, 90);
        Add(AnimationId.RunningLeft, "running-left", 8, 90);
        Add(AnimationId.Waving, "waving", 4, 130, loop: false);
        Add(AnimationId.Jumping, "jumping", 5, 105, loop: false);
        Add(AnimationId.Failed, "failed", 8, 140, loop: false);
        Add(AnimationId.Waiting, "waiting", 6, 160);
        Add(AnimationId.Running, "running", 6, 130);
        Add(AnimationId.Review, "review", 6, 160);

        for (int i = 0; i < 8; i++)
        {
            _lookFrames.Add(LoadFrame("look-000-157.5", i, 1));
            _highResolutionLookFrames.Add(LoadFrame("look-000-157.5", i, 2));
        }

        for (int i = 0; i < 8; i++)
        {
            _lookFrames.Add(LoadFrame("look-180-337.5", i, 1));
            _highResolutionLookFrames.Add(LoadFrame("look-180-337.5", i, 2));
        }

        if (TotalFrameCount != 73 || HighResolutionFrameCount != 73)
        {
            throw new InvalidDataException("Lumi animation resource count is not 73 in both resolutions.");
        }
    }

    public AnimationDefinition Get(AnimationId id)
    {
        if (id == AnimationId.Look)
        {
            throw new ArgumentException("Use GetLookFrame for direction cells.", nameof(id));
        }

        return _animations[id];
    }

    public BitmapSource GetLookFrame(int index, bool highResolution = false)
    {
        index = ((index % 16) + 16) % 16;
        return highResolution ? _highResolutionLookFrames[index] : _lookFrames[index];
    }

    public int TotalFrameCount => _animations.Values.Sum(a => a.Frames.Count) + _lookFrames.Count;

    public int HighResolutionFrameCount =>
        _animations.Values.Sum(a => a.HighResolutionFrames.Count) + _highResolutionLookFrames.Count;

    private void Add(AnimationId id, string folder, int frameCount, int frameMilliseconds, bool loop = true)
    {
        var frames = new List<BitmapSource>(frameCount);
        var highResolutionFrames = new List<BitmapSource>(frameCount);
        for (int i = 0; i < frameCount; i++)
        {
            frames.Add(LoadFrame(folder, i, 1));
            highResolutionFrames.Add(LoadFrame(folder, i, 2));
        }

        _animations[id] = new AnimationDefinition
        {
            Id = id,
            Frames = frames,
            HighResolutionFrames = highResolutionFrames,
            FrameDuration = TimeSpan.FromMilliseconds(frameMilliseconds),
            Loop = loop
        };
    }

    private static BitmapSource LoadFrame(string folder, int index, int resolutionScale)
    {
        string assetFolder = resolutionScale == 1 ? "frames" : "frames@2x";
        string path = $"pack://application:,,,/assets/{assetFolder}/{folder}/{index:00}.png";
        var resource = Application.GetResourceStream(new Uri(path, UriKind.Absolute));
        if (resource is null)
        {
            throw new FileNotFoundException("Missing embedded Lumi frame: " + path);
        }

        using Stream stream = resource.Stream;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        int expectedWidth = 192 * resolutionScale;
        int expectedHeight = 208 * resolutionScale;
        if (bitmap.PixelWidth != expectedWidth || bitmap.PixelHeight != expectedHeight)
        {
            throw new InvalidDataException(
                $"Unexpected Lumi frame size for {path}: {bitmap.PixelWidth}x{bitmap.PixelHeight}; " +
                $"expected {expectedWidth}x{expectedHeight}.");
        }

        return bitmap;
    }
}
