using System.Text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using SoundTheSpire.Hot.Audio;
using SoundTheSpire.Hot.Combat;

namespace SoundTheSpire.Hot.Commands;

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

    public override CmdResult Process(Player? issuingPlayer, string[] args) =>
        Start(args.Length > 0 ? args[0] : "ironclad", args.Length > 1 ? args[1] : "SOUNDTEST");

    public static CmdResult Start(string characterName, string seed, ActModel? firstAct = null)
    {
        if (RunManager.Instance.IsInProgress)
            return new CmdResult(false, "A run is already in progress.");
        if (NGame.Instance is not { } game)
            return new CmdResult(false, "NGame is not ready.");

        var character = ModelDb.AllCharacters.FirstOrDefault(c => c.Id.Entry.Equals(characterName, StringComparison.OrdinalIgnoreCase));
        if (character == null)
            return new CmdResult(false, $"Unknown character '{characterName}'. Options: {string.Join(", ", ModelDb.AllCharacters.Select(c => c.Id.Entry))}");

        NAudioManager.Instance?.StopMusic();
        var rng = new Rng((uint)StringHelper.GetDeterministicHashCode(seed), "act_selection");
        var acts = ActModel.GetRandomList(rng, SaveManager.Instance.GenerateUnlockStateFromProgress(), isMultiplayer: false).ToList();
        if (firstAct != null)
        {
            if (acts.Count == 0)
                acts.Add(firstAct);
            else
                acts[0] = firstAct;
        }

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

/// <summary>Starts a run whose deterministic music roll selects one requested regular-music event.</summary>
public class StsRegularTestConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_regular_test";
    public override string Args => "[overgrowth-a1|overgrowth-a2|underdocks|hive-a1|hive-a2|glory-a1|glory-a2]";
    public override string Description => "Start an unsaved regular-music test run with a deterministic track.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (RunManager.Instance.IsInProgress)
            return new CmdResult(false, "Return to the main menu first.");
        var requested = args.FirstOrDefault()?.ToLowerInvariant() ?? "overgrowth-a1";
        var (act, desired, optionCount, label) = requested switch
        {
            "a1" or "overgrowth-a1" => (ModelDb.Act<Overgrowth>() as ActModel, 0, 2, "OVERGROWTH_A1"),
            "a2" or "overgrowth-a2" => (ModelDb.Act<Overgrowth>(), 1, 2, "OVERGROWTH_A2"),
            "underdocks" => (ModelDb.Act<Underdocks>(), 0, 1, "UNDERDOCKS"),
            "hive-a1" => (ModelDb.Act<Hive>(), 0, 2, "HIVE_A1"),
            "hive-a2" => (ModelDb.Act<Hive>(), 1, 2, "HIVE_A2"),
            "glory-a1" => (ModelDb.Act<Glory>(), 0, 2, "GLORY_A1"),
            "glory-a2" => (ModelDb.Act<Glory>(), 1, 2, "GLORY_A2"),
            _ => (null, -1, 0, ""),
        };
        if (act == null)
            return new CmdResult(false, "Usage: sts_regular_test " + Args);
        for (var i = 0; i < 100; i++)
        {
            var seed = $"{label}_{i}";
            var runSeed = new RunRngSet(seed).Seed;
            if (new Rng(runSeed, "bg_music").NextInt(0, optionCount) == desired)
                return StsRunConsoleCmd.Start("ironclad", seed, act);
        }
        return new CmdResult(false, "Could not find a deterministic music seed.");
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

    internal static MoveState? BuildMove(MonsterModel monster, string kind, int amount, int hits)
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

/// <summary>Ends the local player's turn, as the End Turn button does.</summary>
public class StsPlayConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_play";
    public override string Args => "<hand_index> [enemy_index]";
    public override string Description => "Play a card from hand as if clicked (indices as in sts_state).";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (CombatReader.CurrentCombat is not { } combat || LocalContext.GetMe(combat) is not { PlayerCombatState: { } turn })
            return new CmdResult(false, "Not in combat.");
        var hand = turn.Hand.Cards;
        if (args.Length == 0 || !int.TryParse(args[0], out var index) || index < 0 || index >= hand.Count)
            return new CmdResult(false, $"Bad hand index, hand has {hand.Count} cards.");
        Creature? target = null;
        if (args.Length > 1)
        {
            if (!int.TryParse(args[1], out var enemy) || enemy < 0 || enemy >= combat.Enemies.Count)
                return new CmdResult(false, $"Bad enemy index '{args[1]}'.");
            target = combat.Enemies[enemy];
        }
        var card = hand[index];
        return card.TryManualPlay(target)
            ? new CmdResult(true, $"Playing {card.Id}.")
            : new CmdResult(false, $"{card.Id} can't be played on that target.");
    }
}

public class StsTutorialConsoleCmd : AbstractConsoleCmd
{
    public const string Name = "sts_tutorial";

    public override string CmdName => Name;
    public override string Args => "";
    public override string Description => "Start the dedicated listening-tutorial run from the main menu.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args) =>
        RunManager.Instance.IsInProgress
            ? new CmdResult(false, "The tutorial can only be started from the main menu.")
            : TutorialButton.StartFromMainMenu();
}

/// <summary>Waterfall music test: one 15-damage attack against three consecutive Defends.</summary>
public class StsDefenseChainConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_defense_chain";
    public override string Args => "";
    public override string Description => "In the tutorial fight, keep one 15-damage enemy and deal three Defends.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (CombatManager.Instance.DebugOnlyGetState() is not { CurrentSide: CombatSide.Player } combat ||
            combat.Enemies.FirstOrDefault(e => e.IsAlive && e.Monster is WaterfallGiant) is not { } giant)
            return new CmdResult(false, "Run this inside the Waterfall tutorial fight.");
        return new CmdResult(SetUp(combat, giant), true, "Setting up 15 damage against three Defends.");
    }

    private static async Task SetUp(CombatState combat, Creature giant)
    {
        ListeningTutorial.Stop();
        var others = combat.Enemies.Where(e => e.IsAlive && !ReferenceEquals(e, giant)).ToList();
        await CreatureCmd.Kill(others, force: true);

        var monster = giant.Monster!;
        var attack = StsIntentConsoleCmd.BuildMove(monster, "attack", 15, 1)!;
        attack.FollowUpState = monster.NextMove;
        monster.SetMoveImmediate(attack, forceTransition: true);

        foreach (var player in combat.Players)
        {
            foreach (var card in PileType.Hand.GetPile(player).Cards.ToList())
                await CardPileCmd.Add(card, PileType.Draw);
            for (var i = 0; i < 3; i++)
                await CardPileCmd.Add(combat.CreateCard(ModelDb.Card<DefendIronclad>(), player), PileType.Hand);
        }

        IntentAnnouncer.PlayAll();
        DefenseMonitor.StartWatching(combat);
    }
}

public class StsEndTurnConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_endturn";
    public override string Args => "";
    public override string Description => "End the local player's turn.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (CombatReader.CurrentCombat is not { } combat || LocalContext.GetMe(combat) is not { PlayerCombatState: { } turnState } me)
            return new CmdResult(false, "Not in combat.");
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(me, turnState.TurnNumber));
        return new CmdResult(true, $"Ending turn {turnState.TurnNumber}.");
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
        Player? localPlayer;
        try
        {
            localPlayer = LocalContext.GetMe(combat);
        }
        catch (InvalidOperationException)
        {
            localPlayer = null;
            sb.AppendLine("local player: transitioning");
        }
        if (localPlayer?.Creature is { } me)
            sb.AppendLine($"defense at end of turn: {PassiveDefense.Describe(combat, me)}, incoming {CombatReader.IncomingDamage(combat)}");
        if (localPlayer?.PlayerCombatState is { } turn)
            sb.AppendLine($"hand: {string.Join(", ", turn.Hand.Cards.Select((c, i) => $"{i} {c.Id}"))}");
        if (localPlayer == null)
            return new CmdResult(true, sb.ToString().TrimEnd());

        var players = combat.PlayerCreatures;
        EnemyVoices.Assign(CombatReader.ReadEnemies(combat));
        var i = 0;
        foreach (var e in combat.Enemies)
        {
            var intents = e.Monster?.NextMove.Intents.Select(intent => DescribeIntent(intent, players, e)) ?? Enumerable.Empty<string>();
            var voice = e.IsAlive ? EnemyVoices.InstrumentOf(e).Name : "-";
            sb.AppendLine($"enemy {i++} {e.Name}: hp {e.CurrentHp}/{e.MaxHp} block {e.Block} alive {e.IsAlive} primary {e.IsPrimaryEnemy} voice {voice} move {e.Monster?.NextMove.Id} intents [{string.Join(", ", intents)}]");
        }
        return new CmdResult(true, sb.ToString().TrimEnd());
    }

    private static string DescribeIntent(AbstractIntent intent, IReadOnlyList<Creature> targets, Creature owner) =>
        intent is AttackIntent attack
            ? $"{intent.IntentType} {attack.GetSingleDamage(targets, owner)}x{Math.Max(1, attack.Repeats)}"
            : intent.IntentType.ToString();
}
