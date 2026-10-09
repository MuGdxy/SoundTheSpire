namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// A chord line that can keep moving: each <see cref="MoveTo"/> goes legato from whatever is sounding at that moment
/// to the new chord (common tones held, the rest overlapping slightly). Moves are numbered: one that comes due after a
/// later-issued move has already sounded is skipped, and only the latest-issued release counts, so moves made before
/// the line is released join up into one phrase. The sounding set is tracked inside the synth callbacks, so it is
/// exact even when a move lands halfway through an earlier one.
/// <para>
/// Anything else that stops the engine has to go through <see cref="Interrupt"/>, which also breaks the line.
/// </para>
/// </summary>
public static class LegatoLine
{
    private const double Overlap = 0.08;

    private static readonly object Gate = new();
    private static readonly HashSet<int> Sounding = new();
    private static int _issued;
    private static int _applied;
    private static bool _open;
    private static int _channel;

    /// <summary>True while a line is still sounding and can be continued.</summary>
    public static bool IsOpen
    {
        get { lock (Gate) return _open; }
    }

    /// <summary>Releases only the defensive line's MIDI channel; other audio layers keep playing.</summary>
    public static void Interrupt(SynthEngine engine)
    {
        var channel = -1;
        lock (Gate)
        {
            _applied = ++_issued;
            if (_open)
                channel = _channel;
            _open = false;
            Sounding.Clear();
        }
        if (channel >= 0)
            engine.Schedule(0, synth => synth.NoteOffAll(channel, immediate: false));
    }

    /// <summary>Starts a new defensive line, replacing only the prior line on this layer.</summary>
    public static void Begin(SynthEngine engine, int channel)
    {
        Interrupt(engine);
        lock (Gate)
        {
            _open = true;
            _channel = channel;
        }
    }

    /// <summary>
    /// Moves to <paramref name="keys"/> at <paramref name="at"/> seconds from now and releases the line at
    /// <paramref name="releaseAt"/> unless a later move pushes the release back.
    /// </summary>
    public static void MoveTo(SynthEngine engine, double at, int[] keys, int velocity, double releaseAt)
    {
        int seq;
        int channel;
        lock (Gate)
        {
            seq = ++_issued;
            channel = _channel;
            _open = true;
        }

        engine.Schedule(at, s =>
        {
            int[] leaving;
            lock (Gate)
            {
                if (seq < _applied)
                    return;
                _applied = seq;
                foreach (var key in keys.Where(k => !Sounding.Contains(k)))
                    s.NoteOn(channel, key, velocity);
                leaving = Sounding.Except(keys).ToArray();
                Sounding.Clear();
                Sounding.UnionWith(keys);
            }
            if (leaving.Length > 0)
                engine.Schedule(Overlap, s2 =>
                {
                    lock (Gate)
                    {
                        foreach (var key in leaving.Where(k => !Sounding.Contains(k)))
                            s2.NoteOff(channel, key);
                    }
                });
        });

        engine.Schedule(releaseAt, s =>
        {
            lock (Gate)
            {
                if (seq != _issued)
                    return;
                foreach (var key in Sounding)
                    s.NoteOff(channel, key);
                Sounding.Clear();
                _open = false;
            }
        });
    }
}
