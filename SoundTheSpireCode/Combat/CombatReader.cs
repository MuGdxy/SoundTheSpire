using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace SoundTheSpire.SoundTheSpireCode.Combat;

public enum IntentKind
{
    Attack,
    Defend,
    Buff,
    Debuff,
    Other,
}

public readonly record struct IntentInfo(IntentKind Kind, int DamagePerHit = 0, int Hits = 0)
{
    public int TotalDamage => DamagePerHit * Hits;
}

/// <param name="Slot">Stable index of the enemy within this combat; does not shift when others die.</param>
/// <param name="ScreenX">Horizontal screen position, 0 = left edge, 1 = right edge.</param>
public sealed record EnemyInfo(Creature Creature, int Slot, float ScreenX, IReadOnlyList<IntentInfo> Intents);

/// <summary>
/// Reads what a sighted player can see about the enemies. Everything the sonification knows about combat goes
/// through here, so game-model changes after an update are contained in this file.
/// </summary>
public static class CombatReader
{
    public static ICombatState? CurrentCombat =>
        CombatManager.Instance.IsInProgress ? CombatManager.Instance.DebugOnlyGetState() : null;

    /// <summary>Living enemies, sorted left to right.</summary>
    public static IReadOnlyList<EnemyInfo> ReadEnemies(ICombatState combat)
    {
        var players = combat.PlayerCreatures;
        var enemies = combat.Enemies.ToList();
        return enemies
            .Select((e, slot) => (Enemy: e, Slot: slot))
            .Where(x => x.Enemy.IsAlive && x.Enemy.Monster != null)
            .Select(x => new EnemyInfo(x.Enemy, x.Slot, ScreenX(x.Enemy, x.Slot, enemies.Count), ReadIntents(x.Enemy, players)))
            .OrderBy(e => e.ScreenX)
            .ToList();
    }

    public static EnemyInfo? ReadEnemy(Creature enemy) =>
        enemy.CombatState is { } combat ? ReadEnemies(combat).FirstOrDefault(e => e.Creature == enemy) : null;

    private static List<IntentInfo> ReadIntents(Creature enemy, IReadOnlyList<Creature> players) =>
        enemy.Monster!.NextMove.Intents.Select(intent => Describe(intent, players, enemy)).ToList();

    private static IntentInfo Describe(AbstractIntent intent, IReadOnlyList<Creature> players, Creature owner)
    {
        if (intent is AttackIntent attack)
            return new IntentInfo(IntentKind.Attack, attack.GetSingleDamage(players, owner), Math.Max(1, attack.Repeats));

        return intent.IntentType switch
        {
            IntentType.Defend => new IntentInfo(IntentKind.Defend),
            IntentType.Buff => new IntentInfo(IntentKind.Buff),
            IntentType.Debuff or IntentType.DebuffStrong or IntentType.CardDebuff or IntentType.StatusCard => new IntentInfo(IntentKind.Debuff),
            // Hidden and Unknown must stay ambiguous: never reveal more than the intent icon shows.
            _ => new IntentInfo(IntentKind.Other),
        };
    }

    private static float ScreenX(Creature enemy, int index, int count)
    {
        if (enemy.GetCreatureNode() is { } node && node.IsInsideTree())
        {
            var width = node.GetViewportRect().Size.X;
            if (width > 0)
                return Math.Clamp(node.GetGlobalRect().GetCenter().X / width, 0f, 1f);
        }
        return count <= 1 ? 0.5f : (float)index / (count - 1);
    }
}
