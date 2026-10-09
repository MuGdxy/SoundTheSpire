using Godot;

namespace SoundTheSpire.Hot.Audio;

/// <summary>Sequential, non-positional playback for pre-generated encounter voice clips.</summary>
public static class VoicePlayback
{
    private static readonly Queue<string> Queue = new();

    private static AudioStreamPlayer? _player;
    private static int _gapMs = 180;
    private static ulong _nextClipTicks;
    private static bool _wasPlaying;

    public static bool IsBusy => Queue.Count > 0 || (_player?.Playing ?? false);
    public static int QueuedClips => Queue.Count;

    public static void Initialize(int gapMs, float volumeDb)
    {
        _gapMs = gapMs;
        if (_player != null && GodotObject.IsInstanceValid(_player))
        {
            _player.VolumeDb = volumeDb;
            return;
        }
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        _player = new AudioStreamPlayer
        {
            Name = "SoundTheSpireEncounterVoice",
            VolumeDb = volumeDb,
        };
        tree.Root.AddChild(_player);
    }

    public static void Configure(int gapMs, float volumeDb)
    {
        _gapMs = gapMs;
        if (_player != null && GodotObject.IsInstanceValid(_player))
            _player.VolumeDb = volumeDb;
    }

    public static void Enqueue(IEnumerable<string> clips)
    {
        foreach (var clip in clips)
            Queue.Enqueue(clip);
    }

    public static void Poll()
    {
        if (_player == null || !GodotObject.IsInstanceValid(_player) || !_player.IsInsideTree())
            return;

        if (_player.Playing)
        {
            _wasPlaying = true;
            return;
        }

        var now = Time.GetTicksMsec();
        if (_wasPlaying)
        {
            _wasPlaying = false;
            _nextClipTicks = now + (ulong)_gapMs;
            return;
        }
        if (Queue.Count == 0 || now < _nextClipTicks)
            return;

        var path = Queue.Dequeue();
        try
        {
            var stream = AudioStreamOggVorbis.LoadFromFile(path);
            if (stream == null)
                throw new InvalidDataException("Godot returned no OGG stream.");
            stream.Loop = false;
            _player.Stream = stream;
            _player.Play();
            MainFile.Logger.Info($"Encounter voice: {Path.GetFileName(path)}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Encounter voice clip failed ({path}): {e.Message}");
        }
    }

    public static void Stop()
    {
        Queue.Clear();
        _player?.Stop();
        _wasPlaying = false;
        _nextClipTicks = 0;
    }

    public static void Shutdown()
    {
        Stop();
        if (_player != null && GodotObject.IsInstanceValid(_player))
            _player.QueueFree();
        _player = null;
    }
}
