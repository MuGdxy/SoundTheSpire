using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using SoundTheSpire.Hot.Audio;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// Watches the local player's defense during their turn and plays the defense status phrase whenever the damage that
/// would get through (incoming minus the block they would hold at end of turn, see <see cref="PassiveDefense"/>)
/// changes, even within the same tier, so every change is heard. Any cause counts: more block, an attacker killed,
/// Weak applied, an intent changed, an attack card switching Ripple Basin off. Clearing the whole fight is a victory,
/// so it stays silent.
/// </summary>
public static class DefenseMonitor
{
    private static ICombatState? _combat;
    private static int _hurt;

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
        var block = PassiveDefense.ProjectedBlock(combat, me);
        var hurt = Hurt(block, incoming);
        if (hurt != _hurt && SynthEngine.Instance is { } engine)
        {
            MainFile.Logger.Info($"Defense hurt {_hurt} -> {hurt} (tier {BlockMotif.HurtTier(block, incoming)}): {PassiveDefense.Describe(combat, me)}, incoming {incoming}");
            BlockMotif.PlayStatus(engine, block, incoming);
        }
        _hurt = hurt;
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
        _hurt = Hurt(PassiveDefense.ProjectedBlock(combat, me), CombatReader.IncomingDamage(combat));
    }

    private static int Hurt(int block, int incoming) => Math.Max(0, incoming - block);
}
