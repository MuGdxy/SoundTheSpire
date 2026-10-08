using SoundTheSpire.Hot.Combat;

namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// One phrase per enemy, left to right, in 4/4 at 120 BPM. Every enemy gets the same number of whole bars
/// (one by default, more only if some enemy needs it); a phrase shorter than that is padded with rests.
/// Instrument = which enemy (see <see cref="EnemyVoices"/>), pan = where it stands, articulation = intent kind,
/// number of strikes = attack hits.
/// Attacks: the harder they hit, the lower, louder, thicker and longer; light attacks are high, soft and thin.
/// No percussion in attacks: drums carry no clear pitch, so the damage tier would be lost.
/// </summary>
public static class IntentMotif
{
    // Per damage tier (the game's attack icon tiers), lightest first.
    private static readonly int[] AttackRoots = { 72, 64, 55, 48, 40 };
    private static readonly int[] AttackVelocities = { 45, 62, 82, 104, 124 };

    private const double BeatSeconds = 60.0 / 120;
    private const double BarSeconds = BeatSeconds * 4;
    private const int Eighths = 2;
    private const int Triplets = 3;
    private const int FullBarHits = 8;

    /// <summary>
    /// Riff per hit count. '1' = open strum, 'x' = palm-muted chug, spaces only group by ear;
    /// <c>StepsPerBeat</c> is the grid (eighth notes or eighth-note triplets).
    /// </summary>
    private static readonly Dictionary<int, (string Pattern, int StepsPerBeat)> Riffs = new()
    {
        [1] = ("1", Eighths),
        [2] = ("11", Eighths),
        [3] = ("1xx", Triplets),
        [4] = ("1xxx", Eighths),
        [5] = ("1xxx 1", Eighths),
        [6] = ("1xx 1xx", Triplets),
        [7] = ("1xx1 xx1", Eighths),
        [8] = ("1xx1 xx1x", Eighths),
        [9] = ("1xx 1xx 1xx", Triplets),
        [10] = ("1xx1 xx1x 11", Eighths),
    };

    private const int PalmMuteVelocityDrop = 20;
    private const double StrumSpread = 0.008;

    public static void Play(SynthEngine engine, IReadOnlyList<EnemyInfo> enemies)
    {
        engine.Stop();
        EnemyVoices.Assign(enemies);
        var phrases = enemies.Select(e => (Enemy: e, Phrase: Compose(e))).ToList();
        var slot = BarsFor(phrases.Select(p => p.Phrase.Length).DefaultIfEmpty(0).Max()) * BarSeconds;
        for (var i = 0; i < phrases.Count; i++)
            Schedule(engine, i * slot, phrases[i].Enemy, phrases[i].Phrase);
    }

    /// <param name="enemies">All living enemies; instruments are ranked across them.</param>
    public static void Play(SynthEngine engine, IReadOnlyList<EnemyInfo> enemies, EnemyInfo enemy)
    {
        engine.Stop();
        EnemyVoices.Assign(enemies);
        Schedule(engine, 0, enemy, Compose(enemy));
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

    private static int BarsFor(double seconds) => Math.Max(1, (int)Math.Ceiling(seconds / BarSeconds - 1e-6));

    private static void Schedule(SynthEngine engine, double start, EnemyInfo enemy, Phrase phrase)
    {
        // Channels 0-7 stay clear of the percussion channel.
        var channel = enemy.Slot % 8;
        var program = EnemyVoices.ProgramOf(enemy);
        var pan = (int)Math.Round(16 + enemy.ScreenX * 95);
        engine.Schedule(start, s =>
        {
            s.SetProgram(channel, program);
            s.SetPan(channel, pan);
        });
        phrase.ScheduleOn(engine, start, channel);
    }

    private static Phrase Compose(EnemyInfo enemy)
    {
        var phrase = new Phrase();
        var t = 0.0;
        foreach (var intent in enemy.Intents)
            t = ComposeIntent(phrase, t, intent);
        return phrase;
    }

    private static double ComposeIntent(Phrase phrase, double t, IntentInfo intent)
    {
        switch (intent.Kind)
        {
            case IntentKind.Attack:
                return ComposeAttack(phrase, t, intent);
            case IntentKind.Defend:
                phrase.Note(t, 0.5, 70, 36, 43);
                return t + 0.6;
            case IntentKind.Buff:
                return Arpeggio(phrase, t, 85, 60, 64, 67, 72);
            case IntentKind.Debuff:
                return Arpeggio(phrase, t, 85, 72, 68, 65, 60);
            default:
                phrase.Note(t, 0.12, 45, 52);
                return t + 0.3;
        }
    }

    private static double ComposeAttack(Phrase phrase, double t, IntentInfo intent)
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
            phrase.Note(t, duration, velocity, keys);
            return t + duration + 0.1;
        }
        return ComposeRiff(phrase, t, velocity, hits, keys.Length >= 2 ? keys : new[] { root, root + 7 });
    }

    /// <summary>
    /// Multi-hit attacks as a power-chord riff from <see cref="Riffs"/>, one strike per hit, each lasting a full step.
    /// Above the table, full 8-hit bars are played first and the remainder uses its own riff.
    /// </summary>
    private static double ComposeRiff(Phrase phrase, double t, int velocity, int hits, int[] keys)
    {
        var maxInTable = Riffs.Keys.Max();
        while (hits > maxInTable)
        {
            t = ComposePattern(phrase, t, velocity, keys, Riffs[FullBarHits]);
            hits -= FullBarHits;
        }
        return ComposePattern(phrase, t, velocity, keys, Riffs[hits]);
    }

    private static double ComposePattern(Phrase phrase, double t, int velocity, int[] keys, (string Pattern, int StepsPerBeat) riff)
    {
        var step = BeatSeconds / riff.StepsPerBeat;
        foreach (var strike in riff.Pattern.Where(c => c != ' '))
        {
            var strikeVelocity = strike == '1' ? velocity : Math.Max(1, velocity - PalmMuteVelocityDrop);
            phrase.Strum(t, step, strikeVelocity, StrumSpread, keys);
            t += step;
        }
        return t;
    }

    private static double Arpeggio(Phrase phrase, double t, int velocity, params int[] keys)
    {
        for (var k = 0; k < keys.Length; k++)
            phrase.Note(t + k * 0.08, 0.15, velocity, keys[k]);
        return t + keys.Length * 0.08 + 0.2;
    }
}
