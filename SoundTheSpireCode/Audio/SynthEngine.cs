using Godot;
using MeltySynth;

namespace SoundTheSpire.SoundTheSpireCode.Audio;

/// <summary>
/// SoundFont synthesizer streamed into Godot's mixer through an AudioStreamGenerator.
/// The game's own audio goes through FMOD, so this is a separate, parallel output.
/// </summary>
public sealed class SynthEngine
{
    private const float BufferSeconds = 0.08f;
    private const int MaxChunk = 2048;

    private readonly Synthesizer _synth;
    private readonly AudioStreamPlayer _player;
    private readonly List<(long Frame, Action<Synthesizer> Action)> _events = new();
    private readonly float[] _left = new float[MaxChunk];
    private readonly float[] _right = new float[MaxChunk];
    private AudioStreamGeneratorPlayback? _playback;
    private long _renderedFrames;

    public static SynthEngine? Instance { get; private set; }

    public int SampleRate { get; }

    public int ActiveVoices => _synth.ActiveVoiceCount;

    public long Skips => _playback?.GetSkips() ?? -1;

    public bool IsStreaming => _playback != null;

    private SynthEngine(string soundFontPath, Node root)
    {
        SampleRate = (int)AudioServer.GetMixRate();
        _synth = new Synthesizer(soundFontPath, new SynthesizerSettings(SampleRate) { EnableReverbAndChorus = true });

        var stream = new AudioStreamGenerator { MixRate = SampleRate, BufferLength = BufferSeconds };
        _player = new AudioStreamPlayer { Name = "SoundTheSpireSynth", Stream = stream };
        root.CallDeferred(Node.MethodName.AddChild, _player);
    }

    public static SynthEngine Create(string soundFontPath, Node root)
    {
        Instance = new SynthEngine(soundFontPath, root);
        return Instance;
    }

    /// <summary>Runs <paramref name="action"/> on the synthesizer after <paramref name="delaySeconds"/> of audio.</summary>
    public void Schedule(double delaySeconds, Action<Synthesizer> action)
    {
        var frame = _renderedFrames + (long)(delaySeconds * SampleRate);
        var index = _events.FindIndex(e => e.Frame > frame);
        _events.Insert(index < 0 ? _events.Count : index, (frame, action));
    }

    public void Pump()
    {
        if (!_player.IsInsideTree())
            return;

        if (_playback == null)
        {
            _player.Play();
            _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        }

        var available = _playback.GetFramesAvailable();
        while (available > 0)
        {
            RunDueEvents();

            var chunk = Math.Min(available, MaxChunk);
            if (_events.Count > 0)
                chunk = (int)Math.Clamp(_events[0].Frame - _renderedFrames, 1, chunk);

            _synth.Render(_left.AsSpan(0, chunk), _right.AsSpan(0, chunk));

            var frames = new Vector2[chunk];
            for (var i = 0; i < chunk; i++)
                frames[i] = new Vector2(_left[i], _right[i]);
            _playback.PushBuffer(frames);

            _renderedFrames += chunk;
            available -= chunk;
        }
    }

    private void RunDueEvents()
    {
        while (_events.Count > 0 && _events[0].Frame <= _renderedFrames)
        {
            var action = _events[0].Action;
            _events.RemoveAt(0);
            action(_synth);
        }
    }
}
