using System.Text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace SoundTheSpire.SoundTheSpireCode.Commands;

/// <summary>Starts an unsaved singleplayer run straight from the main menu.</summary>
public class StsRunConsoleCmd : AbstractConsoleCmd
{
    private static Task? _starting;

    /// <summary>True once the run scene exists and the first room has been entered.</summary>
    public static bool IsRunReady =>
        RunManager.Instance.IsInProgress && NRun.Instance != null && (_starting == null || _starting.IsCompleted);

    public override string CmdName => "sts_run";
    public override string Args => "[character:string] [seed:string]";
    public override string Description => "Start a new unsaved singleplayer run (default: ironclad).";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (RunManager.Instance.IsInProgress)
            return new CmdResult(false, "A run is already in progress.");
        if (NGame.Instance is not { } game)
            return new CmdResult(false, "NGame is not ready.");

        var characterName = args.Length > 0 ? args[0] : "ironclad";
        var character = ModelDb.AllCharacters.FirstOrDefault(c => c.Id.Entry.Equals(characterName, StringComparison.OrdinalIgnoreCase));
        if (character == null)
            return new CmdResult(false, $"Unknown character '{characterName}'. Options: {string.Join(", ", ModelDb.AllCharacters.Select(c => c.Id.Entry))}");

        var seed = args.Length > 1 ? args[1] : "SOUNDTEST";
        var rng = new Rng((uint)StringHelper.GetDeterministicHashCode(seed), "act_selection");
        var acts = ActModel.GetRandomList(rng, SaveManager.Instance.GenerateUnlockStateFromProgress(), isMultiplayer: false).ToList();

        var task = game.StartNewSingleplayerRun(character, shouldSave: false, acts, Array.Empty<ModifierModel>(), seed, GameMode.Standard);
        _starting = task;
        return new CmdResult(task, true, $"Starting {character.Id.Entry} run with seed {seed}.");
    }
}

public class StsMenuConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_menu";
    public override string Args => "";
    public override string Description => "Leave the current run and return to the main menu.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (!RunManager.Instance.IsInProgress)
            return new CmdResult(true, "Already at the main menu.");
        if (NGame.Instance is not { } game)
            return new CmdResult(false, "NGame is not ready.");
        return new CmdResult(game.ReturnToMainMenu(), true, "Returning to main menu.");
    }
}

/// <summary>Replaces an enemy's next move with a scripted intent.</summary>
public class StsIntentConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_intent";
    public override string Args => "<enemy:index|all> <attack DMG [HITS] | defend AMT | buff AMT | debuff AMT>";
    public override string Description => "Force an enemy's next intent.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (CombatManager.Instance.DebugOnlyGetState() is not { } combat)
            return new CmdResult(false, "Not in combat.");
        if (args.Length < 2)
            return new CmdResult(false, "Usage: sts_intent " + Args);

        var enemies = combat.Enemies.Where(e => e.IsAlive && e.Monster != null).ToList();
        List<Creature> targets;
        if (args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
            targets = enemies;
        else if (int.TryParse(args[0], out var index) && index >= 0 && index < enemies.Count)
            targets = new List<Creature> { enemies[index] };
        else
            return new CmdResult(false, $"Enemy must be 'all' or 0..{enemies.Count - 1}.");

        var kind = args[1].ToLowerInvariant();
        var amount = args.Length > 2 && int.TryParse(args[2], out var a) ? a : 0;
        var hits = args.Length > 3 && int.TryParse(args[3], out var h) ? h : 1;

        foreach (var creature in targets)
        {
            var monster = creature.Monster!;
            if (BuildMove(monster, kind, amount, hits) is not { } move)
                return new CmdResult(false, $"Unknown intent '{kind}'.");
            move.FollowUpState = monster.NextMove;
            monster.SetMoveImmediate(move, forceTransition: true);
        }

        return new CmdResult(true, $"Set {targets.Count} enemies to {kind} {amount}" + (hits > 1 ? $"x{hits}" : ""));
    }

    private static MoveState? BuildMove(MonsterModel monster, string kind, int amount, int hits)
    {
        return kind switch
        {
            "attack" => new MoveState("STS_ATTACK", _ => DamageCmd.Attack(amount).WithHitCount(hits).FromMonster(monster)
                    .WithHitFx("vfx/vfx_attack_slash").Execute(null),
                hits > 1 ? new MultiAttackIntent(amount, hits) : new SingleAttackIntent(amount)),
            "defend" => new MoveState("STS_DEFEND", _ => CreatureCmd.GainBlock(monster.Creature, amount, ValueProp.Move, null),
                new DefendIntent()),
            "buff" => new MoveState("STS_BUFF", _ => PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), monster.Creature, amount, monster.Creature, null),
                new BuffIntent()),
            "debuff" => new MoveState("STS_DEBUFF", targets => PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(), targets, amount, monster.Creature, null),
                new DebuffIntent()),
            _ => null,
        };
    }
}

/// <summary>Plain-text snapshot of the run/combat, for checking scenarios without looking at the screen.</summary>
public class StsStateConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_state";
    public override string Args => "";
    public override string Description => "Print run and combat state.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"run: {RunManager.Instance.IsInProgress}");
        sb.AppendLine($"ready: {StsRunConsoleCmd.IsRunReady}");
        sb.AppendLine($"room: {RunManager.Instance.DebugOnlyGetState()?.CurrentRoom?.GetType().Name ?? "none"}");
        var combat = CombatManager.Instance.DebugOnlyGetState();
        sb.AppendLine($"combat: {combat != null && CombatManager.Instance.IsInProgress}");
        if (combat == null)
            return new CmdResult(true, sb.ToString().TrimEnd());

        foreach (var p in combat.PlayerCreatures)
            sb.AppendLine($"player {p.Name}: hp {p.CurrentHp}/{p.MaxHp} block {p.Block}");

        var players = combat.PlayerCreatures;
        var i = 0;
        foreach (var e in combat.Enemies)
        {
            var intents = e.Monster?.NextMove.Intents.Select(intent => DescribeIntent(intent, players, e)) ?? Enumerable.Empty<string>();
            sb.AppendLine($"enemy {i++} {e.Name}: hp {e.CurrentHp}/{e.MaxHp} block {e.Block} alive {e.IsAlive} move {e.Monster?.NextMove.Id} intents [{string.Join(", ", intents)}]");
        }
        return new CmdResult(true, sb.ToString().TrimEnd());
    }

    private static string DescribeIntent(AbstractIntent intent, IReadOnlyList<Creature> targets, Creature owner) =>
        intent is AttackIntent attack
            ? $"{intent.IntentType} {attack.GetSingleDamage(targets, owner)}x{Math.Max(1, attack.Repeats)}"
            : intent.IntentType.ToString();
}
