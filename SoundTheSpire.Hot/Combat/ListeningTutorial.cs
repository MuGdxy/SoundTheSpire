using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
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
/// four rising tiers (left to right), the hand holds one attack that kills each slime and one big block that covers the
/// Giant, who is already in his last, unkillable phase. The player speaks each step in a speech bubble; the next step
/// starts when the previous one has been done.
/// </summary>
public static class ListeningTutorial
{
    public const string Encounter = "WATERFALL_GIANT_BOSS";

    private static readonly (Func<MonsterModel> Model, int Hp, int Attack)[] Slimes =
    {
        (() => ModelDb.Monster<TwigSlimeS>(), 7, 3),
        (() => ModelDb.Monster<LeafSlimeS>(), 11, 8),
        (() => ModelDb.Monster<LeafSlimeM>(), 32, 15),
    };
    private const int GiantAttack = 30;
    private const int Energy = 7;

    private const string ListenText =
        "声音教学。四只怪从左到右依次亮出攻击，一个比一个重。\n" +
        "最后一小节是这回合你会挨的打：越刺耳，伤得越重。\n" +
        "把鼠标依次移到每只怪身上，单独听它；按 R 重听全部。";
    private const string KillText =
        "现在动手：双重打击打最左边的，御血术打第二只，重锤打第三只。\n" +
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

    /// <summary>Jumps the run to the Giant's fight; the lesson is set up when the first turn begins.</summary>
    public static Task EnterFight()
    {
        Stop();
        var encounter = ModelDb.GetById<EncounterModel>(new ModelId(ModelId.SlugifyCategory<EncounterModel>(), Encounter)).ToMutable();
        encounter.DebugRandomizeRng();
        _pending = true;
        return RunManager.Instance.EnterRoomDebug(RoomType.Monster, MapPointType.Unassigned, encounter);
    }

    public static void OnTurnStarted(CombatState combat)
    {
        if (!_pending || combat.CurrentSide != CombatSide.Player || !IsGiantFight(combat) || LocalContext.GetMe(combat) is not { } me)
            return;
        _pending = false;
        Start(combat, me).ContinueWith(t => MainFile.Logger.Error($"Tutorial setup failed: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
    }

    public static void PollStartKey()
    {
        var down = Input.IsKeyPressed(StartKey);
        if (down && !_startKeyWasDown && RunManager.Instance.IsInProgress && RunManager.Instance.IsSingleplayerOrFakeMultiplayer && !CombatManager.Instance.IsInProgress)
            EnterFight();
        _startKeyWasDown = down;
    }

    public static async Task Start(CombatState combat, Player me)
    {
        Stop();
        var giant = combat.Enemies.First(e => e.IsAlive && e.Monster is WaterfallGiant);

        foreach (var (model, hp, attack) in Slimes)
        {
            var slime = await CreatureCmd.Add(model().ToMutable(), combat);
            await CreatureCmd.SetMaxAndCurrentHp(slime, hp);
            SetAttack(slime, attack);
            SlimeCreatures.Add(slime);
        }
        if (NCombatRoom.Instance is { } room)
        {
            await room.ToSignal(room.GetTree(), SceneTree.SignalName.ProcessFrame);
            Layout(room, combat, SlimeCreatures.Append(giant).ToList());
        }

        await EnterLastPhase((WaterfallGiant)giant.Monster!);

        foreach (var card in PileType.Hand.GetPile(me).Cards.ToList())
            await CardPileCmd.Add(card, PileType.Draw);
        foreach (var canonical in new CardModel[] { ModelDb.Card<TwinStrike>(), ModelDb.Card<Hemokinesis>(), ModelDb.Card<Bludgeon>(), ModelDb.Card<Impervious>() })
            await CardPileCmd.Add(combat.CreateCard(canonical, me), PileType.Hand);
        await PlayerCmd.SetEnergy(Energy, me);

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
                Advance(Step.Kill, KillText);
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
