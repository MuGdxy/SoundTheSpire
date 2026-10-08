using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Nodes.Debug;
using GameDevConsole = MegaCrit.Sts2.Core.DevConsole.DevConsole;

namespace SoundTheSpire.SoundTheSpireCode.Core;

/// <summary>
/// The game only discovers console commands in mod assemblies when a console is constructed, so commands from the
/// hot module are inserted into (and removed from) every live console directly.
/// </summary>
public static class ConsoleRegistry
{
    private static readonly FieldInfo CommandsField = AccessTools.Field(typeof(GameDevConsole), "_commands");
    private static readonly FieldInfo GameConsoleNodeField = AccessTools.Field(typeof(NDevConsole), "_instance");
    private static readonly FieldInfo GameConsoleField = AccessTools.Field(typeof(NDevConsole), "_devConsole");

    private static readonly List<WeakReference<GameDevConsole>> Consoles = new();
    private static IReadOnlyList<AbstractConsoleCmd> _hotCommands = Array.Empty<AbstractConsoleCmd>();

    internal static void SetHotCommands(IReadOnlyList<AbstractConsoleCmd> commands)
    {
        if (GameConsoleNodeField.GetValue(null) is NDevConsole node && GameConsoleField.GetValue(node) is GameDevConsole game)
            Track(game);

        foreach (var reference in Consoles)
        {
            if (!reference.TryGetTarget(out var console))
                continue;
            var registered = Commands(console);
            foreach (var old in _hotCommands)
            {
                if (registered.TryGetValue(old.CmdName, out var current) && ReferenceEquals(current, old))
                    registered.Remove(old.CmdName);
            }
            foreach (var command in commands)
                registered[command.CmdName] = command;
        }
        _hotCommands = commands;
    }

    private static Dictionary<string, AbstractConsoleCmd> Commands(GameDevConsole console) =>
        (Dictionary<string, AbstractConsoleCmd>)CommandsField.GetValue(console)!;

    private static void Track(GameDevConsole console)
    {
        Consoles.RemoveAll(r => !r.TryGetTarget(out _));
        if (Consoles.Any(r => r.TryGetTarget(out var c) && ReferenceEquals(c, console)))
            return;
        Consoles.Add(new WeakReference<GameDevConsole>(console));
    }

    [HarmonyPatch(typeof(GameDevConsole), MethodType.Constructor, typeof(bool))]
    private static class DevConsoleConstructorPatch
    {
        private static void Postfix(GameDevConsole __instance)
        {
            Track(__instance);
            var registered = Commands(__instance);
            foreach (var command in _hotCommands)
                registered[command.CmdName] = command;
        }
    }
}
