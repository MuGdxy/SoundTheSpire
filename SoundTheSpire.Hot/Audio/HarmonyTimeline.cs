using Godot;

namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// Resolves the song chord at a future playback time. FMOD markers anchor an offline per-bar chord map to runtime.
/// Audio consumers only request a chord; they do not know section offsets, pickups or loop lengths.
/// </summary>
public static class HarmonyTimeline
{
    private static ulong _sectionStartTicks;
    private static string? _marker;

    public static void Reset()
    {
        _sectionStartTicks = 0;
        _marker = null;
    }

    public static void OnMarker(string marker, int positionMs)
    {
        if (MusicClock.ActiveProfile?.Harmony is not { } harmony ||
            !marker.Equals(harmony.Marker, StringComparison.OrdinalIgnoreCase))
            return;
        _marker = marker;
        if (_sectionStartTicks != 0)
            return;
        _sectionStartTicks = Time.GetTicksMsec();
        MainFile.Logger.Info(
            $"Harmony section '{MusicClock.ActiveProfile.Id}' anchored at marker '{marker}' ({positionMs}ms).");
    }

    public static MusicChordChangeData? ChordAt(double secondsFromNow, out int bar)
    {
        bar = -1;
        if (MusicClock.ActiveProfile?.Harmony is not { } harmony || harmony.Changes.Length == 0)
            return null;

        var musicalSeconds = MusicalSeconds(harmony, secondsFromNow);
        bar = (int)Math.Floor(musicalSeconds / MusicClock.BarSeconds) % harmony.LoopBars;

        MusicChordChangeData? chord = null;
        foreach (var change in harmony.Changes.OrderBy(change => change.Bar))
        {
            if (change.Bar > bar)
                break;
            chord = change;
        }
        return chord ?? harmony.Changes.OrderBy(change => change.Bar).Last();
    }

    public static int? MelodyAt(double secondsFromNow, out int step)
    {
        step = -1;
        if (MusicClock.ActiveProfile?.Harmony is not
            { MelodyNotes.Length: > 0, MelodySubdivisionsPerBeat: > 0 } harmony)
            return null;
        var musicalSeconds = MusicalSeconds(harmony, secondsFromNow);
        var stepSeconds = MusicClock.BeatSeconds / harmony.MelodySubdivisionsPerBeat;
        step = (int)Math.Floor(musicalSeconds / stepSeconds) % harmony.MelodyNotes.Length;
        return harmony.MelodyNotes[step];
    }

    public static double LoudnessGainDbAt(double secondsFromNow, out int bar)
    {
        bar = -1;
        if (MusicClock.ActiveProfile is not
            { DynamicLoudness: true, Harmony: { BarLoudnessLufs.Length: > 0 } harmony })
            return 0;
        var musicalSeconds = MusicalSeconds(harmony, secondsFromNow);
        bar = (int)Math.Floor(musicalSeconds / MusicClock.BarSeconds) % harmony.LoopBars;
        var difference = harmony.BarLoudnessLufs[bar] - harmony.ReferenceLoudnessLufs;
        return Math.Clamp(difference, -harmony.MaxLoudnessAttenuationDb, 0);
    }

    public static int AdjustVelocity(int velocity, double secondsFromNow)
    {
        var gainDb = LoudnessGainDbAt(secondsFromNow, out _);
        // GeneralUser GS amplitude is approximately proportional to velocity squared.
        var velocityScale = Math.Pow(10, gainDb / 40.0);
        return Math.Clamp((int)Math.Round(velocity * velocityScale), 1, 127);
    }

    private static double MusicalSeconds(MusicHarmonyData harmony, double secondsFromNow)
    {
        var elapsed = _sectionStartTicks == 0
            ? 0
            : (Time.GetTicksMsec() - _sectionStartTicks) / 1000.0;
        return Math.Max(0,
            elapsed + Math.Max(0, secondsFromNow) - harmony.PickupBeats * MusicClock.BeatSeconds);
    }
}
