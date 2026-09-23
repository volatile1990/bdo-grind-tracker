using BdoGrindTracker.App.Overlay;
using BdoGrindTracker.Ocr;

namespace BdoGrindTracker.App.Analysis;

/// <param name="Kind">The recognized message, replayed into the spot's rotation profile once a session starts.</param>
/// <param name="At">When the banner appeared.</param>
internal sealed record RotationStartSighting(string SpotId, string Kind, string Label, DateTimeOffset At);

/// <summary>
/// Watches the banner area for a spot's unmistakable rotation start while the automatic grind detection is idle.
/// Walking past a spot can trigger such a banner, so it only reports the sighting; the automatic start still waits
/// for trash loot before a session begins.
/// </summary>
internal sealed class RotationStartWatcher : IDisposable
{
    internal static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(3);
    /// <summary>How long a sighting keeps a following trash drop eligible for an immediate start.</summary>
    internal static readonly TimeSpan Validity = TimeSpan.FromMinutes(3);

    /// <summary>The spots whose start banner can mean nothing else, with the messages to look for.</summary>
    private static readonly (string SpotId, RotationMessageProfile Profile, string[] Starts)[] Watched =
        [.. RotationProfiles.SupportedSpotIds
            .Select(spotId => (SpotId: spotId, Profile: RotationProfiles.Messages(spotId),
                Starts: RotationDefinition.Find(spotId)?.UnmistakableStartMessages ?? []))
            .Where(watched => watched.Profile is not null && watched.Starts.Length > 0)
            .Select(watched => (watched.SpotId, watched.Profile!, watched.Starts))];

    private readonly Func<Bitmap, string>? _recognize;
    private CompanionWindowsOcrRecognizer? _ocr;
    private DateTimeOffset? _lastProbe;
    private bool _disposed;

    internal RotationStartWatcher(Func<Bitmap, string>? recognize = null) => _recognize = recognize;

    /// <summary>The start banner on screen, or null. Probes at most every <see cref="ProbeInterval"/>.</summary>
    internal RotationStartSighting? Observe(Bitmap frame, DateTimeOffset at)
    {
        if (_disposed) return null;
        if (_lastProbe is { } probed && at - probed < ProbeInterval) return null;
        _lastProbe = at;
        // Spots share their banner area: each region is read once and parsed by every profile watching it.
        foreach (var group in Watched.GroupBy(watched =>
            (Region: watched.Profile.Crop(frame.Width, frame.Height), watched.Profile.SingleLine)))
        {
            var region = group.Key.Region;
            if (region.Width < 32 || region.Height < 16 ||
                !new Rectangle(Point.Empty, frame.Size).Contains(region)) continue;
            using var crop = frame.Clone(region, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            string? text = null;
            foreach (var (spotId, profile, starts) in group)
            {
                text ??= Read(crop, profile);
                if (text.Length == 0) break;
                foreach (var message in profile.Parse(text))
                    if (starts.Contains(message.Kind)) return new(spotId, message.Kind, message.Label, at);
            }
        }
        return null;
    }

    private string Read(Bitmap crop, RotationMessageProfile profile)
    {
        if (_recognize is not null) return _recognize(crop);
        _ocr ??= CompanionWindowsOcrRecognizer.TryCreate("en-US", requirePreferredLanguage: true);
        if (_ocr is null) return "";
        using var pixels = CompanionFrameDecoder.Decode(crop);
        return profile.Recognize(pixels, _ocr);
    }

    public void Dispose()
    {
        _disposed = true;
        _ocr = null;
    }
}
