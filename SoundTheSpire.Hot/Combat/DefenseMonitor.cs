using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using SoundTheSpire.Hot.Audio;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// Watches the local player's defense during their turn and plays the defense status phrase whenever the verdict
/// (resolved / partial / undefended, judged against the block they would hold at end of turn, see
/// <see cref="PassiveDefense"/>) changes, whatever caused it: more block, an attacker killed, Weak applied, an intent
/// changed, an attack card switching Ripple Basin off. Clearing the whole fight is a victory, so it stays silent.
/// </summary>
public static class DefenseMonitor
{
    private static ICombatState? _combat;
    private static BlockMotif.Verdict _verdict;

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
        var verdict = BlockMotif.Judge(block, incoming);
        if (verdict != _verdict && SynthEngine.Instance is { } engine)
        {
            MainFile.Logger.Info($"Defense {_verdict} -> {verdict}: {PassiveDefense.Describe(combat, me)}, incoming {incoming}");
            BlockMotif.PlayStatus(engine, block, incoming);
        }
        _verdict = verdict;
    }

    /// <summary>
    /// Takes the current verdict as the baseline; nothing plays until it changes. Called when play begins, after
    /// start-of-turn relics, so their block is part of the turn summary rather than a separate change.
    /// </summary>
    public static void StartWatching(ICombatState combat)
    {
        if (LocalContext.GetMe(combat)?.Creature is not { } me)
            return;
        _combat = combat;
        _verdict = BlockMotif.Judge(PassiveDefense.ProjectedBlock(combat, me), CombatReader.IncomingDamage(combat));
    }
}
