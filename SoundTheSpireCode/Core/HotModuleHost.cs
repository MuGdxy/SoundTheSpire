using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using SoundTheSpire.SoundTheSpireCode.Audio;

namespace SoundTheSpire.SoundTheSpireCode.Core;

/// <summary>
/// Loads SoundTheSpire.Hot.dll and swaps it for a fresh build on demand: Harmony patches, console commands and
/// frame handlers of the previous generation are removed first. The synthesizer and the game state survive.
/// </summary>
public static class HotModuleHost
{
    public const string AssemblyName = "SoundTheSpire.Hot";

    private static readonly Harmony Harmony = new(MainFile.ModId + ".Hot");

    private static IHotModule? _module;
    private static HotContext? _context;

    public static int Generation { get; private set; }

    /// <summary>Loads (or reloads) the hot module. Must run on the main thread. Throws if the new build is unusable.</summary>
    public static string Load()
    {
        var path = Path.Combine(MainFile.ModDirectory, AssemblyName + ".dll");
        if (!File.Exists(path))
            throw new FileNotFoundException($"{AssemblyName}.dll not found in the mod folder.", path);

        // Inspect the new build before tearing down the old one, so a broken build leaves the current one running.
        // Loading from bytes keeps the file unlocked for the next build.
        var pdbPath = Path.ChangeExtension(path, ".pdb");
        using var dll = new MemoryStream(File.ReadAllBytes(path));
        using var pdb = File.Exists(pdbPath) ? new MemoryStream(File.ReadAllBytes(pdbPath)) : null;
        var assembly = new HotLoadContext(Generation + 1).LoadFromStream(dll, pdb);

        var types = assembly.GetTypes();
        var entry = types.Single(t => typeof(IHotModule).IsAssignableFrom(t) && !t.IsAbstract);
        var module = (IHotModule)Activator.CreateInstance(entry)!;
        var commands = types
            .Where(t => t.IsSubclassOf(typeof(AbstractConsoleCmd)) && !t.IsAbstract)
            .Select(t => (AbstractConsoleCmd)Activator.CreateInstance(t)!)
            .ToList();

        Unload();
        Generation++;
        Harmony.PatchAll(assembly);
        ConsoleRegistry.SetHotCommands(commands);
        _context = new HotContext();
        module.Load(_context);
        _module = module;

        var patches = Harmony.GetPatchedMethods().Count();
        var message = $"Loaded {AssemblyName} generation {Generation}: {patches} patched methods, {commands.Count} commands.";
        MainFile.Logger.Info(message);
        return message;
    }

    private static void Unload()
    {
        if (_module != null)
        {
            try
            {
                _module.Unload();
            }
            catch (Exception e)
            {
                MainFile.Logger.Error($"Hot module unload failed: {e}");
            }
        }
        _context?.Release();
        _context = null;
        _module = null;
        Harmony.UnpatchAll(Harmony.Id);
        ConsoleRegistry.SetHotCommands(Array.Empty<AbstractConsoleCmd>());
        SynthEngine.Instance?.Reset();
    }

    /// <summary>
    /// Never unloaded: Harmony keeps references into patch assemblies, and a few hundred KB per reload is fine for
    /// development. Shared dependencies resolve to the copies already loaded so types stay identical.
    /// </summary>
    private sealed class HotLoadContext(int generation) : AssemblyLoadContext($"{AssemblyName}#{generation}")
    {
        protected override Assembly? Load(AssemblyName name) =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                a.GetName().Name == name.Name && GetLoadContext(a) is not HotLoadContext);
    }
}
