using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
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
    private static ICombatState? _pendingTurnSummary;
    private static Creature? _focusedEnemy;
    private static ulong _nextFocusedReplayTicks;
    private static string _focusedIntentSignature = "";

    public static bool HasFocusedEnemy => _focusedEnemy != null;

    public static bool PlayAll()
    {
        if (CombatReader.CurrentCombat is not { } combat || SynthEngine.Instance is not { } engine)
            return false;
        if (VoicePlayback.IsBusy)
        {
            _pendingTurnSummary = combat;
            MainFile.Logger.Info("Encounter voice is playing: turn summary deferred.");
            return true;
        }
        PlayTurnSummary(engine, combat);
        return true;
    }

    /// <param name="slot">Enemy index as used by sts_state and sts_intent.</param>
    public static bool PlayEnemy(int slot)
    {
        if (VoicePlayback.IsBusy ||
            CombatReader.CurrentCombat is not { } combat ||
            SynthEngine.Instance is not { } engine)
            return false;
        var enemies = CombatReader.ReadEnemies(combat);
        if (enemies.FirstOrDefault(e => e.Slot == slot) is not { } enemy)
            return false;
        IntentMotif.Play(engine, enemies, enemy);
        return true;
    }

    public static void PollReplayKey()
    {
        PlayPendingTurnSummary();
        PollFocusedIntent();
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
        DefenseMonitor.StartWatching(combat);
        if (VoicePlayback.IsBusy)
        {
            _pendingTurnSummary = combat;
            MainFile.Logger.Info($"Turn {combat.RoundNumber} ready: waiting for encounter voice.");
            return;
        }
        MainFile.Logger.Info($"Turn {combat.RoundNumber} ready: playing turn summary");
        PlayTurnSummary(engine, combat);
    }

    private static void PlayPendingTurnSummary()
    {
        if (_pendingTurnSummary is not { } combat || VoicePlayback.IsBusy)
            return;
        _pendingTurnSummary = null;
        if (!ReferenceEquals(CombatReader.CurrentCombat, combat) ||
            combat.CurrentSide != CombatSide.Player ||
            SynthEngine.Instance is not { } engine)
            return;
        MainFile.Logger.Info($"Encounter voice finished: playing turn {combat.RoundNumber} summary.");
        PlayTurnSummary(engine, combat);
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

    private static void PlayFocusedIntent(SynthEngine engine, ICombatState combat, EnemyInfo enemy)
    {
        var duration = IntentMotif.Play(engine, CombatReader.ReadEnemies(combat), enemy);
        _focusedIntentSignature = IntentSignature(enemy);
        _nextFocusedReplayTicks = Time.GetTicksMsec() + (ulong)Math.Ceiling(duration * 1000);
    }

    private static void PollFocusedIntent()
    {
        if (_focusedEnemy is not { } creature ||
            Time.GetTicksMsec() < _nextFocusedReplayTicks ||
            VoicePlayback.IsBusy ||
            SynthEngine.Instance is not { } engine ||
            CombatReader.CurrentCombat is not { CurrentSide: CombatSide.Player } combat ||
            !ReferenceEquals(creature.CombatState, combat))
            return;
        var enemies = CombatReader.ReadEnemies(combat);
        if (enemies.FirstOrDefault(enemy => ReferenceEquals(enemy.Creature, creature)) is not { } focused)
        {
            _focusedEnemy = null;
            return;
        }
        var signature = IntentSignature(focused);
        if (signature != _focusedIntentSignature)
        {
            MainFile.Logger.Info(
                $"Enemy {focused.Slot} intent changed while focused: updating drums without restarting melody.");
            var changedDuration = IntentMotif.ContinueMelody(engine, enemies, focused);
            IntentMotif.PlayChangedRhythm(engine, enemies, focused);
            _focusedIntentSignature = signature;
            _nextFocusedReplayTicks = Time.GetTicksMsec() +
                (ulong)Math.Ceiling(Math.Max(changedDuration, MusicClock.BarSeconds) * 1000);
            return;
        }
        MainFile.Logger.Info($"Enemy {focused.Slot} remains focused: continuing its melody.");
        var duration = IntentMotif.ContinueMelody(engine, enemies, focused);
        _nextFocusedReplayTicks = Time.GetTicksMsec() +
            (ulong)Math.Ceiling(Math.Max(duration, MusicClock.BarSeconds) * 1000);
    }

    private static void ClearFocusedIntent(Creature? creature = null)
    {
        if (creature != null && !ReferenceEquals(_focusedEnemy, creature))
            return;
        _focusedEnemy = null;
        _nextFocusedReplayTicks = 0;
        _focusedIntentSignature = "";
    }

    private static string IntentSignature(EnemyInfo enemy) =>
        string.Join("|", enemy.Intents.Select(intent =>
            $"{intent.Kind}:{intent.TotalDamage}:{intent.Hits}"));

    [HarmonyPatch(typeof(NCreature), "OnFocus")]
    private static class CreatureSelectedPatch
    {
        private static void Prefix(NCreature __instance, out bool __state) => __state = __instance.IsFocused;

        private static void Postfix(NCreature __instance, bool __state)
        {
            if (__state ||
                !__instance.IsFocused ||
                VoicePlayback.IsBusy ||
                SynthEngine.Instance is not { } engine)
                return;
            if (__instance.Entity.CombatState is not { } combat)
                return;
            if (__instance.Entity.IsPlayer)
            {
                ClearFocusedIntent();
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
                _focusedEnemy = __instance.Entity;
                PlayFocusedIntent(engine, combat, enemy);
            }
        }
    }

    [HarmonyPatch(typeof(NCreature), "OnUnfocus")]
    private static class CreatureUnselectedPatch
    {
        private static void Postfix(NCreature __instance) => ClearFocusedIntent(__instance.Entity);
    }
}
