namespace SoundTheSpire.Hot.Audio;

/// <summary>Notes for one channel at times relative to the phrase start, so a phrase can be measured before it is placed.</summary>
public sealed class Phrase
{
    private readonly List<(double At, double Duration, int Velocity, int Key)> _notes = new();
    private readonly List<(double At, double Duration, int Velocity, int Key)> _backing = new();
    private readonly List<(double At, double Duration, int Velocity, int Key)> _percussion = new();

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

    public void BackingNote(double at, double duration, int velocity, params int[] keys)
    {
        foreach (var key in keys)
            _backing.Add((at, duration, velocity, key));
        Length = Math.Max(Length, at + duration);
    }

    public void BackingStrum(double at, double duration, int velocity, double spread, params int[] keys)
    {
        var ordered = keys.Order().ToArray();
        for (var k = 0; k < ordered.Length; k++)
            BackingNote(at + k * spread, duration, velocity, ordered[k]);
    }

    public void Percussion(double at, double duration, int velocity, params int[] keys)
    {
        foreach (var key in keys)
            _percussion.Add((at, duration, velocity, key));
        Length = Math.Max(Length, at + duration);
    }

    public void ApplyGainDb(double gainDb)
    {
        var velocityScale = Math.Pow(10, gainDb / 40.0);
        for (var i = 0; i < _notes.Count; i++)
        {
            var note = _notes[i];
            _notes[i] = (note.At, note.Duration,
                Math.Clamp((int)Math.Round(note.Velocity * velocityScale), 1, 127), note.Key);
        }
        for (var i = 0; i < _percussion.Count; i++)
        {
            var note = _percussion[i];
            _percussion[i] = (note.At, note.Duration,
                Math.Clamp((int)Math.Round(note.Velocity * velocityScale), 1, 127), note.Key);
        }
        for (var i = 0; i < _backing.Count; i++)
        {
            var note = _backing[i];
            _backing[i] = (note.At, note.Duration,
                Math.Clamp((int)Math.Round(note.Velocity * velocityScale), 1, 127), note.Key);
        }
    }

    public void Transpose(int semitones)
    {
        if (semitones == 0)
            return;
        for (var i = 0; i < _notes.Count; i++)
        {
            var note = _notes[i];
            _notes[i] = (note.At, note.Duration, note.Velocity,
                Math.Clamp(note.Key + semitones, 0, 127));
        }
        for (var i = 0; i < _backing.Count; i++)
        {
            var note = _backing[i];
            _backing[i] = (note.At, note.Duration, note.Velocity,
                Math.Clamp(note.Key + semitones, 0, 127));
        }
    }

    public void ConstrainLeadRange(int minNote, int maxNote)
    {
        if (maxNote <= minNote)
            return;
        for (var i = 0; i < _notes.Count; i++)
        {
            var note = _notes[i];
            var key = note.Key;
            while (key < minNote)
                key += 12;
            while (key > maxNote)
                key -= 12;
            _notes[i] = (note.At, note.Duration, note.Velocity,
                Math.Clamp(key, minNote, maxNote));
        }
    }

    public void ScaleLeadDurations(double ratio)
    {
        if (Math.Abs(ratio - 1) < 0.001)
            return;
        ratio = Math.Clamp(ratio, 0.1, 1.2);
        for (var i = 0; i < _notes.Count; i++)
        {
            var note = _notes[i];
            _notes[i] = (note.At, note.Duration * ratio, note.Velocity, note.Key);
        }
    }

    public void ScheduleOn(SynthEngine engine, double start, int channel, int? backingChannel = null)
    {
        foreach (var (at, duration, velocity, key) in _notes)
            engine.Chord(start + at, duration, channel, velocity, key);
        if (backingChannel.HasValue)
            foreach (var (at, duration, velocity, key) in _backing)
                engine.Chord(start + at, duration, backingChannel.Value, velocity, key);
        foreach (var (at, duration, velocity, key) in _percussion)
            engine.Chord(start + at, duration, Midi.PercussionChannel, velocity, key);
    }
}
