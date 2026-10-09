using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoundTheSpire.Hot.Audio;

/// <summary>Loads every external music profile and resolves it by FMOD event path.</summary>
public static class MusicProfileRegistry
{
    private static readonly Lazy<IReadOnlyDictionary<string, MusicProfileData>> Loaded = new(LoadAll);

    public static IReadOnlyCollection<MusicProfileData> All => Loaded.Value.Values.ToArray();

    public static MusicProfileData? Find(string eventPath)
    {
        if (Loaded.Value.TryGetValue(eventPath, out var profile))
            return profile;
        return Loaded.Value.TryGetValue("*", out var fallback) ? fallback : null;
    }

    private static IReadOnlyDictionary<string, MusicProfileData> LoadAll()
    {
        var directory = Path.Combine(MainFile.ModDirectory, "profiles");
        var profiles = new Dictionary<string, MusicProfileData>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            MainFile.Logger.Warn($"Music profile directory not found: {directory}");
            return profiles;
        }

        foreach (var path in Directory.GetFiles(directory, "*.json").Order())
        {
            try
            {
                var profile = JsonSerializer.Deserialize<MusicProfileData>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Validate(profile);
                if (!profiles.TryAdd(profile!.EventPath, profile))
                    throw new InvalidDataException($"Duplicate eventPath '{profile.EventPath}'.");
                MainFile.Logger.Info($"Loaded music profile '{profile.Id}' from {path}");
            }
            catch (Exception e)
            {
                MainFile.Logger.Error($"Music profile ignored ({path}): {e.Message}");
            }
        }
        MainFile.Logger.Info($"Loaded {profiles.Count} music profiles.");
        return profiles;
    }

    private static void Validate(MusicProfileData? profile)
    {
        if (profile == null ||
            string.IsNullOrWhiteSpace(profile.Id) ||
            string.IsNullOrWhiteSpace(profile.EventPath) ||
            profile.Bpm <= 0 ||
            profile.BeatsPerBar <= 0 ||
            profile.BeatUnit <= 0 ||
            profile.AttackThresholds.Length != 4 ||
            profile.AttackRoots.Length != 5 ||
            profile.AttackVelocities.Length != 5 ||
            profile.HurtChords.Length != 4 ||
            profile.HurtVelocities.Length != 5 ||
            profile.MultiHitPercussionTiers.Length != 5 ||
            profile.Voices.Length == 0)
            throw new InvalidDataException("Required musical fields are missing or have the wrong length.");
        if (profile.Harmony is { } harmony &&
            (string.IsNullOrWhiteSpace(harmony.Marker) ||
             harmony.LoopBars <= 0 ||
             harmony.Changes.Length == 0 ||
             harmony.Changes.Any(change =>
                 change.Bar < 0 ||
                 change.Bar >= harmony.LoopBars ||
                 change.Root is < 0 or > 11 ||
                 change.Intervals.Length == 0)))
            throw new InvalidDataException("Harmony marker, loop or chord changes are invalid.");
        if (profile.Harmony is { MelodyNotes.Length: > 0 } melody &&
            (melody.MelodySubdivisionsPerBeat <= 0 ||
             melody.MelodyNotes.Length !=
             melody.LoopBars * profile.BeatsPerBar * melody.MelodySubdivisionsPerBeat))
            throw new InvalidDataException("Melody note count does not match loop bars and subdivisions.");
        if (profile.Harmony is { BarLoudnessLufs.Length: > 0 } loudness &&
            loudness.BarLoudnessLufs.Length != loudness.LoopBars)
            throw new InvalidDataException("Loudness value count does not match loop bars.");
    }
}

public sealed class MusicProfileData
{
    public string Id { get; set; } = "";
    public string EventPath { get; set; } = "";
    public string SourceTrackName { get; set; } = "";
    public string Key { get; set; } = "";
    public IntentPitchMode IntentPitchMode { get; set; } = IntentPitchMode.Tonic;
    public double OutputGainDb { get; set; }
    public bool DynamicLoudness { get; set; }
    public int TonicPitchClass { get; set; }
    public double Bpm { get; set; }
    public int BeatsPerBar { get; set; }
    public int BeatUnit { get; set; }
    public double LeadBeats { get; set; }
    public double ResolveBeats { get; set; }
    public double HoldBeats { get; set; }
    public double GateRatio { get; set; }
    public double RapidInputQuarterNotes { get; set; }
    public int RapidSubdivisionsPerBeat { get; set; }
    public int SelectionSubdivisionsPerBeat { get; set; } = 1;
    public int[] AttackThresholds { get; set; } = [];
    public int[] AttackRoots { get; set; } = [];
    public int[] AttackVelocities { get; set; } = [];
    public int[] AttackBackingVelocities { get; set; } = [];
    public int BackingProgram { get; set; }
    public double BackingGainDb { get; set; }
    public int BackingReverb { get; set; }
    public int BackingChorus { get; set; }
    public int PalmMuteVelocityDrop { get; set; }
    public double StrumSpreadSeconds { get; set; }
    public double MelodyOverlapSeconds { get; set; } = 0.08;
    public MusicPercussionTierData[] MultiHitPercussionTiers { get; set; } = [];
    public int[] MinorThirdTiers { get; set; } = [];
    public int[] DefendNotes { get; set; } = [];
    public int[] BuffNotes { get; set; } = [];
    public int[] DebuffNotes { get; set; } = [];
    public int UnknownNote { get; set; }
    public int[] Suspended { get; set; } = [];
    public int[] Resolved { get; set; } = [];
    public int[][] HurtChords { get; set; } = [];
    public int[] Clash { get; set; } = [];
    public int[] ClashAfter { get; set; } = [];
    public int[] HurtVelocities { get; set; } = [];
    public int NoIncomingVelocity { get; set; }
    public int SuspendedVelocity { get; set; }
    public int ResolvedVelocity { get; set; }
    public int DefenseVolume { get; set; } = 100;
    public int FocusedDefenseVolume { get; set; } = 45;
    public MusicVoiceData[] Voices { get; set; } = [];
    public MusicHarmonyData? Harmony { get; set; }
    public MusicStatusChangeData? StatusChange { get; set; }
    public MusicSpatialData? Spatial { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<IntentPitchMode>))]
public enum IntentPitchMode
{
    Tonic,
    ChordRoot,
    Melody,
}

public sealed class MusicVoiceData
{
    public int Program { get; set; }
    public string Name { get; set; } = "";
    public int MinimumVelocity { get; set; }
    public double GainDb { get; set; }
    public int Reverb { get; set; }
    public int Chorus { get; set; }
    public int PanMotionDepth { get; set; }
    public double PanMotionRateBeats { get; set; } = 1;
    public int OctaveOffset { get; set; }
    public int MinNote { get; set; }
    public int MaxNote { get; set; } = 127;
    public double GateRatio { get; set; } = 1;
    public int Modulation { get; set; }
}

public sealed class MusicPercussionTierData
{
    public string Name { get; set; } = "";
    public int[] OpenNotes { get; set; } = [];
    public int[] BackbeatNotes { get; set; } = [];
    public int[] MutedNotes { get; set; } = [];
    public int OpenVelocity { get; set; }
    public int BackbeatVelocity { get; set; }
    public int MutedVelocity { get; set; }
    public double GateRatio { get; set; }
}

public sealed class MusicHarmonyData
{
    public string Marker { get; set; } = "";
    public double PickupBeats { get; set; }
    public int LoopBars { get; set; }
    public int MelodySubdivisionsPerBeat { get; set; } = 1;
    public int[] MelodyNotes { get; set; } = [];
    public double ReferenceLoudnessLufs { get; set; }
    public double MaxLoudnessAttenuationDb { get; set; }
    public double[] BarLoudnessLufs { get; set; } = [];
    public MusicChordChangeData[] Changes { get; set; } = [];
}

public sealed class MusicChordChangeData
{
    public int Bar { get; set; }
    public string Name { get; set; } = "";
    public int Root { get; set; }
    public int[] Intervals { get; set; } = [];
}

public sealed class MusicStatusChangeData
{
    public int BuffProgram { get; set; }
    public int DebuffProgram { get; set; }
    public int ApplyVelocity { get; set; }
    public int RemoveVelocity { get; set; }
    public double StepBeats { get; set; }
    public double NoteDurationBeats { get; set; }
    public int SubdivisionsPerBeat { get; set; }
}

public sealed class MusicSpatialData
{
    public int PanMin { get; set; } = 8;
    public int PanMax { get; set; } = 119;
    public double BackGainDb { get; set; }
    public double FrontGainDb { get; set; }
    public int BackReverbAdd { get; set; }
    public int FrontReverbAdd { get; set; }
    public int BackBrightness { get; set; } = 64;
    public int FrontBrightness { get; set; } = 64;
}
