using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// Block the player will hold when enemies act if they end the turn now, without playing anything: current block
/// plus every end-of-turn source. The sources mirror the game's own end-of-turn hooks; amounts go through
/// <see cref="Hook.ModifyBlock"/> so Dexterity, Frail and the like are applied exactly as the game would.
/// </summary>
public static class PassiveDefense
{
    public readonly record struct Gain(string Source, int Amount);

    public static int ProjectedBlock(ICombatState combat, Creature me) => me.Block + Gains(combat, me).Sum(g => g.Amount);

    public static IReadOnlyList<Gain> Gains(ICombatState combat, Creature me)
    {
        var gains = new List<Gain>();
        if (me.Player is not { } player)
            return gains;

        void Add(string source, decimal amount)
        {
            var modified = (int)Hook.ModifyBlock(combat, me, amount, ValueProp.Unpowered, null, null, out _);
            if (modified > 0)
                gains.Add(new Gain(source, modified));
        }

        // Orichalcum checks block in the very-early end-of-turn hook, before any other source has added some.
        if (me.Block == 0)
        {
            if (player.GetRelic<Orichalcum>() is { } orichalcum)
                Add(nameof(Orichalcum), orichalcum.DynamicVars.Block.BaseValue);
            if (player.GetRelic<FakeOrichalcum>() is { } fake)
                Add(nameof(FakeOrichalcum), fake.DynamicVars.Block.BaseValue);
        }

        if (me.GetPower<PlatingPower>() is { } plating)
            Add(nameof(PlatingPower), plating.Amount);

        if (player.GetRelic<CloakClasp>() is { } clasp && player.PlayerCombatState is { } turn)
            Add(nameof(CloakClasp), turn.Hand.Cards.Count * clasp.DynamicVars.Block.BaseValue);

        if (player.GetRelic<RippleBasin>() is { } basin && !PlayedAttackThisTurn(combat, me))
            Add(nameof(RippleBasin), basin.DynamicVars.Block.BaseValue);

        if (player.PlayerCombatState is { } state)
        {
            foreach (var orb in state.OrbQueue.Orbs)
            {
                if (orb is not FrostOrb frost)
                    continue;
                var triggers = Hook.ModifyOrbPassiveTriggerCount(combat, orb, 1, out _);
                for (var i = 0; i < triggers; i++)
                    Add(nameof(FrostOrb), frost.PassiveVal);
            }
        }

        return gains;
    }

    private static bool PlayedAttackThisTurn(ICombatState combat, Creature me) =>
        CombatManager.Instance.History.CardPlaysFinished.Any((CardPlayFinishedEntry e) =>
            e.HappenedThisTurn(combat) && e.CardPlay.Card.Type == CardType.Attack && e.CardPlay.Card.Owner == me.Player);

    public static string Describe(ICombatState combat, Creature me)
    {
        var gains = Gains(combat, me);
        var parts = gains.Count == 0 ? "" : " + " + string.Join(" + ", gains.Select(g => $"{g.Source} {g.Amount}"));
        return $"block {me.Block}{parts} = {me.Block + gains.Sum(g => g.Amount)}";
    }
}
