using System.Reflection;
using System.Runtime.Loader;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using SoundTheSpire.SoundTheSpireCode.Audio;
using SoundTheSpire.SoundTheSpireCode.Combat;
using SoundTheSpire.SoundTheSpireCode.Core;

namespace SoundTheSpire.SoundTheSpireCode;

//You're recommended but not required to keep all your code in this package and all your assets in the SoundTheSpire folder.
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

        //If you want to use scripts defined in your mod for Godot scenes, uncomment the following line.
        //Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(assembly);
     
        Harmony harmony = new(ModId);

        harmony.PatchAll(assembly);

        var tree = (SceneTree)Engine.GetMainLoop();
        MainThread.Install(tree);
        StartAudio(tree);
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
        var engine = SynthEngine.Create(soundFont, tree.Root);
        MainThread.Frame += PollTestHotkey;
        MainThread.Frame += IntentAnnouncer.PollReplayKey;
        Logger.Info($"Synth engine ready at {engine.SampleRate} Hz in {(DateTime.UtcNow - started).TotalMilliseconds:F0} ms");
    }

    private static bool _testKeyWasDown;

    private static void PollTestHotkey()
    {
        var down = Input.IsKeyPressed(Key.F8);
        if (down && !_testKeyWasDown && SynthEngine.Instance is { } engine)
            SoundTest.Play(engine);
        _testKeyWasDown = down;
    }
}
