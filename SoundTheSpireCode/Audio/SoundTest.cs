namespace SoundTheSpire.SoundTheSpireCode.Audio;

/// <summary>Spike: three timbres at three pan positions, to check quality, polyphony, panning and latency.</summary>
public static class SoundTest
{
    private const int PianoChannel = 0;
    private const int GuitarChannel = 1;

    public static void Play(SynthEngine engine)
    {
        engine.Schedule(0, s =>
        {
            s.SetProgram(PianoChannel, Midi.Program.AcousticGrandPiano);
            s.SetPan(PianoChannel, 0);
            s.SetProgram(GuitarChannel, Midi.Program.DistortionGuitar);
            s.SetPan(GuitarChannel, 64);
            s.SetPan(Midi.PercussionChannel, 127);
        });

        engine.Chord(0.0, 0.7, PianoChannel, 100, 60, 64, 67);
        engine.Chord(0.9, 0.7, GuitarChannel, 110, 40, 47, 52);
        engine.Chord(1.8, 0.5, Midi.PercussionChannel, 120, Midi.Drum.BassDrum, Midi.Drum.CrashCymbal);
    }
}
