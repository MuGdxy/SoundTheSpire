using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using SoundTheSpire.Hot.Audio;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// Watches the local player's defense during their turn and plays the "pressure resolved" cadence the moment
/// incoming attack damage stops exceeding block, however that happened: more block, an attacker killed,
/// Weak applied, an intent changed. Clearing the whole fight is a victory, not a defensive resolution, so it stays silent.
/// </summary>
public static class DefenseMonitor
{
    private static ICombatState? _combat;
    private static bool _underPressure;

    public static void Poll()
    {
        if (_combat is not { } combat || !ReferenceEquals(combat, CombatReader.CurrentCombat) || combat.CurrentSide != CombatSide.Player)
        {
            _combat = null;
            return;
        }
        if (LocalContext.GetMe(combat)?.Creature is not { } me || !combat.Enemies.Any(e => e.IsAlive))
            return;

        var incoming = CombatReader.IncomingDamage(combat);
        var underPressure = incoming > me.Block;
        if (_underPressure && !underPressure && SynthEngine.Instance is { } engine)
        {
            MainFile.Logger.Info($"Defense resolved: block {me.Block}, incoming {incoming}");
            BlockMotif.PlayResolved(engine);
        }
        _underPressure = underPressure;
    }

    /// <summary>Takes the current pressure as the baseline; nothing plays until it changes.</summary>
    public static void StartWatching(ICombatState combat)
    {
        if (LocalContext.GetMe(combat)?.Creature is not { } me)
            return;
        _combat = combat;
        _underPressure = CombatReader.IncomingDamage(combat) > me.Block;
    }

    /// <summary>Intents are already rolled when this hook runs, so the turn's starting pressure is the baseline.</summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
    private static class TurnStartPatch
    {
        private static void Prefix(ICombatState combatState, PlayerChoiceContext choiceContext, Player player)
        {
            if (LocalContext.IsMe(player))
                StartWatching(combatState);
        }
    }
}
