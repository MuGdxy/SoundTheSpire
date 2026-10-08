using SoundTheSpire.Hot.Combat;

namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// One phrase per enemy, left to right. Instrument = which enemy, pan = where it stands,
/// articulation = intent kind, pitch = attack damage tier, number of strikes = attack hits.
/// </summary>
public static class IntentMotif
{
    private static readonly int[] EnemyInstruments =
    {
        Midi.Program.DistortionGuitar,
        Midi.Program.BrassSection,
        Midi.Program.StringEnsemble,
        Midi.Program.SquareLead,
        Midi.Program.ChurchOrgan,
    };

    // Root note per damage tier; the tiers match the game's attack intent icons.
    private static readonly int[] AttackRoots = { 43, 48, 53, 58, 64 };

    private const double GapBetweenEnemies = 0.3;

    public static void Play(SynthEngine engine, IReadOnlyList<EnemyInfo> enemies)
    {
        engine.Stop();
        var t = 0.0;
        foreach (var enemy in enemies)
            t = PlayEnemy(engine, t, enemy) + GapBetweenEnemies;
    }

    public static void Play(SynthEngine engine, EnemyInfo enemy)
    {
        engine.Stop();
        PlayEnemy(engine, 0, enemy);
    }

    private static double PlayEnemy(SynthEngine engine, double t, EnemyInfo enemy)
    {
        // Channels 0-7 stay clear of the percussion channel.
        var channel = enemy.Slot % 8;
        var program = EnemyInstruments[enemy.Slot % EnemyInstruments.Length];
        var pan = (int)Math.Round(16 + enemy.ScreenX * 95);
        engine.Schedule(t, s =>
        {
            s.SetProgram(channel, program);
            s.SetPan(channel, pan);
        });

        foreach (var intent in enemy.Intents)
            t = PlayIntent(engine, t, channel, intent);
        return t;
    }

    /// <summary>Game's attack icon tiers: &lt;5, &lt;10, &lt;20, &lt;40, 40+. Returns 1..5.</summary>
    public static int DamageTier(int totalDamage) => totalDamage switch
    {
        < 5 => 1,
        < 10 => 2,
        < 20 => 3,
        < 40 => 4,
        _ => 5,
    };

    private static double PlayIntent(SynthEngine engine, double t, int channel, IntentInfo intent)
    {
        switch (intent.Kind)
        {
            case IntentKind.Attack:
                return PlayAttack(engine, t, channel, intent);
            case IntentKind.Defend:
                engine.Chord(t, 0.5, channel, 70, 36, 43);
                return t + 0.6;
            case IntentKind.Buff:
                return Arpeggio(engine, t, channel, 85, 60, 64, 67, 72);
            case IntentKind.Debuff:
                return Arpeggio(engine, t, channel, 85, 72, 68, 65, 60);
            default:
                engine.Chord(t, 0.12, channel, 45, 52);
                return t + 0.3;
        }
    }

    private static double PlayAttack(SynthEngine engine, double t, int channel, IntentInfo intent)
    {
        var tier = DamageTier(intent.TotalDamage);
        var root = AttackRoots[tier - 1];
        var keys = tier switch
        {
            <= 2 => new[] { root, root + 7 },
            <= 4 => new[] { root, root + 7, root + 12 },
            _ => new[] { root, root + 7, root + 12, root + 19 },
        };
        var velocity = 70 + tier * 10;

        var hits = Math.Clamp(intent.Hits, 1, 8);
        if (hits == 1)
        {
            engine.Chord(t, 0.4, channel, velocity, keys);
            return t + 0.5;
        }
        for (var h = 0; h < hits; h++)
            engine.Chord(t + h * 0.16, 0.12, channel, velocity, keys);
        return t + hits * 0.16 + 0.15;
    }

    private static double Arpeggio(SynthEngine engine, double t, int channel, int velocity, params int[] keys)
    {
        for (var k = 0; k < keys.Length; k++)
            engine.Chord(t + k * 0.08, 0.15, channel, velocity, keys[k]);
        return t + keys.Length * 0.08 + 0.2;
    }
}
