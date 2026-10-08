using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using SoundTheSpire.Hot.Combat;

namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// Which instrument each enemy plays. The strongest enemies get the front of <see cref="Instruments"/>:
/// primary enemies before minions, then higher max HP (boss, elite leader, medium slime over small ones).
/// An enemy keeps its instrument for the whole combat; one freed by a death goes to the next enemy that appears.
/// </summary>
public static class EnemyVoices
{
    public static readonly (int Program, string Name)[] Instruments =
    {
        (Midi.Program.DistortionGuitar, "guitar"),
        (Midi.Program.BrassSection, "brass"),
        (Midi.Program.StringEnsemble, "strings"),
        (Midi.Program.SquareLead, "square"),
        (Midi.Program.ChurchOrgan, "organ"),
    };

    private static readonly Dictionary<Creature, int> Assigned = new();
    private static ICombatState? _combat;

    /// <param name="enemies">All living enemies, so newcomers are ranked against each other.</param>
    public static void Assign(IReadOnlyList<EnemyInfo> enemies)
    {
        var combat = enemies.FirstOrDefault()?.Creature.CombatState;
        if (!ReferenceEquals(combat, _combat))
        {
            Assigned.Clear();
            _combat = combat;
        }

        var alive = enemies.Select(e => e.Creature).ToHashSet();
        var taken = Assigned.Where(a => alive.Contains(a.Key)).Select(a => a.Value).ToHashSet();
        var newcomers = enemies
            .Where(e => !Assigned.ContainsKey(e.Creature))
            .OrderByDescending(e => e.IsPrimary)
            .ThenByDescending(e => e.MaxHp)
            .ThenBy(e => e.Slot);
        foreach (var enemy in newcomers)
        {
            var index = Enumerable.Range(0, Instruments.Length).Where(i => !taken.Contains(i)).DefaultIfEmpty(-1).First();
            if (index < 0)
                index = Assigned.Count % Instruments.Length;
            Assigned[enemy.Creature] = index;
            taken.Add(index);
        }
    }

    public static int ProgramOf(EnemyInfo enemy) => InstrumentOf(enemy.Creature).Program;

    public static (int Program, string Name) InstrumentOf(Creature enemy) =>
        Instruments[Assigned.TryGetValue(enemy, out var index) ? index : 0];
}
