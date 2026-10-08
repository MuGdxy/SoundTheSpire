using System.Reflection;
using System.Runtime.Loader;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using SoundTheSpire.SoundTheSpireCode.Audio;
using SoundTheSpire.SoundTheSpireCode.Core;

namespace SoundTheSpire.SoundTheSpireCode;

/// <summary>
/// Resident half of the mod: main-thread pump, synthesizer, debug bridge and the hot-module host.
/// Gameplay sonification lives in SoundTheSpire.Hot and can be reloaded with sts_reload.
/// </summary>
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "SoundTheSpire"; //At the moment, this is used only for the Logger and harmony names.

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static string ModDirectory { get; private set; } = "";

    public static void Initialize()
    {
        var assembly = Assembly.GetExecutingAssembly();
        ModDirectory = Path.GetDirectoryName(assembly.Location)!;

        // The game's load context only resolves sts2 and 0Harmony; our other dependencies live next to our dll.
        AssemblyLoadContext.GetLoadContext(assembly)!.Resolving += (context, name) =>
        {
            var path = Path.Combine(ModDirectory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };

        Harmony harmony = new(ModId);

        harmony.PatchAll(assembly);

        var tree = (SceneTree)Engine.GetMainLoop();
        MainThread.Install(tree);
        StartAudio(tree);
        try
        {
            HotModuleHost.Load();
        }
        catch (Exception e)
        {
            Logger.Error($"Hot module failed to load: {e}");
        }
#if DEBUG
        DebugBridge.Start();
#endif

        Logger.Info($"{ModId} loaded");
    }

    // Kept out of Initialize so MeltySynth is only JIT-resolved after the assembly resolver is registered.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void StartAudio(SceneTree tree)
    {
        var soundFont = Path.Combine(ModDirectory, "soundfonts", "GeneralUser-GS.sf2");
        if (!File.Exists(soundFont))
        {
            Logger.Error($"SoundFont not found at {soundFont}; audio disabled.");
            return;
        }

        var started = DateTime.UtcNow;
        InstrumentVolume.Load();
        var engine = SynthEngine.Create(soundFont, tree.Root);
        MainThread.Frame += engine.SyncVolume;
        engine.SyncVolume();
        Logger.Info($"Synth engine ready at {engine.SampleRate} Hz in {(DateTime.UtcNow - started).TotalMilliseconds:F0} ms");
    }
}
