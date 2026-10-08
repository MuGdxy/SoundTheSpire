using Godot;
using MeltySynth;

namespace SoundTheSpire.SoundTheSpireCode.Audio;

/// <summary>
/// SoundFont synthesizer streamed into Godot's mixer through an AudioStreamGenerator.
/// The game's own audio goes through FMOD, so this is a separate, parallel output.
/// Rendering runs on its own thread so game hitches (loading screens, background FPS limits) don't starve the buffer.
/// </summary>
public sealed class SynthEngine
{
    private const float BufferSeconds = 0.1f;
    private const int MaxChunk = 1024;
    private const int RenderIntervalMs = 5;

    private readonly object _lock = new();
    private readonly Synthesizer _synth;
    private readonly AudioStreamPlayer _player;
    private readonly List<(long Frame, Action<Synthesizer> Action)> _events = new();
    private readonly float[] _left = new float[MaxChunk];
    private readonly float[] _right = new float[MaxChunk];
    private volatile AudioStreamGeneratorPlayback? _playback;
    private long _renderedFrames;

    public static SynthEngine? Instance { get; private set; }

    public int SampleRate { get; }

    public int ActiveVoices
    {
        get
        {
            lock (_lock)
                return _synth.ActiveVoiceCount;
        }
    }

    public long Skips => _playback?.GetSkips() ?? -1;

    public bool IsStreaming => _playback != null;

    private SynthEngine(string soundFontPath, Node root)
    {
        SampleRate = (int)AudioServer.GetMixRate();
        _synth = new Synthesizer(soundFontPath, new SynthesizerSettings(SampleRate) { EnableReverbAndChorus = true });

        var stream = new AudioStreamGenerator { MixRate = SampleRate, BufferLength = BufferSeconds };
        _player = new AudioStreamPlayer { Name = "SoundTheSpireSynth", Stream = stream };
        _player.Ready += OnPlayerReady;
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
        lock (_lock)
        {
            var frame = _renderedFrames + (long)(delaySeconds * SampleRate);
            var index = _events.FindIndex(e => e.Frame > frame);
            _events.Insert(index < 0 ? _events.Count : index, (frame, action));
        }
    }

    /// <summary>Drops everything scheduled and releases sounding notes.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            _events.Clear();
            _synth.NoteOffAll(immediate: false);
        }
    }

    private void OnPlayerReady()
    {
        _player.Play();
        _playback = (AudioStreamGeneratorPlayback)_player.GetStreamPlayback();
        new Thread(RenderLoop) { IsBackground = true, Name = "SoundTheSpire.Synth" }.Start();
    }

    private void RenderLoop()
    {
        while (true)
        {
            try
            {
                Fill();
            }
            catch (Exception e)
            {
                MainFile.Logger.Error($"Synth render failed: {e}");
            }
            Thread.Sleep(RenderIntervalMs);
        }
    }

    private void Fill()
    {
        var playback = _playback;
        if (playback == null)
            return;

        var available = playback.GetFramesAvailable();
        while (available > 0)
        {
            Vector2[] frames;
            lock (_lock)
            {
                RunDueEvents();

                var chunk = Math.Min(available, MaxChunk);
                if (_events.Count > 0)
                    chunk = (int)Math.Clamp(_events[0].Frame - _renderedFrames, 1, chunk);

                _synth.Render(_left.AsSpan(0, chunk), _right.AsSpan(0, chunk));
                frames = new Vector2[chunk];
                for (var i = 0; i < chunk; i++)
                    frames[i] = new Vector2(_left[i], _right[i]);
                _renderedFrames += chunk;
            }
            playback.PushBuffer(frames);
            available -= frames.Length;
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
