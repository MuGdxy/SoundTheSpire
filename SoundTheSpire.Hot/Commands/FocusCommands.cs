using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SoundTheSpire.Hot.Audio;
using SoundTheSpire.Hot.Combat;

namespace SoundTheSpire.Hot.Commands;

/// <summary>
/// Moves the game's real UI focus to a creature. FocusEntered drives the normal highlight, hover tips,
/// selection reticle and SoundTheSpire's intent listener, so demos exercise the same path as keyboard navigation.
/// </summary>
public sealed class StsFocusConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_focus";
    public override string Args => "<enemy_index|player|clear>";
    public override string Description => "Move native combat focus to an enemy or the player.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (NCombatRoom.Instance is not { } room || CombatReader.CurrentCombat is not { } combat)
            return new CmdResult(false, "Not in combat.");
        if (args.Length != 1)
            return new CmdResult(false, "Usage: sts_focus " + Args);

        if (args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            room.GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            foreach (var creature in combat.Creatures)
            {
                if (room.GetCreatureNode(creature) is not { } creatureNode)
                    continue;
                if (creatureNode.IsFocused)
                    AccessTools.Method(typeof(NCreature), "OnUnfocus").Invoke(creatureNode, null);
                creatureNode.HideSingleSelectReticle();
            }
            return new CmdResult(true, "Cleared combat focus.");
        }

        NCreature? node;
        string label;
        if (args[0].Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            var player = combat.Players.FirstOrDefault()?.Creature;
            node = player == null ? null : room.GetCreatureNode(player);
            label = "player";
        }
        else if (int.TryParse(args[0], out var slot))
        {
            var enemy = CombatReader.ReadEnemies(combat).FirstOrDefault(candidate => candidate.Slot == slot);
            node = enemy == null ? null : room.GetCreatureNode(enemy.Creature);
            label = $"enemy {slot}";
        }
        else
        {
            return new CmdResult(false, "Target must be an enemy index, 'player', or 'clear'.");
        }

        if (node?.Hitbox is not { } hitbox)
            return new CmdResult(false, $"No focusable node for {label}.");

        if (hitbox.FocusMode == Control.FocusModeEnum.None)
            node.ToggleIsInteractable(true);
        hitbox.GrabFocus();
        if (!node.IsFocused)
            AccessTools.Method(typeof(NCreature), "OnFocus").Invoke(node, null);
        node.ShowSingleSelectReticle();
        return new CmdResult(true, $"Focused {label}; {IntentMotif.LastMelodyReport}.");
    }
}

/// <summary>Restarts the room's actual FMOD background music after a debug encounter jump.</summary>
public sealed class StsRestartMusicConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_restart_music";
    public override string Args => "";
    public override string Description => "Restart the current encounter or act background music.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (NRunMusicController.Instance is not { } controller)
            return new CmdResult(false, "Run music controller is unavailable.");

        var custom = CombatReader.CurrentCombat?.Encounter?.CustomBgm;
        if (!string.IsNullOrWhiteSpace(custom))
        {
            controller.PlayCustomMusic(custom);
            return MusicClock.CurrentTrack == custom && MusicClock.HasAttachedEvent
                ? new CmdResult(true, $"Restarted encounter music: {custom}")
                : new CmdResult(true,
                    $"Encounter music unavailable ({custom}); restored act music: {MusicClock.CurrentTrack ?? "none"}");
        }

        controller.StopMusic();
        AccessTools.Field(typeof(NRunMusicController), "_currentTrack").SetValue(controller, null);
        controller.UpdateMusic();
        MusicClock.RefreshCurrentTrack();
        return new CmdResult(true, $"Restarted act music: {MusicClock.CurrentTrack ?? "none"}");
    }
}

public sealed class StsMelodyConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_melody";
    public override string Args => "";
    public override string Description => "Print the current bar's profiled melody notes.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var subdivisions = Math.Max(
            1,
            MusicClock.ActiveProfile?.Harmony?.MelodySubdivisionsPerBeat ?? 1);
        var stepSeconds = MusicClock.BeatSeconds / subdivisions;
        var count = MusicClock.BeatsPerBar * subdivisions;
        var notes = new List<int>(count);
        var steps = new List<int>(count);
        for (var stepIndex = 0; stepIndex < count; stepIndex++)
        {
            if (HarmonyTimeline.MelodyAt(stepIndex * stepSeconds, out var step) is not { } note)
                return new CmdResult(false, "No melody timeline for the active profile.");
            notes.Add(note);
            steps.Add(step);
        }
        HarmonyTimeline.ChordAt(0, out var bar);
        return new CmdResult(true,
            $"bar {bar}, melody steps {steps[0]}..{steps[^1]}, MIDI [{string.Join(",", notes)}]");
    }
}

/// <summary>Lists every authored custom encounter music event and whether it has an exact profile.</summary>
public sealed class StsMusicCatalogConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_music_catalog";
    public override string Args => "";
    public override string Description => "List custom encounter music events and profile coverage.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var exactPaths = MusicProfileRegistry.All
            .Where(profile => profile.EventPath != "*")
            .Select(profile => profile.EventPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = ModelDb.AllEncounters
            .Where(encounter => !string.IsNullOrWhiteSpace(encounter.CustomBgm))
            .GroupBy(encounter => encounter.CustomBgm)
            .OrderBy(group => group.Key)
            .Select(group =>
                $"{(exactPaths.Contains(group.Key) ? "profiled" : "missing")} {group.Key}: " +
                string.Join(", ", group.Select(encounter => encounter.Id.Entry).Order()))
            .ToArray();
        return new CmdResult(true, lines.Length == 0 ? "No custom encounter music." : string.Join("\n", lines));
    }
}

/// <summary>Checks whether one FMOD event can actually be created from the currently loaded game banks.</summary>
public sealed class StsMusicProbeConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_music_probe";
    public override string Args => "<event_path>";
    public override string Description => "Probe one FMOD music event, then restore act music.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (NRunMusicController.Instance is not { } controller || args.Length != 1)
            return new CmdResult(false, "Usage: sts_music_probe " + Args);
        var path = args[0];
        controller.PlayCustomMusic(path);
        var available = MusicClock.CurrentTrack == path && MusicClock.HasAttachedEvent;
        if (available)
            controller.StopCustomMusic();
        return new CmdResult(true, $"{(available ? "available" : "missing")} {path}");
    }
}

/// <summary>Starts one available FMOD music event and leaves it playing for capture/analysis.</summary>
public sealed class StsMusicPlayConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_music_play";
    public override string Args => "<ceremonial|kin|vantom|event_path>";
    public override string Description => "Play one FMOD music event for offline capture.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (NRunMusicController.Instance is not { } controller || args.Length == 0)
            return new CmdResult(false, "Usage: sts_music_play " + Args);
        var path = args[0].ToLowerInvariant() switch
        {
            "ceremonial" => "event:/music/act1_boss_ceremonial_beast",
            "kin" => "event:/music/act1_boss_the_kin",
            "vantom" => "event:/music/act1_boss_vantom",
            _ => args[0],
        };
        controller.PlayCustomMusic(path);
        return MusicClock.CurrentTrack == path && MusicClock.HasAttachedEvent
            ? new CmdResult(true, $"Playing {path}")
            : new CmdResult(false, $"Unavailable {path}");
    }
}

public abstract class StsFixedMusicPlayConsoleCmd : AbstractConsoleCmd
{
    protected abstract string EventPath { get; }
    public override string Args => "";
    public override string Description => "Play one fixed FMOD boss event for offline capture.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (NRunMusicController.Instance is not { } controller)
            return new CmdResult(false, "Run music controller is unavailable.");
        controller.PlayCustomMusic(EventPath);
        return MusicClock.CurrentTrack == EventPath && MusicClock.HasAttachedEvent
            ? new CmdResult(true, $"Playing {EventPath}")
            : new CmdResult(false, $"Unavailable {EventPath}");
    }
}

public sealed class StsMusicCeremonialConsoleCmd : StsFixedMusicPlayConsoleCmd
{
    public override string CmdName => "sts_music_ceremonial";
    protected override string EventPath => "event:/music/act1_boss_ceremonial_beast";
}

public sealed class StsMusicKinConsoleCmd : StsFixedMusicPlayConsoleCmd
{
    public override string CmdName => "sts_music_kin";
    protected override string EventPath => "event:/music/act1_boss_the_kin";
}

public sealed class StsMusicVantomConsoleCmd : StsFixedMusicPlayConsoleCmd
{
    public override string CmdName => "sts_music_vantom";
    protected override string EventPath => "event:/music/act1_boss_vantom";
}

public sealed class StsMusicSoulFyshConsoleCmd : StsFixedMusicPlayConsoleCmd
{
    public override string CmdName => "sts_music_soul_fysh";
    protected override string EventPath => "event:/music/act1_b_boss_soul_fysh";
}

public sealed class StsMusicWaterfallConsoleCmd : StsFixedMusicPlayConsoleCmd
{
    public override string CmdName => "sts_music_waterfall";
    protected override string EventPath => "event:/music/act1_b_boss_waterfall_giant";
}

public sealed class StsMusicKaiserConsoleCmd : StsFixedMusicPlayConsoleCmd
{
    public override string CmdName => "sts_music_kaiser";
    protected override string EventPath => "event:/music/act2_boss_kaiser_crab";
}

public sealed class StsMusicKnowledgeConsoleCmd : StsFixedMusicPlayConsoleCmd
{
    public override string CmdName => "sts_music_knowledge";
    protected override string EventPath => "event:/music/act2_boss_knowledge_demon";
}

public sealed class StsMusicInsatiableConsoleCmd : StsFixedMusicPlayConsoleCmd
{
    public override string CmdName => "sts_music_insatiable";
    protected override string EventPath => "event:/music/act2_boss_the_insatiable";
}

public sealed class StsMusicQueenConsoleCmd : StsFixedMusicPlayConsoleCmd
{
    public override string CmdName => "sts_music_queen";
    protected override string EventPath => "event:/music/act3_boss_queen";
}

public sealed class StsMusicTestSubjectConsoleCmd : StsFixedMusicPlayConsoleCmd
{
    public override string CmdName => "sts_music_test_subject";
    protected override string EventPath => "event:/music/act3_boss_test_subject";
}

/// <summary>Moves Test Subject directly to its genuine two-headed second form for audio demos.</summary>
public sealed class StsTestSubjectPhase2ConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_test_subject_phase2";
    public override string Args => "";
    public override string Description => "Advance Test Subject directly to phase two and set 10x3.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (CombatReader.CurrentCombat?.Enemies
                .Select(enemy => enemy.Monster)
                .OfType<TestSubject>()
                .FirstOrDefault() is not { } subject)
            return new CmdResult(false, "Test Subject is not in combat.");
        return new CmdResult(Enter(subject), true, "Advancing Test Subject directly to phase two.");
    }

    private static async Task Enter(TestSubject subject)
    {
        var respawns = (int)AccessTools.Property(typeof(TestSubject), "Respawns").GetValue(subject)!;
        if (respawns == 0)
        {
            var respawn = AccessTools.Method(typeof(TestSubject), "RespawnMove");
            await (Task)respawn.Invoke(subject, new object[] { Array.Empty<MegaCrit.Sts2.Core.Entities.Creatures.Creature>() })!;
        }
        SetHits(subject, 3);
    }

    internal static void SetHits(TestSubject subject, int hits)
    {
        AccessTools.Property(typeof(TestSubject), "ExtraMultiClawCount").SetValue(subject, hits - 3);
        var move = (MoveState)subject.MoveStateMachine!.States["MULTI_CLAW_MOVE"];
        subject.SetMoveImmediate(move, forceTransition: true);
    }
}

/// <summary>Changes only Test Subject's native phase-two strike count; it does not play any sound.</summary>
public sealed class StsTestSubjectHitsConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_test_subject_hits";
    public override string Args => "<3|4|5>";
    public override string Description => "Set the native phase-two Multi Claw strike count.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], out var hits) || hits is < 3 or > 5)
            return new CmdResult(false, "Usage: sts_test_subject_hits " + Args);
        if (CombatReader.CurrentCombat?.Enemies
                .Select(enemy => enemy.Monster)
                .OfType<TestSubject>()
                .FirstOrDefault() is not { } subject)
            return new CmdResult(false, "Test Subject is not in combat.");
        StsTestSubjectPhase2ConsoleCmd.SetHits(subject, hits);
        return new CmdResult(true, $"Set Test Subject native Multi Claw to 10x{hits}.");
    }
}
