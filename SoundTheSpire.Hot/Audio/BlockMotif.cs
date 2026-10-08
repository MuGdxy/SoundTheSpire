namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// The defensive side, on piano in C like the intent intro. Block gains themselves already have the game's own sound.
/// <list type="bullet">
/// <item>Resolved (block covers incoming): sus4 → C major. Also played live by <see cref="Combat.DefenseMonitor"/>.</item>
/// <item>Partly covered: sus4 left hanging.</item>
/// <item>No block at all against an attack: a dissonant phrase.</item>
/// <item>No incoming attack: C major alone.</item>
/// </list>
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

    public static void PlayResolved(SynthEngine engine)
    {
        engine.Stop();
        ScheduleResolved(engine, 0);
    }

    /// <summary>One bar starting at <paramref name="at"/> describing the defense against this turn's attacks.</summary>
    public static void ScheduleStatus(SynthEngine engine, double at, int block, int incoming)
    {
        if (incoming <= 0)
        {
            Setup(engine, at);
            engine.Chord(at, BeatSeconds * 3, Channel, 70, Resolved);
        }
        else if (block >= incoming)
        {
            ScheduleResolved(engine, at);
        }
        else if (block > 0)
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
