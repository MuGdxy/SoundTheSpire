using MeltySynth;

namespace SoundTheSpire.Hot.Audio;

public static class Midi
{
    public const int PercussionChannel = 9;

    public static class Program
    {
        public const int AcousticGrandPiano = 0;
        public const int ChurchOrgan = 19;
        public const int JazzGuitar = 26;
        public const int CleanGuitar = 27;
        public const int MutedGuitar = 28;
        public const int OverdrivenGuitar = 29;
        public const int DistortionGuitar = 30;
        public const int GuitarHarmonics = 31;
        public const int Cello = 42;
        public const int PizzicatoStrings = 45;
        public const int StringEnsemble = 48;
        public const int Trumpet = 56;
        public const int FrenchHorn = 60;
        public const int BrassSection = 61;
        public const int Oboe = 68;
        public const int SquareLead = 80;
    }

    public static class Drum
    {
        public const int BassDrum = 36;
        public const int CrashCymbal = 49;
        public const int MutedTriangle = 80;
        public const int OpenTriangle = 81;
    }

    public static void SetProgram(this Synthesizer synth, int channel, int program) =>
        synth.ProcessMidiMessage(channel, 0xC0, program, 0);

    /// <param name="pan">0 = hard left, 64 = center, 127 = hard right.</param>
    public static void SetPan(this Synthesizer synth, int channel, int pan) =>
        synth.ProcessMidiMessage(channel, 0xB0, 0x0A, pan);

    public static void SetVolume(this Synthesizer synth, int channel, int amount) =>
        synth.ProcessMidiMessage(channel, 0xB0, 7, Math.Clamp(amount, 0, 127));

    public static void SetReverb(this Synthesizer synth, int channel, int amount) =>
        synth.ProcessMidiMessage(channel, 0xB0, 91, Math.Clamp(amount, 0, 127));

    public static void SetChorus(this Synthesizer synth, int channel, int amount) =>
        synth.ProcessMidiMessage(channel, 0xB0, 93, Math.Clamp(amount, 0, 127));

    public static void SetBrightness(this Synthesizer synth, int channel, int amount) =>
        synth.ProcessMidiMessage(channel, 0xB0, 74, Math.Clamp(amount, 0, 127));

    public static void SetModulation(this Synthesizer synth, int channel, int amount) =>
        synth.ProcessMidiMessage(channel, 0xB0, 1, Math.Clamp(amount, 0, 127));

    public static void Chord(this SynthEngine engine, double at, double duration, int channel, int velocity, params int[] keys)
    {
        engine.Schedule(at, s =>
        {
            foreach (var key in keys)
                s.NoteOn(channel, key, velocity);
        });
        engine.Schedule(at + duration, s =>
        {
            foreach (var key in keys)
                s.NoteOff(channel, key);
        });
    }}
