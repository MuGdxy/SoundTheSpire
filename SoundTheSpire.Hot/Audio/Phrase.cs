namespace SoundTheSpire.Hot.Audio;

/// <summary>Notes for one channel at times relative to the phrase start, so a phrase can be measured before it is placed.</summary>
public sealed class Phrase
{
    private readonly List<(double At, double Duration, int Velocity, int Key)> _notes = new();

    /// <summary>When the last note ends.</summary>
    public double Length { get; private set; }

    public void Note(double at, double duration, int velocity, params int[] keys)
    {
        foreach (var key in keys)
            _notes.Add((at, duration, velocity, key));
        Length = Math.Max(Length, at + duration);
    }

    /// <summary>Downstroke: keys sound low to high, <paramref name="spread"/> seconds apart.</summary>
    public void Strum(double at, double duration, int velocity, double spread, params int[] keys)
    {
        var ordered = keys.Order().ToArray();
        for (var k = 0; k < ordered.Length; k++)
            Note(at + k * spread, duration, velocity, ordered[k]);
    }

    public void ScheduleOn(SynthEngine engine, double start, int channel)
    {
        foreach (var (at, duration, velocity, key) in _notes)
            engine.Chord(start + at, duration, channel, velocity, key);
    }
}
