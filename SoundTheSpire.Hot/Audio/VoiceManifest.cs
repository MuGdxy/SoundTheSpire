using System.Text.Json;
using MegaCrit.Sts2.Core.Localization;

namespace SoundTheSpire.Hot.Audio;

public sealed class VoiceLineData
{
    public string File { get; set; } = "";
    public string Text { get; set; } = "";
    public string MatrixText { get; set; } = "";
}

public sealed class VoiceCommonData
{
    public VoiceLineData? Encounter { get; set; }
    public VoiceLineData? Reinforcements { get; set; }
}

public sealed class VoiceStatusData
{
    public string MatrixDirectory { get; set; } = "status/matrix";
    public string[] BuffPowers { get; set; } = [];

    /// <summary>Reusable words such as "you", "gained", "suffered", "your" and "ended".</summary>
    public Dictionary<string, VoiceLineData> Parts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ordered symbolic clips for buffStarted, debuffStarted and statusEnded.
    /// "target" and "power" are dynamic; every other symbol resolves through <see cref="Parts"/>.
    /// </summary>
    public Dictionary<string, string[]> Templates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, VoiceLineData> Powers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class VoiceManifestData
{
    public int Version { get; set; } = 1;
    public string Locale { get; set; } = "";
    public int GapMs { get; set; } = 180;
    public float VolumeDb { get; set; }
    public string CountPlacement { get; set; } = "after";
    public VoiceCommonData Common { get; set; } = new();
    public Dictionary<string, VoiceLineData> Encounters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VoiceLineData> Rosters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VoiceLineData> Monsters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VoiceLineData> Cards { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VoiceLineData> Counts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public VoiceStatusData Status { get; set; } = new();
}

/// <summary>
/// Loads one locale's pre-generated encounter voice assets. Game locale codes are used directly
/// (eng, zhs, zht, jpn, and so on); English is the asset fallback.
/// </summary>
public sealed class VoiceManifest
{
    private const int SupportedVersion = 1;

    public static VoiceManifest Empty { get; } =
        new("", "", new VoiceManifestData());

    public string Locale { get; }
    public string DirectoryPath { get; }
    public VoiceManifestData Data { get; }
    public bool IsLoaded => DirectoryPath.Length > 0;

    private VoiceManifest(string locale, string directoryPath, VoiceManifestData data)
    {
        Locale = locale;
        DirectoryPath = directoryPath;
        Data = data;
    }

    public static VoiceManifest LoadCurrent()
    {
        var current = LocManager.Instance?.Language ?? "eng";
        return TryLoad(current) ?? (current.Equals("eng", StringComparison.OrdinalIgnoreCase) ? null : TryLoad("eng")) ?? Empty;
    }

    private static VoiceManifest? TryLoad(string locale)
    {
        var directory = Path.Combine(MainFile.ModDirectory, "voices", locale);
        var path = Path.Combine(directory, "manifest.json");
        if (!File.Exists(path))
            return null;

        try
        {
            var data = JsonSerializer.Deserialize<VoiceManifestData>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data == null)
                throw new InvalidDataException("Manifest is empty.");
            if (data.Version != SupportedVersion)
                throw new InvalidDataException($"Expected version {SupportedVersion}, got {data.Version}.");
            if (data.GapMs is < 0 or > 5000)
                throw new InvalidDataException($"gapMs must be between 0 and 5000, got {data.GapMs}.");
            if (data.CountPlacement is not ("before" or "after"))
                throw new InvalidDataException($"countPlacement must be 'before' or 'after', got '{data.CountPlacement}'.");

            MainFile.Logger.Info($"Loaded encounter voices for '{locale}' from {path}");
            return new VoiceManifest(locale, Path.GetFullPath(directory), data);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Encounter voice manifest ignored ({path}): {e.Message}");
            return null;
        }
    }

    public string? Resolve(VoiceLineData? line)
    {
        if (line == null || string.IsNullOrWhiteSpace(line.File) || !IsLoaded)
            return null;

        var path = Path.GetFullPath(Path.Combine(DirectoryPath, line.File.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = DirectoryPath.EndsWith(Path.DirectorySeparatorChar)
            ? DirectoryPath
            : DirectoryPath + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            MainFile.Logger.Warn($"Encounter voice path escapes locale directory: {line.File}");
            return null;
        }
        return File.Exists(path) ? path : null;
    }

    public string? ResolveRelative(string relativePath) =>
        Resolve(new VoiceLineData { File = relativePath });
}
