using Godot;

namespace SoundTheSpire.SoundTheSpireCode.Audio;

/// <summary>Persistent, mod-specific volume controlled by the slider added to the game's sound settings.</summary>
public static class InstrumentVolume
{
    private const string ConfigPath = "user://sound_the_spire.cfg";
    private const string Section = "audio";
    private const string Key = "instrument_volume";
    public const float MaxValue = 4f;
    private const float DefaultValue = 1f;

    public static float Value { get; private set; } = DefaultValue;

    public static void Load()
    {
        var config = new ConfigFile();
        if (config.Load(ConfigPath) == Error.Ok)
            Value = Math.Clamp((float)config.GetValue(Section, Key, DefaultValue).AsDouble(), 0f, MaxValue);
    }

    public static void Set(float value)
    {
        Value = Math.Clamp(value, 0f, MaxValue);
        var config = new ConfigFile();
        config.SetValue(Section, Key, Value);
        var error = config.Save(ConfigPath);
        if (error != Error.Ok)
            MainFile.Logger.Warn($"Could not save instrument volume: {error}");
    }
}
