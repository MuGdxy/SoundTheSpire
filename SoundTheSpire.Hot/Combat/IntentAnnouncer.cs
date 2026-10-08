using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Combat;
using SoundTheSpire.Hot.Audio;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// Turn summary when the player gets control (after start-of-turn relics, draw and auto-play have finished):
/// enemy line-up, then one bar for the defense status. In between, an enemy's intent plays when
/// it is selected (keyboard/controller focus, mouse hover, or card targeting). R replays the summary.
/// </summary>
public static class IntentAnnouncer
{
    public const Key ReplayKey = Key.R;

    private static bool _replayKeyWasDown;

    public static bool PlayAll()
    {
        if (CombatReader.CurrentCombat is not { } combat || SynthEngine.Instance is not { } engine)
            return false;
        PlayTurnSummary(engine, combat);
        return true;
    }

    /// <param name="slot">Enemy index as used by sts_state and sts_intent.</param>
    public static bool PlayEnemy(int slot)
    {
        if (CombatReader.CurrentCombat is not { } combat || SynthEngine.Instance is not { } engine)
            return false;
        var enemies = CombatReader.ReadEnemies(combat);
        if (enemies.FirstOrDefault(e => e.Slot == slot) is not { } enemy)
            return false;
        IntentMotif.Play(engine, enemies, enemy);
        return true;
    }

    public static void PollReplayKey()
    {
        var down = Input.IsKeyPressed(ReplayKey);
        if (down && !_replayKeyWasDown)
            PlayAll();
        _replayKeyWasDown = down;
    }

    /// <summary>Raised by the game once the player's turn setup is complete and play begins.</summary>
    public static void OnTurnStarted(CombatState combat)
    {
        if (SynthEngine.Instance is not { } engine || combat.CurrentSide != CombatSide.Player)
            return;
        MainFile.Logger.Info($"Turn {combat.RoundNumber} ready: playing turn summary");
        PlayTurnSummary(engine, combat);
        DefenseMonitor.StartWatching(combat);
    }

    private static void PlayTurnSummary(SynthEngine engine, ICombatState combat)
    {
        var end = IntentMotif.Play(engine, CombatReader.ReadEnemies(combat));
        if (LocalContext.GetMe(combat)?.Creature is not { } me)
            return;
        var incoming = CombatReader.IncomingDamage(combat);
        MainFile.Logger.Info($"Defense status: {PassiveDefense.Describe(combat, me)}, incoming {incoming}");
        BlockMotif.ScheduleStatus(engine, end, PassiveDefense.ProjectedBlock(combat, me), incoming);
    }

    [HarmonyPatch(typeof(NCreature), "OnFocus")]
    private static class CreatureSelectedPatch
    {
        private static void Prefix(NCreature __instance, out bool __state) => __state = __instance.IsFocused;

        private static void Postfix(NCreature __instance, bool __state)
        {
            if (__state || !__instance.IsFocused || SynthEngine.Instance is not { } engine)
                return;
            if (__instance.Entity.CombatState is not { } combat)
                return;
            if (__instance.Entity.IsPlayer)
            {
                if (!ReferenceEquals(LocalContext.GetMe(combat)?.Creature, __instance.Entity))
                    return;
                var incoming = CombatReader.IncomingDamage(combat);
                MainFile.Logger.Info("Player selected: playing current defense status");
                BlockMotif.PlaySnapshot(engine, PassiveDefense.ProjectedBlock(combat, __instance.Entity), incoming);
                return;
            }
            if (!__instance.Entity.IsEnemy)
                return;
            ListeningTutorial.OnEnemySelected(__instance.Entity);
            var enemies = CombatReader.ReadEnemies(combat);
            if (enemies.FirstOrDefault(e => e.Creature == __instance.Entity) is { } enemy)
            {
                MainFile.Logger.Info($"Enemy {enemy.Slot} selected: playing its intent");
                IntentMotif.Play(engine, enemies, enemy);
            }
        }
    }
}
