using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SoundTheSpire.Hot.Audio;
using SoundTheSpire.Hot.Commands;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// One-turn lesson in hearing numbers, set up inside the Waterfall Giant fight: three slimes and the Giant attack in
/// four rising tiers (left to right). Each player's hand: Turbo for energy, three free Midnights (one kills a slime) and
/// Impervious to cover the Giant, who is already in his last, unkillable phase. The player speaks each step in a speech bubble; the next step
/// starts when the previous one has been done.
/// </summary>
public static class ListeningTutorial
{
    public const string Encounter = "WATERFALL_GIANT_BOSS";

    private static readonly (Func<MonsterModel> Model, int Attack)[] Slimes =
    {
        (() => ModelDb.Monster<TwigSlimeS>(), 3),
        (() => ModelDb.Monster<LeafSlimeS>(), 8),
        (() => ModelDb.Monster<LeafSlimeM>(), 15),
    };
    private const int GiantAttack = 30;

    private const string ListenText =
        "声音教学。四只怪从左到右依次亮出攻击，一个比一个重。\n" +
        "最后一小节是这回合你会挨的打：越刺耳，伤得越重。\n" +
        "把鼠标依次移到每只怪身上，单独听它；按 R 重听全部。";
    private const string KillText =
        "现在动手：先打内核加速补足费用，再用午夜把三只史莱姆一只只杀掉。\n" +
        "每杀一只，它的攻击就不会落到你身上，听防御的和弦一步步缓和。";
    private const string KillTextTogether =
        "现在动手：先打内核加速补足费用，再和队友一起用午夜把三只史莱姆杀掉。\n" +
        "每杀一只，它的攻击就不会落到你身上，听防御的和弦一步步缓和。";
    private const string BlockText =
        "瀑布巨兽已进入最后阶段，锁血无敌，杀不死。\n" +
        "只能防：打出岿然不动，挡住它的致命一击。";
    private const string ResolvedText =
        "悬着的和弦落回了大三和弦：完美解决，这回合你不会受伤。\n" +
        "结束回合，看它的攻击被挡下。";

    private enum Step { Off, Listen, Kill, Block, Resolved }

    private static Step _step;
    private static ICombatState? _combat;
    private static readonly List<Creature> SlimeCreatures = new();
    private static readonly HashSet<Creature> Heard = new();
    private static NSpeechBubbleVfx? _bubble;

    public const Key StartKey = Key.F10;

    private static bool _pending;
    private static bool _startKeyWasDown;

    public static bool IsGiantFight(ICombatState combat) =>
        combat.Enemies.Any(e => e.IsAlive && e.Monster is WaterfallGiant);

    private static bool Multiplayer => !RunManager.Instance.IsSingleplayerOrFakeMultiplayer;

    /// <summary>
    /// Runs sts_tutorial for everyone: in multiplayer it goes through the synchronized action queue like the game's own
    /// networked console commands, so every peer (all need the mod) enters the fight and sets it up identically.
    /// </summary>
    public static void Request()
    {
        if (LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState()) is not { } me)
            return;
        if (Multiplayer)
        {
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new ConsoleCmdGameAction(me, StsTutorialConsoleCmd.Name, CombatManager.Instance.IsInProgress));
            return;
        }
        var result = Run(me);
        if (result.task != null)
            TaskHelper.RunSafely(result.task);
        if (!result.success)
            MainFile.Logger.Warn($"Tutorial: {result.msg}");
    }

    /// <summary>
    /// sts_tutorial on one peer. Outside combat: enter the Giant's fight; the issuer asks again once the first turn
    /// begins. In that fight: set up the lesson.
    /// </summary>
    public static CmdResult Run(Player? issuer)
    {
        if (!RunManager.Instance.IsInProgress)
            return new CmdResult(false, "Start a run first.");
        if (CombatManager.Instance.IsInProgress)
        {
            if (CombatManager.Instance.DebugOnlyGetState() is not { } combat || !IsGiantFight(combat))
                return new CmdResult(false, "Run it outside combat, or in the tutorial's Waterfall Giant fight.");
            if (combat.CurrentSide != CombatSide.Player)
                return new CmdResult(false, "Wait for the player turn.");
            return new CmdResult(Start(combat), true, "Tutorial set up.");
        }
        Stop();
        _pending = issuer == null || LocalContext.IsMe(issuer);
        var encounter = ModelDb.GetById<EncounterModel>(new ModelId(ModelId.SlugifyCategory<EncounterModel>(), Encounter)).ToMutable();
        return new CmdResult(RunManager.Instance.EnterRoomDebug(RoomType.Monster, MapPointType.Unassigned, encounter), true,
            "Entering the Waterfall Giant fight; the tutorial starts on the first turn.");
    }

    public static void OnTurnStarted(CombatState combat)
    {
        if (!_pending || combat.CurrentSide != CombatSide.Player || !IsGiantFight(combat))
            return;
        _pending = false;
        Request();
    }

    public static void PollStartKey()
    {
        var down = Input.IsKeyPressed(StartKey);
        if (down && !_startKeyWasDown && RunManager.Instance.IsInProgress && !CombatManager.Instance.IsInProgress)
            Request();
        _startKeyWasDown = down;
    }

    /// <summary>Changes only the setup, for every player alike; HP scaling, targeting and energy are the standard game.</summary>
    private static async Task Start(CombatState combat)
    {
        Stop();
        var giant = combat.Enemies.First(e => e.IsAlive && e.Monster is WaterfallGiant);

        foreach (var (model, attack) in Slimes)
        {
            var slime = await CreatureCmd.Add(model().ToMutable(), combat);
            SetAttack(slime, attack);
            SlimeCreatures.Add(slime);
        }
        if (NCombatRoom.Instance is { } room)
        {
            await room.ToSignal(room.GetTree(), SceneTree.SignalName.ProcessFrame);
            Layout(room, combat, SlimeCreatures.Append(giant).ToList());
        }

        await EnterLastPhase((WaterfallGiant)giant.Monster!);

        foreach (var player in combat.Players)
        {
            foreach (var card in PileType.Hand.GetPile(player).Cards.ToList())
                await CardPileCmd.Add(card, PileType.Draw);
            await CardPileCmd.Add(combat.CreateCard(ModelDb.Card<Turbo>(), player), PileType.Hand);
            for (var i = 0; i < Slimes.Length; i++)
            {
                var midnight = combat.CreateCard(ModelDb.Card<Midnight>(), player);
                midnight.SetToFreeThisCombat();
                await CardPileCmd.Add(midnight, PileType.Hand);
            }
            await CardPileCmd.Add(combat.CreateCard(ModelDb.Card<Impervious>(), player), PileType.Hand);
        }

        _combat = combat;
        _step = Step.Listen;
        MainFile.Logger.Info("Tutorial: set up, listening step");
        Say(ListenText);
        IntentAnnouncer.PlayAll();
        DefenseMonitor.StartWatching(combat);
    }

    public static void Stop()
    {
        _step = Step.Off;
        _combat = null;
        SlimeCreatures.Clear();
        Heard.Clear();
        if (_bubble != null && GodotObject.IsInstanceValid(_bubble))
            _bubble.QueueFree();
        _bubble = null;
    }

    public static void OnEnemySelected(Creature enemy)
    {
        if (_step == Step.Listen)
            Heard.Add(enemy);
    }

    public static void Poll()
    {
        if (_step == Step.Off)
            return;
        if (_combat is not { } combat || !ReferenceEquals(combat, CombatReader.CurrentCombat) || !CombatManager.Instance.IsInProgress)
        {
            Stop();
            return;
        }
        if (combat.CurrentSide != CombatSide.Player || LocalContext.GetMe(combat)?.Creature is not { } me)
            return;

        var slimesLeft = SlimeCreatures.Count(s => s.IsAlive);
        switch (_step)
        {
            case Step.Listen when Heard.Count(c => c.IsAlive) >= combat.Enemies.Count(e => e.IsAlive) || slimesLeft < SlimeCreatures.Count:
                Advance(Step.Kill, Multiplayer ? KillTextTogether : KillText);
                break;
            case Step.Kill when slimesLeft == 0:
                Advance(Step.Block, BlockText);
                break;
            case Step.Block when PassiveDefense.ProjectedBlock(combat, me) >= CombatReader.IncomingDamage(combat):
                Advance(Step.Resolved, ResolvedText);
                break;
        }
    }

    private static void Advance(Step step, string text)
    {
        _step = step;
        MainFile.Logger.Info($"Tutorial: {step} step");
        Say(text);
    }

    private static void Say(string text)
    {
        if (_bubble != null && GodotObject.IsInstanceValid(_bubble))
            _bubble.QueueFree();
        _bubble = null;
        if (_combat is not { } combat || LocalContext.GetMe(combat)?.Creature is not { } me)
            return;
        _bubble = NSpeechBubbleVfx.Create(text, me, 1e9);
        if (_bubble != null)
            me.GetVfxContainer()?.AddChildSafely(_bubble);
    }

    private static void SetAttack(Creature creature, int damage)
    {
        var monster = creature.Monster!;
        var move = StsIntentConsoleCmd.BuildMove(monster, "attack", damage, 1)!;
        move.FollowUpState = monster.NextMove;
        monster.SetMoveImmediate(move, forceTransition: true);
    }

    /// <summary>The Giant's own knockout: infinite HP without numbers, then the eruption as his only move.</summary>
    private static async Task EnterLastPhase(WaterfallGiant giant)
    {
        await giant.TriggerAboutToBlowState();
        AccessTools.Property(typeof(WaterfallGiant), "SteamEruptionDamage").SetValue(giant, GiantAttack);
        var aboutToBlow = (MoveState)AccessTools.Property(typeof(WaterfallGiant), "AboutToBlowState").GetValue(giant)!;
        giant.SetMoveImmediate((MoveState)aboutToBlow.FollowUpState!, forceTransition: true);
    }

    /// <summary>Creatures added mid-fight get no position in an encounter without slots, so lay out the row again.</summary>
    private static void Layout(NCombatRoom room, ICombatState combat, List<Creature> leftToRight)
    {
        var nodes = leftToRight.Select(room.GetCreatureNode).OfType<NCreature>().ToList();
        AccessTools.Method(typeof(NCombatRoom), "PositionEnemies").Invoke(room, new object[] { nodes, combat.Encounter?.GetCameraScaling() ?? 1f });
        AccessTools.Method(typeof(NCombatRoom), "AdjustCreatureScaleForAspectRatio").Invoke(room, null);
    }
}
