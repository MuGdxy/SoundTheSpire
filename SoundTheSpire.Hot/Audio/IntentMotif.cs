using SoundTheSpire.Hot.Combat;

namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// One phrase per enemy, left to right. Instrument = which enemy, pan = where it stands,
/// articulation = intent kind, number of strikes = attack hits.
/// Attacks: the harder they hit, the lower, louder, thicker and longer; light attacks are high, soft and thin.
/// No percussion in attacks: drums carry no clear pitch, so the damage tier would be lost.
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

    // Per damage tier (the game's attack icon tiers), lightest first.
    private static readonly int[] AttackRoots = { 72, 64, 55, 48, 40 };
    private static readonly int[] AttackVelocities = { 45, 62, 82, 104, 124 };

    private const double GapBetweenEnemies = 0.3;

    private const string RiffPattern = "1xx1xx1x";
    private const double RiffBpm = 120;
    private const double RiffStepSeconds = 60.0 / RiffBpm / 4;
    private const double DoubleHitGap = RiffStepSeconds * 2;
    private const int PalmMuteVelocityDrop = 20;
    private const double StrumSpread = 0.008;

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
            1 => new[] { root },
            2 => new[] { root, root + 7 },
            3 => new[] { root, root + 7, root + 12 },
            4 => new[] { root - 12, root, root + 7 },
            _ => new[] { root - 12, root, root + 7, root + 12 },
        };
        var velocity = AttackVelocities[tier - 1];

        var hits = Math.Clamp(intent.Hits, 1, 16);
        if (hits == 1)
        {
            var duration = 0.2 + tier * 0.1;
            engine.Chord(t, duration, channel, velocity, keys);
            return t + duration + 0.1;
        }
        return PlayRiff(engine, t, channel, velocity, hits, keys.Length >= 2 ? keys : new[] { root, root + 7 });
    }

    /// <summary>
    /// Multi-hit attacks as a power-chord riff, one strike per hit on an even grid, following
    /// <see cref="RiffPattern"/> ('1' = open strum, 'x' = palm-muted chug): 3 hits = 1xx triplet, 4 = 1xx1, ...
    /// A double hit is two open strums so it can't be mistaken for the start of a longer riff.
    /// </summary>
    private static double PlayRiff(SynthEngine engine, double t, int channel, int velocity, int hits, int[] keys)
    {
        if (hits == 2)
        {
            engine.Strum(t, DoubleHitGap, channel, velocity, StrumSpread, keys);
            engine.Strum(t + DoubleHitGap, DoubleHitGap, channel, velocity, StrumSpread, keys);
            return t + DoubleHitGap * 2 + 0.15;
        }

        for (var h = 0; h < hits; h++)
        {
            var open = RiffPattern[h % RiffPattern.Length] == '1';
            var strikeVelocity = open ? velocity : Math.Max(1, velocity - PalmMuteVelocityDrop);
            engine.Strum(t + h * RiffStepSeconds, RiffStepSeconds, channel, strikeVelocity, StrumSpread, keys);
        }
        return t + hits * RiffStepSeconds + 0.15;
    }

    private static double Arpeggio(SynthEngine engine, double t, int channel, int velocity, params int[] keys)
    {
        for (var k = 0; k < keys.Length; k++)
            engine.Chord(t + k * 0.08, 0.15, channel, velocity, keys[k]);
        return t + keys.Length * 0.08 + 0.2;
    }
}
