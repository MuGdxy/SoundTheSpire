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
        (Midi.Program.OverdrivenGuitar, "overdriven guitar"),
        (Midi.Program.GuitarHarmonics, "guitar harmonics"),
        (Midi.Program.JazzGuitar, "jazz guitar"),
        (Midi.Program.MutedGuitar, "muted guitar"),
    };

    private static readonly Dictionary<string, (int Program, string Name)[]> ProfileInstruments = new();

    private static (int Program, string Name)[] CurrentInstruments
    {
        get
        {
            if (MusicClock.ActiveProfile is not { } profile)
                return Instruments;
            if (!ProfileInstruments.TryGetValue(profile.Id, out var instruments))
                ProfileInstruments[profile.Id] = instruments =
                    profile.Voices.Select(v => (v.Program, v.Name)).ToArray();
            return instruments;
        }
    }

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
            var instruments = CurrentInstruments;
            var index = Enumerable.Range(0, instruments.Length).Where(i => !taken.Contains(i)).DefaultIfEmpty(-1).First();
            if (index < 0)
                index = Assigned.Count % instruments.Length;
            Assigned[enemy.Creature] = index;
            taken.Add(index);
        }
    }

    public static int ProgramOf(EnemyInfo enemy) => InstrumentOf(enemy.Creature).Program;

    public static MusicVoiceData? SettingsOf(EnemyInfo enemy)
    {
        if (MusicClock.ActiveProfile is not { } profile)
            return null;
        var index = Assigned.TryGetValue(enemy.Creature, out var assigned) ? assigned : 0;
        return profile.Voices.ElementAtOrDefault(index);
    }

    /// <summary>
    /// SoundFont calibration for profiled voices. Per-program velocity floors come from the external music profile.
    /// </summary>
    public static int BalancedVelocity(EnemyInfo enemy, int velocity)
    {
        if (MusicClock.ActiveProfile is not { } profile)
            return velocity;
        var program = ProgramOf(enemy);
        var voice = profile.Voices.FirstOrDefault(v => v.Program == program);
        var minimum = voice?.MinimumVelocity ?? 0;
        var gain = Math.Pow(10, (voice?.GainDb ?? 0) / 40.0);
        return Math.Clamp((int)Math.Round(Math.Max(minimum, velocity) * gain), 1, 127);
    }

    public static (int Program, string Name) InstrumentOf(Creature enemy) =>
        CurrentInstruments[Assigned.TryGetValue(enemy, out var index) ? index : 0];
}
