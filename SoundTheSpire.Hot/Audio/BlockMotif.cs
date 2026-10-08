namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// The defensive side, on piano in C like the intent intro. Block gains themselves already have the game's own sound.
/// The verdict is the damage that would get through (incoming minus projected block), graded with the same tiers as
/// attacks (<see cref="IntentMotif.DamageTier"/>), from fully consonant to fully dissonant:
/// <list type="bullet">
/// <item>Nothing gets through: sus4 → C major (C major alone when nothing is incoming).</item>
/// <item>Tiers 1–4: one held chord, rougher with every tier: sus4, diminished, tritone + major 7th, minor 2nd + tritone.</item>
/// <item>Tier 5: two clashing chords over a low C.</item>
/// </list>
/// The turn summary ends with one of these, and <see cref="Combat.DefenseMonitor"/> plays the new one whenever the
/// tier changes during the turn.
/// </summary>
public static class BlockMotif
{
    private const int Channel = 10;
    private const double BeatSeconds = 60.0 / 120;
    public const double BarSeconds = BeatSeconds * 4;

    private static readonly int[] Suspended = { 60, 65, 67 };    // C F G
    private static readonly int[] Resolved = { 60, 64, 67, 72 }; // C E G C

    private static readonly int[][] HurtChords =
    {
        new[] { 60, 65, 67 }, // C F G    sus4
        new[] { 60, 63, 66 }, // C Eb Gb  diminished
        new[] { 60, 66, 71 }, // C Gb B   tritone + major 7th
        new[] { 60, 61, 66 }, // C Db Gb  minor 2nd + tritone
    };
    private static readonly int[] HurtVelocities = { 72, 80, 88, 96, 110 };
    private static readonly int[] Clash = { 48, 60, 61, 66 };      // C | C Db Gb
    private static readonly int[] ClashAfter = { 47, 59, 60, 65 }; // B | B C F

    /// <summary>0 when block holds, otherwise the attack tier (1–5) of the damage that gets through.</summary>
    public static int HurtTier(int block, int incoming) =>
        incoming > block ? IntentMotif.DamageTier(incoming - block) : 0;

    public static void PlayStatus(SynthEngine engine, int block, int incoming)
    {
        engine.Stop();
        ScheduleStatus(engine, 0, block, incoming);
    }

    /// <summary>One bar starting at <paramref name="at"/> describing the defense against this turn's attacks.</summary>
    public static void ScheduleStatus(SynthEngine engine, double at, int block, int incoming)
    {
        Setup(engine, at);
        var tier = HurtTier(block, incoming);
        if (incoming <= 0)
        {
            engine.Chord(at, BeatSeconds * 3, Channel, 70, Resolved);
        }
        else if (tier == 0)
        {
            engine.Chord(at, BeatSeconds, Channel, 80, Suspended);
            engine.Chord(at + BeatSeconds, BeatSeconds * 2, Channel, 95, Resolved);
        }
        else if (tier < 5)
        {
            engine.Chord(at, BeatSeconds * 3, Channel, HurtVelocities[tier - 1], HurtChords[tier - 1]);
        }
        else
        {
            // Same rhythm as the intro: a quarter, then a dotted half.
            engine.Chord(at, BeatSeconds * 0.95, Channel, HurtVelocities[4], Clash);
            engine.Chord(at + BeatSeconds, BeatSeconds * 3 * 0.95, Channel, HurtVelocities[4] - 5, ClashAfter);
        }
    }

    private static void Setup(SynthEngine engine, double at) =>
        engine.Schedule(at, s =>
        {
            s.SetProgram(Channel, Midi.Program.AcousticGrandPiano);
            s.SetPan(Channel, 64);
        });
}
