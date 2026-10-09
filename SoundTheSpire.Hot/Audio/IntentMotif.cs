using SoundTheSpire.Hot.Combat;

namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// One phrase per enemy, left to right. Waterfall Giant follows its authored B-minor, 82.5-BPM, 5/8 FMOD timeline;
/// unprofiled tracks retain the original 120-BPM 4/4 fallback. Every enemy gets the same number of whole bars
/// (one by default, more only if some enemy needs it); a shorter phrase is padded with rests.
/// Instrument = which enemy (see <see cref="EnemyVoices"/>), pan = where it stands, articulation = intent kind,
/// number of strikes = attack hits.
/// Attacks: low = one note; medium = root + fifth; high = triad; very high = triad + low root.
/// The harder they hit, the lower, louder and longer as well.
/// No percussion in attacks: drums carry no clear pitch, so the damage tier would be lost.
/// </summary>
public static class IntentMotif
{
    public static string LastPitchReport { get; private set; } = "pitch layer unavailable";

    // Per damage tier (the game's attack icon tiers), lightest first. Profiled tracks override these defaults.
    private static readonly int[] AttackRoots = { 72, 64, 55, 48, 40 };
    private static readonly int[] AttackVelocities = { 45, 62, 82, 104, 124 };

    private static double BeatSeconds => MusicClock.BeatSeconds;
    private static double BarSeconds => MusicClock.BarSeconds;
    private static double TempoScale => MusicClock.QuarterSeconds / 0.5; // Existing articulations were authored at 120 BPM.
    private const int Eighths = 2;
    private const int Triplets = 3;
    private const int FullBarHits = 8;
    private static readonly int[] BackingChannels = { 8, 12, 13, 14, 15 };

    /// <summary>
    /// Riff per hit count. D = downbeat, B = backbeat, s = subdivision; spaces only group by ear;
    /// <c>StepsPerBeat</c> is the grid (eighth notes or eighth-note triplets).
    /// </summary>
    private static readonly Dictionary<int, (string Pattern, int StepsPerBeat)> Riffs = new()
    {
        [1] = ("D", Eighths),
        [2] = ("DB", Eighths),
        [3] = ("Dss", Triplets),
        [4] = ("DsBs", Eighths),
        [5] = ("DsBs D", Eighths),
        [6] = ("Dss Bss", Triplets),
        [7] = ("DsBs DsB", Eighths),
        [8] = ("DsBs DsBs", Eighths),
        [9] = ("Dss Bss Dss", Triplets),
        [10] = ("DsBs DsBs Ds", Eighths),
    };

    private const double StrumSpread = 0.008;

    /// <summary>The whole line-up, beginning directly on the next authored bar line when a music clock is available.</summary>
    /// <returns>When the line-up ends, on a bar line.</returns>
    public static double Play(SynthEngine engine, IReadOnlyList<EnemyInfo> enemies)
    {
        EnemyVoices.Assign(enemies);
        var barDelay = MusicClock.DelayToNextBar();
        if (MusicClock.ActiveProfile is { } profile)
            MainFile.Logger.Info($"{profile.Id} summary: waiting {barDelay:F3}s for the next bar.");
        var start = barDelay;
        var measured = enemies.Select(enemy => Compose(enemy, null, null).Length).DefaultIfEmpty(0).Max();
        var slot = BarsFor(measured) * BarSeconds;
        for (var i = 0; i < enemies.Count; i++)
        {
            var at = start + i * slot;
            var chord = HarmonyTimeline.ChordAt(at, out var bar);
            var melody = HarmonyTimeline.MelodyAt(at, out var melodyStep);
            var gainDb = HarmonyTimeline.LoudnessGainDbAt(at, out _);
            if (chord != null)
                MainFile.Logger.Info(
                    $"Enemy {enemies[i].Slot}: harmony bar {bar}, {chord.Name}" +
                    (melody.HasValue ? $", melody step {melodyStep}, MIDI {melody.Value}" : "") +
                    $", loudness gain {gainDb:F1}dB.");
            var phrase = Compose(enemies[i], chord, melody);
            ApplyPitchLayer(phrase, at);
            phrase.SnapPercussionEndToBar(BarSeconds);
            phrase.ApplyGainDb(gainDb);
            Schedule(engine, at, enemies[i], phrase);
        }
        return start + enemies.Count * slot;
    }

    /// <param name="enemies">All living enemies; instruments are ranked across them.</param>
    public static double Play(SynthEngine engine, IReadOnlyList<EnemyInfo> enemies, EnemyInfo enemy)
    {
        EnemyVoices.Assign(enemies);
        var subdivisions = Math.Max(1, MusicClock.ActiveProfile?.SelectionSubdivisionsPerBeat ?? 1);
        var at = MusicClock.DelayToNextSubdivision(subdivisions);
        var chord = HarmonyTimeline.ChordAt(at, out var bar);
        var melody = HarmonyTimeline.MelodyAt(at, out var melodyStep);
        var gainDb = HarmonyTimeline.LoudnessGainDbAt(at, out _);
        if (chord != null)
            MainFile.Logger.Info(
                $"Enemy {enemy.Slot}: selected harmony bar {bar}, {chord.Name}, root {chord.Root}" +
                (melody.HasValue ? $", melody step {melodyStep}, MIDI {melody.Value}" : "") +
                $", loudness gain {gainDb:F1}dB.");
        var phrase = Compose(enemy, chord, melody);
        ApplyPitchLayer(phrase, at);
        phrase.SnapPercussionEndToBar(BarSeconds);
        phrase.ApplyGainDb(gainDb);
        Schedule(engine, at, enemy, phrase);
        return at + BarsFor(phrase.Length) * BarSeconds;
    }

    /// <summary>Schedules only the next bar of the continuously focused melody; no backing or drums retrigger.</summary>
    public static double ContinueFocusedPitch(SynthEngine engine, IReadOnlyList<EnemyInfo> enemies, EnemyInfo enemy)
    {
        EnemyVoices.Assign(enemies);
        const double at = 0;
        var chord = HarmonyTimeline.ChordAt(at, out _);
        var melody = HarmonyTimeline.MelodyAt(at, out _);
        var gainDb = HarmonyTimeline.LoudnessGainDbAt(at, out _);
        var phrase = Compose(enemy, chord, melody);
        ApplyPitchLayer(phrase, at);
        if (!phrase.IsContinuousLead)
            return 0;
        phrase.KeepLeadOnly();
        phrase.ApplyGainDb(gainDb);
        Schedule(engine, at, enemy, phrase);
        return BarSeconds;
    }

    /// <summary>Plays changed intent backing/drums without restarting the continuously focused melody.</summary>
    public static void PlayChangedRhythm(SynthEngine engine, IReadOnlyList<EnemyInfo> enemies, EnemyInfo enemy)
    {
        EnemyVoices.Assign(enemies);
        const double at = 0;
        var chord = HarmonyTimeline.ChordAt(at, out _);
        var melody = HarmonyTimeline.MelodyAt(at, out _);
        var gainDb = HarmonyTimeline.LoudnessGainDbAt(at, out _);
        var phrase = Compose(enemy, chord, melody);
        phrase.RemoveLead();
        phrase.SnapPercussionEndToBar(BarSeconds);
        phrase.ApplyGainDb(gainDb);
        Schedule(engine, at, enemy, phrase);
    }

    /// <summary>Game's attack icon tiers: &lt;5, &lt;10, &lt;20, &lt;40, 40+. Returns 1..5.</summary>
    public static int DamageTier(int totalDamage)
    {
        var thresholds = MusicClock.ActiveProfile?.AttackThresholds ?? new[] { 5, 10, 20, 40 };
        var index = Array.FindIndex(thresholds, threshold => totalDamage < threshold);
        return index < 0 ? 5 : index + 1;
    }

    private static int BarsFor(double seconds) => Math.Max(1, (int)Math.Ceiling(seconds / BarSeconds - 1e-6));

    private static void Schedule(SynthEngine engine, double start, EnemyInfo enemy, Phrase phrase)
    {
        // Channels 0-7 stay clear of the percussion channel.
        var channel = enemy.Slot % 8;
        var backingChannel = BackingChannels[enemy.Slot % BackingChannels.Length];
        var program = EnemyVoices.ProgramOf(enemy);
        var voice = EnemyVoices.SettingsOf(enemy);
        var profile = MusicClock.ActiveProfile;
        phrase.Transpose(voice?.OctaveOffset ?? 0);
        if (voice != null)
        {
            phrase.ConstrainLeadRange(voice.MinNote, voice.MaxNote);
            if (!phrase.IsContinuousLead)
                phrase.ScaleLeadDurations(voice.GateRatio);
        }
        if (phrase.IsContinuousLead)
            LastPitchReport += $" rendered MIDI [{string.Join(",", phrase.LeadKeys)}]";
        var spatial = profile?.Spatial;
        if (spatial != null)
        {
            var gainDb = spatial.BackGainDb +
                enemy.ScreenDepth * (spatial.FrontGainDb - spatial.BackGainDb);
            phrase.ApplyGainDb(gainDb);
        }
        var panMin = spatial?.PanMin ?? 8;
        var panMax = spatial?.PanMax ?? 119;
        var pan = (int)Math.Round(panMin + enemy.ScreenX * (panMax - panMin));
        var initialPan = voice is { PanMotionDepth: > 0 }
            ? Math.Clamp(pan - voice.PanMotionDepth, 0, 127)
            : pan;
        engine.Schedule(start, s =>
        {
            s.SetProgram(channel, program);
            s.SetPan(channel, initialPan);
            var depthReverb = spatial == null ? 0 : (int)Math.Round(
                spatial.BackReverbAdd +
                enemy.ScreenDepth * (spatial.FrontReverbAdd - spatial.BackReverbAdd));
            var brightness = spatial == null ? 64 : (int)Math.Round(
                spatial.BackBrightness +
                enemy.ScreenDepth * (spatial.FrontBrightness - spatial.BackBrightness));
            s.SetReverb(channel, (voice?.Reverb ?? 0) + depthReverb);
            s.SetChorus(channel, voice?.Chorus ?? 0);
            s.SetBrightness(channel, brightness);
            s.SetModulation(channel, voice?.Modulation ?? 0);
            s.SetProgram(backingChannel, profile?.BackingProgram ?? Midi.Program.AcousticGrandPiano);
            s.SetPan(backingChannel, pan);
            s.SetReverb(backingChannel, (profile?.BackingReverb ?? 0) + depthReverb);
            s.SetChorus(backingChannel, profile?.BackingChorus ?? 0);
            s.SetBrightness(backingChannel, brightness);
            s.SetPan(Midi.PercussionChannel, pan);
            s.SetReverb(Midi.PercussionChannel, depthReverb);
            s.SetChorus(Midi.PercussionChannel, 0);
            s.SetBrightness(Midi.PercussionChannel, brightness);
        });
        if (voice is { PanMotionDepth: > 0, PanMotionRateBeats: > 0 } && phrase.Length > 0)
        {
            var configuredStep = MusicClock.BeatSeconds * voice.PanMotionRateBeats;
            var step = Math.Min(configuredStep, Math.Max(phrase.Length / 2.0, 0.02));
            var direction = 1;
            for (var at = step; at < phrase.Length; at += step)
            {
                var movingPan = Math.Clamp(pan + direction * voice.PanMotionDepth, 0, 127);
                engine.Schedule(start + at, synth => synth.SetPan(channel, movingPan));
                direction *= -1;
            }
            engine.Schedule(start + phrase.Length, synth => synth.SetPan(channel, pan));
        }
        phrase.ScheduleOn(engine, start, channel, backingChannel);
    }

    private static Phrase Compose(EnemyInfo enemy, MusicChordChangeData? chord, int? melodyNote)
    {
        var phrase = new Phrase();
        var t = 0.0;
        foreach (var intent in enemy.Intents)
            t = ComposeIntent(phrase, t, enemy, intent, chord, melodyNote);
        return phrase;
    }

    private static double ComposeIntent(
        Phrase phrase,
        double t,
        EnemyInfo enemy,
        IntentInfo intent,
        MusicChordChangeData? chord,
        int? melodyNote)
    {
        var profile = MusicClock.ActiveProfile;
        switch (intent.Kind)
        {
            case IntentKind.Attack:
                return ComposeAttack(phrase, t, enemy, intent, chord, melodyNote);
            case IntentKind.Defend:
                phrase.Note(t, 0.5 * TempoScale, EnemyVoices.BalancedVelocity(enemy, 70),
                    profile != null
                        ? IntentVoicing.TransposeFromTonic(profile.DefendNotes, profile, chord)
                        : new[] { 36, 43 });
                return t + 0.6 * TempoScale;
            case IntentKind.Buff:
                var buff = chord != null && profile != null
                    ? IntentVoicing.ChordKeys(chord, profile.BuffNotes.FirstOrDefault(60))
                    : profile?.BuffNotes ?? new[] { 60, 64, 67, 72 };
                return Arpeggio(phrase, t, EnemyVoices.BalancedVelocity(enemy, 85),
                    buff);
            case IntentKind.Debuff:
                var debuff = chord != null && profile != null
                    ? IntentVoicing.ChordKeys(chord, profile.DebuffNotes.LastOrDefault(72)).Reverse().ToArray()
                    : profile?.DebuffNotes ?? new[] { 72, 68, 65, 60 };
                return Arpeggio(phrase, t, EnemyVoices.BalancedVelocity(enemy, 85),
                    debuff);
            default:
                phrase.Note(t, 0.12 * TempoScale, EnemyVoices.BalancedVelocity(enemy, 45),
                    profile != null
                        ? IntentVoicing.TransposeFromTonic([profile.UnknownNote], profile, chord)[0]
                        : 52);
                return t + 0.3 * TempoScale;
        }
    }

    private static double ComposeAttack(
        Phrase phrase,
        double t,
        EnemyInfo enemy,
        IntentInfo intent,
        MusicChordChangeData? chord,
        int? melodyNote)
    {
        var tier = DamageTier(intent.TotalDamage);
        var profile = MusicClock.ActiveProfile;
        var voicing = IntentVoicing.Attack(profile, tier, chord, melodyNote, AttackRoots);
        var velocity = EnemyVoices.BalancedVelocity(enemy,
            (profile?.AttackVelocities ?? AttackVelocities)[tier - 1]);

        var hits = Math.Clamp(intent.Hits, 1, 16);
        if (hits == 1)
        {
            var duration = (0.2 + tier * 0.1) * TempoScale;
            phrase.Note(t, duration, velocity, voicing.Lead);
            if (voicing.Backing.Length > 0)
                phrase.BackingNote(t, duration,
                    BackingVelocity(voicing.BackingVelocity), voicing.Backing);
            if (profile?.MultiHitPercussionTiers.ElementAtOrDefault(tier - 1) is { } percussion)
                phrase.Percussion(
                    t,
                    duration * percussion.GateRatio,
                    percussion.OpenVelocity,
                    percussion.OpenNotes);
            return t + duration + 0.1 * TempoScale;
        }
        return ComposeRiff(phrase, t, velocity, hits, voicing, tier, enemy);
    }

    /// <summary>
    /// Multi-hit attacks as a power-chord riff from <see cref="Riffs"/>, one strike per hit, each lasting a full step.
    /// Above the table, full 8-hit bars are played first and the remainder uses its own riff.
    /// </summary>
    private static double ComposeRiff(
        Phrase phrase,
        double t,
        int velocity,
        int hits,
        IntentVoicing.AttackVoicing voicing,
        int tier,
        EnemyInfo enemy)
    {
        var start = t;
        var maxInTable = Riffs.Keys.Max();
        while (hits > maxInTable)
        {
            t = ComposePattern(phrase, t, Riffs[FullBarHits], tier);
            hits -= FullBarHits;
        }
        t = ComposePattern(phrase, t, Riffs[hits], tier);

        var duration = Math.Max(0.05, (t - start) * 0.95);
        var strumSpread = MusicClock.ActiveProfile?.StrumSpreadSeconds ?? StrumSpread;
        phrase.Strum(start, duration, velocity, strumSpread, voicing.Lead);
        if (voicing.Backing.Length > 0)
            phrase.BackingStrum(start, duration,
                BackingVelocity(voicing.BackingVelocity),
                strumSpread, voicing.Backing);
        return t;
    }

    private static double ComposePattern(
        Phrase phrase,
        double t,
        (string Pattern, int StepsPerBeat) riff,
        int tier)
    {
        var step = BeatSeconds / riff.StepsPerBeat;
        var profile = MusicClock.ActiveProfile;
        var percussion = profile?.MultiHitPercussionTiers.ElementAtOrDefault(tier - 1);
        foreach (var strike in riff.Pattern.Where(c => c != ' '))
        {
            if (percussion != null)
            {
                var backbeat = strike == 'B';
                var downbeat = strike == 'D';
                phrase.Percussion(
                    t,
                    step * percussion.GateRatio,
                    downbeat
                        ? percussion.OpenVelocity
                        : backbeat
                            ? percussion.BackbeatVelocity
                            : percussion.MutedVelocity,
                    downbeat
                        ? percussion.OpenNotes
                        : backbeat
                            ? percussion.BackbeatNotes
                            : percussion.MutedNotes);
            }
            t += step;
        }
        return t;
    }

    private static double Arpeggio(Phrase phrase, double t, int velocity, params int[] keys)
    {
        for (var k = 0; k < keys.Length; k++)
            phrase.Note(t + k * 0.08 * TempoScale, 0.15 * TempoScale, velocity, keys[k]);
        return t + (keys.Length * 0.08 + 0.2) * TempoScale;
    }

    private static int BackingVelocity(int velocity)
    {
        var gain = Math.Pow(10, (MusicClock.ActiveProfile?.BackingGainDb ?? 0) / 40.0);
        return Math.Clamp((int)Math.Round(velocity * gain), 1, 127);
    }

    private static void ApplyPitchLayer(Phrase phrase, double timelineOffset)
    {
        if (MusicClock.ActiveProfile is not { } profile)
            return;
        if (profile.IntentPitchMode == IntentPitchMode.Tonic)
        {
            phrase.SustainLead(BarSeconds + Math.Min(0.08, BeatSeconds * 0.25));
            LastPitchReport = $"tonic pitch class {profile.TonicPitchClass}";
            return;
        }
        if (profile.IntentPitchMode != IntentPitchMode.Melody)
            return;
        var subdivisions = Math.Max(
            1,
            MusicClock.ActiveProfile?.Harmony?.MelodySubdivisionsPerBeat ?? 1);
        var noteStep = BeatSeconds / subdivisions;
        var count = MusicClock.BeatsPerBar * subdivisions;
        var notes = new List<int>(count);
        var steps = new List<int>(count);
        for (var stepIndex = 0; stepIndex < count; stepIndex++)
        {
            if (HarmonyTimeline.MelodyAt(timelineOffset + stepIndex * noteStep, out var step) is not { } note)
            {
                LastPitchReport = "melody unavailable";
                return;
            }
            notes.Add(note);
            steps.Add(step);
        }
        LastPitchReport =
            $"melody steps {steps[0]}..{steps[^1]} MIDI [{string.Join(",", notes)}]";
        var configuredOverlap = MusicClock.ActiveProfile?.MelodyOverlapSeconds ?? 0.08;
        phrase.ReplaceLeadWithMelody(notes, noteStep, Math.Min(configuredOverlap, noteStep * 0.75));
    }
}
