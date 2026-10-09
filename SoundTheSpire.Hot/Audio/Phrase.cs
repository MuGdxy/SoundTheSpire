namespace SoundTheSpire.Hot.Audio;

/// <summary>Notes for one channel at times relative to the phrase start, so a phrase can be measured before it is placed.</summary>
public sealed class Phrase
{
    private readonly List<(double At, double Duration, int Velocity, int Key)> _notes = new();
    private readonly List<(double At, double Duration, int Velocity, int Key)> _backing = new();
    private readonly List<(double At, double Duration, int Velocity, int Key)> _percussion = new();
    public bool IsLegatoMelody { get; private set; }
    public IReadOnlyList<int> LeadKeys => _notes.Select(note => note.Key).ToArray();

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

    public void ReplaceLeadWithMelody(IReadOnlyList<int> notes, double step, double overlap)
    {
        if (notes.Count == 0 || _notes.Count == 0)
            return;
        var velocity = _notes.Max(note => note.Velocity);
        _notes.Clear();
        IsLegatoMelody = true;
        for (var start = 0; start < notes.Count;)
        {
            if (notes[start] < 0)
            {
                start++;
                continue;
            }
            var end = start + 1;
            while (end < notes.Count && notes[end] == notes[start])
                end++;
            var duration = (end - start) * step +
                (end < notes.Count && notes[end] >= 0 ? overlap : 0);
            _notes.Add((start * step, duration, velocity, notes[start]));
            start = end;
        }
        Length = Math.Max(Length, notes.Count * step);
    }

    public void SnapPercussionEndToBar(double barSeconds)
    {
        if (_percussion.Count == 0 || barSeconds <= 0)
            return;
        var naturalEnd = _percussion.Max(note => note.At + note.Duration);
        var targetEnd = Math.Max(barSeconds, Math.Ceiling(naturalEnd / barSeconds) * barSeconds);
        Length = Math.Max(Length, targetEnd);
    }

    public void KeepLeadOnly()
    {
        _backing.Clear();
        _percussion.Clear();
        Length = _notes.Count == 0 ? 0 : _notes.Max(note => note.At + note.Duration);
    }

    public void RemoveLead()
    {
        _notes.Clear();
        var backingEnd = _backing.Count == 0 ? 0 : _backing.Max(note => note.At + note.Duration);
        var percussionEnd = _percussion.Count == 0 ? 0 : _percussion.Max(note => note.At + note.Duration);
        Length = Math.Max(backingEnd, percussionEnd);
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
