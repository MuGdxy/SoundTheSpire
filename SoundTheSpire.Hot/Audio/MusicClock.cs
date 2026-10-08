using Godot;
using Godot.Collections;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Audio;

namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// Active external music profile and FMOD beat clock. Normal act suites and custom boss events use the same path.
/// </summary>
public static class MusicClock
{
    private const long TimelineMarkerCallbackMask = 0x00000800;
    private const long TimelineBeatCallbackMask = 0x00001000;
    private const long TimelineCallbackMask = TimelineMarkerCallbackMask | TimelineBeatCallbackMask;

    private static GodotObject? _event;
    private static ulong _lastBeatTicks;
    private static int _beat;
    private static int _bar;
    private static int _timelinePositionMs;
    private static readonly HashSet<string> SeenMarkerCallbacks = [];
    private static int _beatsPerBar = 4;
    private static int _beatUnit = 4;
    private static double _tempo = 120;
    private static bool _loggedMeter;

    public static string? CurrentTrack { get; private set; }
    public static MusicProfileData? ActiveProfile { get; private set; }
    public static bool IsProfileActive => ActiveProfile != null;
    public static bool IsClockActive => CurrentTrack != null;
    public static bool HasFmodBeat => _lastBeatTicks != 0;
    public static double Tempo => _tempo;
    public static int BeatsPerBar => _beatsPerBar;
    public static int BeatUnit => _beatUnit;
    public static int CurrentBar => _bar;
    public static int CurrentBeat => _beat;
    public static int TimelinePositionMs => _timelinePositionMs;
    public static double EstimatedTimelinePositionMs => _timelinePositionMs +
        (_lastBeatTicks == 0 ? 0.0 : Time.GetTicksMsec() - _lastBeatTicks);
    public static string? LastMarker { get; private set; }
    public static double BeatSeconds => IsClockActive
        ? 60.0 / _tempo * 4.0 / _beatUnit
        : 60.0 / 120.0;
    public static double QuarterSeconds => IsClockActive ? 60.0 / _tempo : BeatSeconds;
    public static double BarSeconds => BeatSeconds * (IsClockActive ? _beatsPerBar : 4);

    public static double DelayToNextBar() => DelayToNextBarBoundary();
    public static double DelayToNextBeat() => DelayToNextSubdivision(1);

    public static void RefreshCurrentTrack()
    {
        if (NRunMusicController.Instance is not { } controller)
        {
            Reset();
            return;
        }
        var custom = CombatManager.Instance.DebugOnlyGetState()?.Encounter?.CustomBgm;
        var track = !string.IsNullOrEmpty(custom)
            ? custom
            : AccessTools.Field(typeof(NRunMusicController), "_currentTrack").GetValue(controller) as string;
        Activate(controller, track);
    }

    /// <summary>Delay to the next equal subdivision of the current notated beat (1 = eighth, 2 = sixteenth in 5/8).</summary>
    public static double DelayToNextSubdivision(int divisionsPerBeat)
    {
        if (!IsClockActive || _lastBeatTicks == 0 || _tempo <= 0)
            return 0;
        var step = BeatSeconds / Math.Max(1, divisionsPerBeat);
        var elapsed = (Time.GetTicksMsec() - _lastBeatTicks) / 1000.0;
        var delay = step - elapsed % step;
        if (delay < 0.02)
            delay += step;
        return delay;
    }

    public static void Reset()
    {
        HarmonyTimeline.Reset();
        SynthEngine.Instance?.SetProfileGain(1f);
        ActiveProfile = null;
        CurrentTrack = null;
        _event = null;
        _lastBeatTicks = 0;
        _beat = 0;
        _bar = 0;
        _timelinePositionMs = 0;
        SeenMarkerCallbacks.Clear();
        LastMarker = null;
        _beatsPerBar = 4;
        _beatUnit = 4;
        _tempo = 120;
        _loggedMeter = false;
    }

    private static void Activate(NRunMusicController controller, string? track)
    {
        Reset();
        CurrentTrack = track;
        if (track == null)
            return;
        var profile = MusicProfileRegistry.Find(track);
        ActiveProfile = profile;
        if (profile != null)
        {
            _tempo = profile.Bpm;
            _beatsPerBar = profile.BeatsPerBar;
            _beatUnit = profile.BeatUnit;
            SynthEngine.Instance?.SetProfileGain((float)Math.Pow(10.0, profile.OutputGainDb / 20.0));
        }
        Attach(controller);
    }

    private static void Attach(NRunMusicController controller)
    {
        if (!IsClockActive)
            return;
        if (AccessTools.Field(typeof(NRunMusicController), "_proxy").GetValue(controller) is not Node proxy)
            return;
        var value = proxy.Get("_musicEv");
        if (value.VariantType != Variant.Type.Object || value.AsGodotObject() is not { } musicEvent)
            return;
        _event = musicEvent;
        _event.Call("set_callback",
            Callable.From<Dictionary, long>(OnTimelineCallback),
            TimelineCallbackMask);
    }

    private static void OnTimelineCallback(Dictionary data, long type)
    {
        var label = ActiveProfile?.Id ?? CurrentTrack ?? "unprofiled";
        if (type == TimelineMarkerCallbackMask)
        {
            var marker = data["name"].AsString();
            var position = data["position"].AsInt32();
            if (!SeenMarkerCallbacks.Add($"{marker}\0{position}"))
                return;
            LastMarker = marker;
            HarmonyTimeline.OnMarker(LastMarker, position);
            MainFile.Logger.Info(
                $"Music marker '{label}': {LastMarker} at {position}ms, near bar {_bar} beat {_beat}.");
            return;
        }
        if (type != TimelineBeatCallbackMask)
            return;
        _beat = data["beat"].AsInt32();
        _bar = data["bar"].AsInt32();
        _timelinePositionMs = data["position"].AsInt32();
        _beatsPerBar = data["time_signature_upper"].AsInt32();
        _beatUnit = data["time_signature_lower"].AsInt32();
        _tempo = data["tempo"].AsDouble();
        _lastBeatTicks = Time.GetTicksMsec();
        if (_loggedMeter)
            return;
        _loggedMeter = true;
        MainFile.Logger.Info(
            $"Music clock '{label}': {ActiveProfile?.Key ?? "unprofiled"}, " +
            $"{_tempo:F2} BPM, {_beatsPerBar}/{_beatUnit}; FMOD beat callbacks active.");
    }

    private static double DelayToNextBarBoundary()
    {
        if (!IsClockActive || _lastBeatTicks == 0 || _tempo <= 0)
            return 0;
        var beatSeconds = BeatSeconds;
        var elapsed = (Time.GetTicksMsec() - _lastBeatTicks) / 1000.0;
        var beats = _beatsPerBar - _beat + 1;
        var delay = beats * beatSeconds - elapsed;
        var cycle = _beatsPerBar * beatSeconds;
        while (delay < 0.02)
            delay += cycle;
        return delay;
    }

    [HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.PlayCustomMusic))]
    private static class PlayCustomMusicPatch
    {
        private static void Postfix(NRunMusicController __instance, string customMusic) =>
            Activate(__instance, customMusic);
    }

    [HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateMusic))]
    private static class UpdateMusicPatch
    {
        private static void Postfix(NRunMusicController __instance)
        {
            var track = AccessTools.Field(typeof(NRunMusicController), "_currentTrack").GetValue(__instance) as string;
            if (track != null && (ActiveProfile?.EventPath != track || _event == null))
                Activate(__instance, track);
        }
    }

    [HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopCustomMusic))]
    private static class StopCustomMusicPatch
    {
        private static void Postfix(NRunMusicController __instance)
        {
            var track = AccessTools.Field(typeof(NRunMusicController), "_currentTrack").GetValue(__instance) as string;
            Activate(__instance, track);
        }
    }

    [HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopMusic))]
    private static class StopMusicPatch
    {
        private static void Postfix() => Reset();
    }
}
