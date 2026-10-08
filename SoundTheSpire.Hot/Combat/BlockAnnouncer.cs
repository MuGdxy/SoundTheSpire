using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SoundTheSpire.Hot.Audio;

namespace SoundTheSpire.Hot.Combat;

/// <summary>Plays the "pressure resolved" cadence when a block gain makes the local player's block cover all incoming damage.</summary>
public static class BlockAnnouncer
{
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterBlockGained))]
    private static class BlockGainedPatch
    {
        private static void Prefix(ICombatState combatState, Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
        {
            if (!creature.IsPlayer || !LocalContext.IsMe(creature) || SynthEngine.Instance is not { } engine)
                return;
            var block = creature.Block;
            var incoming = CombatReader.IncomingDamage(combatState);
            if (!BlockMotif.Resolves(block - (int)amount, block, incoming))
                return;
            MainFile.Logger.Info($"Block {block} now covers incoming {incoming}: pressure resolved");
            BlockMotif.PlayResolved(engine);
        }
    }
}
