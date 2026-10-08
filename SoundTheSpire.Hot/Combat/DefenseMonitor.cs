using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using SoundTheSpire.Hot.Audio;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// Watches the local player's defense during their turn and plays the defense status phrase whenever the damage that
/// would get through (incoming minus the block they would hold at end of turn, see <see cref="PassiveDefense"/>)
/// changes, even within the same tier, so every change is heard. Any cause counts: more block, an attacker killed,
/// Weak applied, an intent changed, an attack card switching Ripple Basin off. Clearing the whole fight is a victory,
/// so it stays silent.
/// <para>
/// A card or potion is judged as a whole, from leaving the hand to the end of its effect: if it lowered the damage
/// that gets through, the status before and the status after play as a transition; if it raised it, the new status
/// plays alone.
/// </para>
/// </summary>
public static class DefenseMonitor
{
    private readonly record struct Defense(int Block, int Incoming)
    {
        public int Hurt => Math.Max(0, Incoming - Block);
    }

    private static ICombatState? _combat;
    private static Defense _last;
    private static Defense? _beforePlay;

    public static void Poll()
    {
        if (_combat is not { } combat || !ReferenceEquals(combat, CombatReader.CurrentCombat) || combat.CurrentSide != CombatSide.Player)
        {
            _combat = null;
            return;
        }
        if (LocalContext.GetMe(combat)?.Creature is not { Player: { } player } me || !combat.Enemies.Any(e => e.IsAlive))
            return;

        var now = new Defense(PassiveDefense.ProjectedBlock(combat, me), CombatReader.IncomingDamage(combat));
        if (IsPlaying(player))
        {
            _beforePlay ??= _last;
            _last = now;
            return;
        }

        var engine = SynthEngine.Instance;
        if (_beforePlay is { } before)
        {
            _beforePlay = null;
            _last = before;
            if (now.Hurt < before.Hurt && engine != null)
            {
                MainFile.Logger.Info($"Defense improved by play: hurt {before.Hurt} -> {now.Hurt}: {PassiveDefense.Describe(combat, me)}, incoming {now.Incoming}");
                BlockMotif.PlayTransition(engine, before.Block, before.Incoming, now.Block, now.Incoming);
                _last = now;
                return;
            }
        }

        if (now.Hurt != _last.Hurt && engine != null)
        {
            MainFile.Logger.Info($"Defense hurt {_last.Hurt} -> {now.Hurt} (tier {BlockMotif.HurtTier(now.Block, now.Incoming)}): {PassiveDefense.Describe(combat, me)}, incoming {now.Incoming}");
            BlockMotif.PlayStatus(engine, now.Block, now.Incoming);
        }
        _last = now;
    }

    /// <summary>
    /// Takes the current state as the baseline; nothing plays until it changes. Called when play begins, after
    /// start-of-turn relics, so their block is part of the turn summary rather than a separate change.
    /// </summary>
    public static void StartWatching(ICombatState combat)
    {
        if (LocalContext.GetMe(combat)?.Creature is not { } me)
            return;
        _combat = combat;
        _beforePlay = null;
        _last = new Defense(PassiveDefense.ProjectedBlock(combat, me), CombatReader.IncomingDamage(combat));
    }

    /// <summary>A card sits in the play pile from leaving the hand until it resolves; potions only run an effect.</summary>
    private static bool IsPlaying(Player player) =>
        CombatManager.Instance.IsExecutingCardOrPotionEffect(player) || PileType.Play.GetPile(player).Cards.Count > 0;
}
