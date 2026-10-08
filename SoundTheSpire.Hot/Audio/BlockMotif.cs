namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// "Defensive pressure resolved": plays once when the player's block first covers all incoming attack damage.
/// Block gains themselves already have the game's own sound, so nothing else is added. Sus4 → major cadence in C,
/// like the intent intro.
/// </summary>
public static class BlockMotif
{
    private const int Channel = 10;
    private const double BeatSeconds = 60.0 / 120;

    private static readonly int[] Suspended = { 60, 65, 67 };    // C F G
    private static readonly int[] Resolved = { 60, 64, 67, 72 }; // C E G C

    public static bool Resolves(int previousBlock, int block, int incoming) =>
        incoming > 0 && previousBlock < incoming && block >= incoming;

    public static void PlayResolved(SynthEngine engine)
    {
        engine.Stop();
        engine.Schedule(0, s =>
        {
            s.SetProgram(Channel, Midi.Program.AcousticGrandPiano);
            s.SetPan(Channel, 64);
        });
        engine.Chord(0, BeatSeconds, Channel, 80, Suspended);
        engine.Chord(BeatSeconds, BeatSeconds * 2, Channel, 95, Resolved);
    }
}
