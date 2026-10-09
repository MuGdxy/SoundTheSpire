using Godot;

namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// The defensive side, on piano. Waterfall Giant resolves to B minor and follows its authored FMOD tempo.
/// The verdict is the damage that would get through (incoming minus projected block), graded with the same tiers as
/// attacks (<see cref="IntentMotif.DamageTier"/>), from fully consonant to fully dissonant:
/// <list type="bullet">
/// <item>Nothing gets through: sus4 → tonic triad (the tonic alone when nothing is incoming).</item>
/// <item>Tiers 1–4: one held chord, rougher with every tier: sus4, diminished, tritone + major 7th, minor 2nd + tritone.</item>
/// <item>Tier 5: two clashing chords over a low tonic.</item>
/// </list>
/// The turn summary ends with one of these, and <see cref="Combat.DefenseMonitor"/> plays the new one whenever the
/// tier changes during the turn.
/// </summary>
public static class BlockMotif
{
    private const int Channel = 10;
    private static MusicProfileData? Profile => MusicClock.ActiveProfile;
    private static double BeatSeconds => MusicClock.BeatSeconds;
    public static double BarSeconds => MusicClock.BarSeconds;
    private static double LeadBeats => Profile?.LeadBeats ?? 1;
    private static double ResolveBeats => Profile?.ResolveBeats ?? 3;
    private static double HoldBeats => Profile?.HoldBeats ?? 3;
    private static double GateRatio => Profile?.GateRatio ?? 0.95;
    private static ulong _lastProfileMoveTicks;
    private static ulong _lastProfileInputTicks;

    private static int[] Suspended => Profile?.Suspended ?? new[] { 60, 65, 67 };
    private static int[] Resolved => Profile?.Resolved ?? new[] { 60, 64, 67, 72 };

    private static int[][] HurtChords => Profile?.HurtChords ?? new[]
    {
        new[] { 60, 65, 67 }, // C F G    sus4
        new[] { 60, 63, 66 }, // C Eb Gb  diminished
        new[] { 60, 66, 71 }, // C Gb B   tritone + major 7th
        new[] { 60, 61, 66 }, // C Db Gb  minor 2nd + tritone
    };
    private static int[] HurtVelocities => Profile?.HurtVelocities ?? new[] { 72, 80, 88, 96, 110 };
    private static int[] Clash => Profile?.Clash ?? new[] { 48, 60, 61, 66 };
    private static int[] ClashAfter => Profile?.ClashAfter ?? new[] { 47, 59, 60, 65 };

    /// <summary>0 when block holds, otherwise the attack tier (1–5) of the damage that gets through.</summary>
    public static int HurtTier(int block, int incoming) =>
        incoming > block ? IntentMotif.DamageTier(incoming - block) : 0;

    public static void PlayStatus(SynthEngine engine, int block, int incoming)
    {
        LegatoLine.Interrupt(engine);
        ScheduleStatus(engine, MusicClock.DelayToNextBeat(), block, incoming,
            Combat.IntentAnnouncer.HasFocusedEnemy
                ? Profile?.FocusedDefenseVolume ?? 45
                : Profile?.DefenseVolume ?? 100);
    }

    /// <summary>One held chord for querying the current state; unlike a transition, it has no before/after motion.</summary>
    public static void PlaySnapshot(SynthEngine engine, int block, int incoming)
    {
        LegatoLine.Interrupt(engine);
        var at = MusicClock.DelayToNextBeat();
        var songChord = HarmonyTimeline.ChordAt(at, out var bar);
        var status = StatusChord(block, incoming, songChord);
        if (songChord != null)
            MainFile.Logger.Info($"Defense snapshot: harmony bar {bar}, {songChord.Name}.");
        Setup(engine, at);
        engine.Chord(at, BeatSeconds * HoldBeats, Channel,
            HarmonyTimeline.AdjustVelocity(status.Velocity, at), status.Keys);
    }

    /// <summary>
    /// A card that improved the defense: one legato bar moving from the status chord before (a beat) into the status
    /// chord after (three beats), the rhythm of sus4 → major. Common tones are held through; the others overlap
    /// slightly so the change sounds connected. If the previous transition is still sounding, the line moves on from
    /// it straight away, so cards played in a row lead step by step towards the resolution.
    /// </summary>
    public static void PlayTransition(SynthEngine engine, int fromBlock, int fromIncoming, int toBlock, int toIncoming)
    {
        var hold = BeatSeconds * ResolveBeats * GateRatio;
        if (LegatoLine.IsOpen)
        {
            var (moveAt, grid) = MusicClock.IsProfileActive
                ? ReserveNextProfileMove()
                : (MusicClock.DelayToNextBeat(), "beat");
            var songChord = HarmonyTimeline.ChordAt(moveAt, out var bar);
            var queuedStatus = StatusChord(toBlock, toIncoming, songChord);
            if (MusicClock.ActiveProfile is { } activeProfile)
                MainFile.Logger.Info(
                    $"{activeProfile.Id} legato step: queued in {moveAt:F3}s on the {grid} grid" +
                    (songChord == null ? "." : $", harmony bar {bar}, {songChord.Name}."));
            LegatoLine.MoveTo(engine, moveAt, queuedStatus.Keys,
                HarmonyTimeline.AdjustVelocity(queuedStatus.Velocity, moveAt), moveAt + hold);
            return;
        }

        var start = MusicClock.DelayToNextBeat();
        var lead = BeatSeconds * LeadBeats;
        var fromSongChord = HarmonyTimeline.ChordAt(start, out var fromBar);
        var toSongChord = HarmonyTimeline.ChordAt(start + lead, out var toBar);
        var from = StatusChord(fromBlock, fromIncoming, fromSongChord);
        var to = StatusChord(toBlock, toIncoming, toSongChord);
        LegatoLine.Begin(engine, Channel);
        Setup(engine, start);
        var release = start + lead + hold;
        LegatoLine.MoveTo(engine, start, from.Keys,
            HarmonyTimeline.AdjustVelocity(from.Velocity, start), release);
        LegatoLine.MoveTo(engine, start + lead, to.Keys,
            HarmonyTimeline.AdjustVelocity(to.Velocity, start + lead), release);
        if (MusicClock.ActiveProfile is { } profile)
        {
            var now = Time.GetTicksMsec();
            _lastProfileInputTicks = now;
            _lastProfileMoveTicks = now + (ulong)Math.Round((start + lead) * 1000);
            MainFile.Logger.Info(
                $"{profile.Id} legato line: starts in {start:F3}s; first resolution at {start + lead:F3}s" +
                (toSongChord == null ? "." : $", harmony {fromBar}:{fromSongChord?.Name} -> {toBar}:{toSongChord.Name}."));
        }
    }

    private static (double At, string Grid) ReserveNextProfileMove()
    {
        var now = Time.GetTicksMsec();
        var profile = MusicClock.ActiveProfile!;
        var rapid = _lastProfileInputTicks > 0 &&
            (now - _lastProfileInputTicks) / 1000.0 <
            MusicClock.QuarterSeconds * profile.RapidInputQuarterNotes;
        var divisions = rapid ? Math.Max(1, profile.RapidSubdivisionsPerBeat) : 1;
        var step = BeatSeconds / divisions;
        var boundary = now + (ulong)Math.Round(MusicClock.DelayToNextSubdivision(divisions) * 1000);
        var afterPrevious = _lastProfileMoveTicks + (ulong)Math.Round(step * 1000);
        var moveTicks = Math.Max(boundary, afterPrevious);
        _lastProfileInputTicks = now;
        _lastProfileMoveTicks = moveTicks;
        return ((moveTicks - now) / 1000.0,
            divisions == 2 ? "sixteenth-note" : divisions == 1 ? "eighth-note" : $"1/{8 * divisions}-note");
    }

    /// <summary>The single chord that stands for a status in a transition.</summary>
    private static (int[] Keys, int Velocity) StatusChord(
        int block,
        int incoming,
        MusicChordChangeData? songChord)
    {
        var tier = HurtTier(block, incoming);
        if (tier == 0)
            return (ResolvedFor(songChord), incoming <= 0
                ? Profile?.NoIncomingVelocity ?? 70
                : Profile?.ResolvedVelocity ?? 95);
        return tier < 5
            ? (ShapeFor(HurtChords[tier - 1], songChord), HurtVelocities[tier - 1])
            : (ShapeFor(Clash, songChord), HurtVelocities[4]);
    }

    /// <summary>One bar starting at <paramref name="at"/> describing the defense against this turn's attacks.</summary>
    public static void ScheduleStatus(
        SynthEngine engine,
        double at,
        int block,
        int incoming,
        int? channelVolume = null)
    {
        Setup(engine, at, channelVolume);
        var tier = HurtTier(block, incoming);
        var songChord = HarmonyTimeline.ChordAt(at, out _);
        if (incoming <= 0)
        {
            engine.Chord(at, BeatSeconds * HoldBeats, Channel,
                HarmonyTimeline.AdjustVelocity(Profile?.NoIncomingVelocity ?? 70, at),
                ResolvedFor(songChord));
        }
        else if (tier == 0)
        {
            var lead = BeatSeconds * LeadBeats;
            engine.Chord(at, lead, Channel,
                HarmonyTimeline.AdjustVelocity(Profile?.SuspendedVelocity ?? 80, at),
                ShapeFor(Suspended, songChord));
            var resolvedChord = HarmonyTimeline.ChordAt(at + lead, out _);
            engine.Chord(at + lead, BeatSeconds * ResolveBeats, Channel,
                HarmonyTimeline.AdjustVelocity(Profile?.ResolvedVelocity ?? 95, at + lead),
                ResolvedFor(resolvedChord));
        }
        else if (tier < 5)
        {
            engine.Chord(at, BeatSeconds * HoldBeats, Channel,
                HarmonyTimeline.AdjustVelocity(HurtVelocities[tier - 1], at),
                ShapeFor(HurtChords[tier - 1], songChord));
        }
        else
        {
            // Profiled grouping: 1+3 in the fallback, 2+3 in Waterfall's 5/8.
            var lead = BeatSeconds * LeadBeats;
            engine.Chord(at, lead * GateRatio, Channel,
                HarmonyTimeline.AdjustVelocity(HurtVelocities[4], at),
                ShapeFor(Clash, songChord));
            var afterChord = HarmonyTimeline.ChordAt(at + lead, out _);
            engine.Chord(at + lead, BeatSeconds * ResolveBeats * GateRatio, Channel,
                HarmonyTimeline.AdjustVelocity(HurtVelocities[4] - 5, at + lead),
                ShapeFor(ClashAfter, afterChord));
        }
    }

    private static int[] ResolvedFor(MusicChordChangeData? chord)
    {
        if (Profile is not { } profile || chord == null)
            return Resolved;
        return IntentVoicing.ChordKeys(chord, profile.Resolved.FirstOrDefault(60));
    }

    private static int[] ShapeFor(int[] keys, MusicChordChangeData? chord) =>
        Profile is { } profile
            ? IntentVoicing.TransposeFromTonic(keys, profile, chord)
            : keys;

    private static void Setup(SynthEngine engine, double at, int? channelVolume = null) =>
        engine.Schedule(at, s =>
        {
            s.SetProgram(Channel, Midi.Program.AcousticGrandPiano);
            s.SetPan(Channel, 64);
            s.SetVolume(Channel, channelVolume ?? Profile?.DefenseVolume ?? 100);
        });
}
