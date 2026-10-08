using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes.Combat;
using SoundTheSpire.Hot.Audio;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// The whole enemy line-up plays once when combat opens; after that an enemy's intent plays when it is selected
/// (keyboard/controller focus, mouse hover, or card targeting). R replays the whole line-up.
/// </summary>
public static class IntentAnnouncer
{
    public const Key ReplayKey = Key.R;

    private static bool _replayKeyWasDown;

    public static bool PlayAll()
    {
        if (CombatReader.CurrentCombat is not { } combat || SynthEngine.Instance is not { } engine)
            return false;
        IntentMotif.Play(engine, CombatReader.ReadEnemies(combat));
        return true;
    }

    public static bool PlayEnemy(int screenIndex)
    {
        if (CombatReader.CurrentCombat is not { } combat || SynthEngine.Instance is not { } engine)
            return false;
        var enemies = CombatReader.ReadEnemies(combat);
        if (screenIndex < 0 || screenIndex >= enemies.Count)
            return false;
        IntentMotif.Play(engine, enemies[screenIndex]);
        return true;
    }

    public static void PollReplayKey()
    {
        var down = Input.IsKeyPressed(ReplayKey);
        if (down && !_replayKeyWasDown)
            PlayAll();
        _replayKeyWasDown = down;
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
    private static class CombatOpeningPatch
    {
        private static void Prefix(ICombatState combatState, PlayerChoiceContext choiceContext, Player player)
        {
            if (combatState.RoundNumber == 1 && LocalContext.IsMe(player) && SynthEngine.Instance is { } engine)
                IntentMotif.Play(engine, CombatReader.ReadEnemies(combatState));
        }
    }

    [HarmonyPatch(typeof(NCreature), "OnFocus")]
    private static class EnemySelectedPatch
    {
        private static void Prefix(NCreature __instance, out bool __state) => __state = __instance.IsFocused;

        private static void Postfix(NCreature __instance, bool __state)
        {
            if (__state || !__instance.IsFocused || !__instance.Entity.IsEnemy || SynthEngine.Instance is not { } engine)
                return;
            if (CombatReader.ReadEnemy(__instance.Entity) is { } enemy)
                IntentMotif.Play(engine, enemy);
        }
    }
}
