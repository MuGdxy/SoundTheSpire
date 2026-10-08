namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// The defensive side, on piano in C like the intent intro. Block gains themselves already have the game's own sound.
/// <list type="bullet">
/// <item>Resolved (block covers incoming): sus4 → C major.</item>
/// <item>Partly covered: sus4 left hanging.</item>
/// <item>No block at all against an attack: a dissonant phrase.</item>
/// <item>No incoming attack: C major alone.</item>
/// </list>
/// The turn summary ends with one of these, and <see cref="Combat.DefenseMonitor"/> plays the new one whenever the
/// verdict changes during the turn.
/// </summary>
public static class BlockMotif
{
    private const int Channel = 10;
    private const double BeatSeconds = 60.0 / 120;
    public const double BarSeconds = BeatSeconds * 4;

    private static readonly int[] Suspended = { 60, 65, 67 };    // C F G
    private static readonly int[] Resolved = { 60, 64, 67, 72 }; // C E G C
    private static readonly int[] Clash = { 60, 61, 66 };        // C Db Gb
    private static readonly int[] ClashAfter = { 59, 60, 65 };   // B C F

    public enum Verdict { Resolved, Partial, Undefended }

    public static Verdict Judge(int block, int incoming) =>
        block >= incoming ? Verdict.Resolved : block > 0 ? Verdict.Partial : Verdict.Undefended;

    public static void PlayStatus(SynthEngine engine, int block, int incoming)
    {
        engine.Stop();
        ScheduleStatus(engine, 0, block, incoming);
    }

    /// <summary>One bar starting at <paramref name="at"/> describing the defense against this turn's attacks.</summary>
    public static void ScheduleStatus(SynthEngine engine, double at, int block, int incoming)
    {
        var verdict = Judge(block, incoming);
        if (incoming <= 0)
        {
            Setup(engine, at);
            engine.Chord(at, BeatSeconds * 3, Channel, 70, Resolved);
        }
        else if (verdict == Verdict.Resolved)
        {
            ScheduleResolved(engine, at);
        }
        else if (verdict == Verdict.Partial)
        {
            Setup(engine, at);
            engine.Chord(at, BeatSeconds * 3, Channel, 80, Suspended);
        }
        else
        {
            // Same rhythm as the intro: a quarter, then a dotted half.
            Setup(engine, at);
            engine.Chord(at, BeatSeconds * 0.95, Channel, 95, Clash);
            engine.Chord(at + BeatSeconds, BeatSeconds * 3 * 0.95, Channel, 90, ClashAfter);
        }
    }

    private static void ScheduleResolved(SynthEngine engine, double at)
    {
        Setup(engine, at);
        engine.Chord(at, BeatSeconds, Channel, 80, Suspended);
        engine.Chord(at + BeatSeconds, BeatSeconds * 2, Channel, 95, Resolved);
    }

    private static void Setup(SynthEngine engine, double at) =>
        engine.Schedule(at, s =>
        {
            s.SetProgram(Channel, Midi.Program.AcousticGrandPiano);
            s.SetPan(Channel, 64);
        });
}
