using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using SoundTheSpire.Hot.Audio;
using SoundTheSpire.Hot.Combat;

namespace SoundTheSpire.Hot.Commands;

public class StsTestConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_test";
    public override string Args => "";
    public override string Description => "Play the synth test: piano left, guitar center, drums right.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (SynthEngine.Instance is not { } engine)
            return new CmdResult(false, "Synth engine is not running.");
        SoundTest.Play(engine);
        return new CmdResult(true, "Playing sound test.");
    }
}

public class StsMusicConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_music";
    public override string Args => "";
    public override string Description => "Show the current FMOD track, matched profile and live meter.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var profile = MusicClock.ActiveProfile?.Id ?? "none";
        var state = RunManager.Instance.DebugOnlyGetState();
        var roll = state?.Act.BgMusicOptions.Length > 0
            ? new Rng(state.Rng.Seed, "bg_music").NextInt(0, state.Act.BgMusicOptions.Length)
            : -1;
        return new CmdResult(true,
            $"track {MusicClock.CurrentTrack ?? "none"} profile {profile} key {MusicClock.ActiveProfile?.Key ?? "none"} " +
            $"clock {MusicClock.Tempo:F2} BPM {MusicClock.BeatsPerBar}/{MusicClock.BeatUnit} callback {MusicClock.HasFmodBeat} " +
            $"loaded_profiles {MusicProfileRegistry.All.Count} run_seed {state?.Rng.Seed} bg_roll {roll}");
    }
}

public class StsIntentsConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_intents";
    public override string Args => "[enemy_index]";
    public override string Description => "Play the enemy intent motif: all enemies left to right, or one enemy (index as in sts_state).";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return IntentAnnouncer.PlayAll()
                ? new CmdResult(true, "Playing intents of all enemies, left to right.")
                : new CmdResult(false, "Not in combat or synth not running.");

        if (!int.TryParse(args[0], out var index))
            return new CmdResult(false, $"Bad enemy index '{args[0]}'.");
        return IntentAnnouncer.PlayEnemy(index)
            ? new CmdResult(true, $"Playing intent of enemy {index}.")
            : new CmdResult(false, $"No living enemy {index}, or not in combat.");
    }
}

public class StsScreenshotConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_screenshot";
    public override string Args => "<path.png>";
    public override string Description => "Save the current frame as a PNG.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(false, "Missing output path.");
        if (Engine.GetMainLoop() is not SceneTree tree)
            return new CmdResult(false, "No scene tree.");
        var path = string.Join(" ", args);
        var error = tree.Root.GetTexture().GetImage().SavePng(path);
        var window = $"window {DisplayServer.WindowGetMode()}, frames drawn {Engine.GetFramesDrawn()}";
        return error == Error.Ok ? new CmdResult(true, $"Saved {path} ({window}).") : new CmdResult(false, $"SavePng failed: {error}.");
    }
}

public class StsTipsConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_tips";
    public override string Args => "<enemy_index>";
    public override string Description => "Print the hover tips an enemy shows (index as in sts_state).";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (CombatReader.CurrentCombat is not { } combat)
            return new CmdResult(false, "Not in combat.");
        if (args.Length == 0 || !int.TryParse(args[0], out var index) || index < 0 || index >= combat.Enemies.Count)
            return new CmdResult(false, "Bad enemy index.");
        var enemy = combat.Enemies[index];
        if (NCombatRoom.Instance?.GetCreatureNode(enemy) is not { } node)
            return new CmdResult(false, "Enemy has no node.");

        IntentVeil.ClearLastShown();
        node.ShowHoverTips(enemy.HoverTips);
        node.HideHoverTips();
        var shown = IntentVeil.LastShown ?? enemy.HoverTips.ToList();
        var source = IntentVeil.LastShown != null ? "veiled" : "unchanged";
        var tips = shown.Select(t => t is HoverTip tip ? $"[{tip.Title}] {tip.Description}" : t.GetType().Name);
        return new CmdResult(true, $"{source}:\n{string.Join("\n", tips)}");
    }
}

public class StsCardsConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_cards";
    public override string Args => "";
    public override string Description => "Print the hand's card text as shown in combat, and the deck's text as shown outside it.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (CombatReader.CurrentCombat is not { } combat || LocalContext.GetMe(combat) is not { PlayerCombatState: { } turn } me)
            return new CmdResult(false, "Not in combat.");
        var hand = turn.Hand.Cards.Select((c, i) => $"hand {i} {c.Id}: {c.GetDescriptionForPile(PileType.Hand)}");
        var deck = me.Deck.Cards.Take(1).Select(c => $"deck {c.Id}: {c.GetDescriptionForPile(PileType.Deck)}");
        return new CmdResult(true, string.Join("\n", hand.Concat(deck)));
    }
}

public class StsFindCardConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_findcard";
    public override string Args => "<text>";
    public override string Description => "List cards whose shown title contains the text, with id, pool, cost and text.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(false, "Usage: sts_findcard " + Args);
        var text = string.Join(" ", args);
        var found = ModelDb.AllCards
            .Where(c => c.Title.Contains(text, StringComparison.OrdinalIgnoreCase) || c.Id.Entry.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Select(c => $"{c.Id.Entry} {c.Title} [{c.Pool.Title}] cost {c.EnergyCost.Canonical}: {c.GetDescriptionForPile(PileType.None)}");
        return new CmdResult(true, string.Join("\n", found));
    }
}

public class StsFindMonsterConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_findmonster";
    public override string Args => "<text>";
    public override string Description => "List monsters whose shown name (or id) contains the text, with their type name.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(false, "Usage: sts_findmonster " + Args);
        var text = string.Join(" ", args);
        var found = ModelDb.Monsters
            .Select(m => (Model: m, Name: m.Title.GetFormattedText()))
            .Where(m => m.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || m.Model.Id.Entry.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Select(m => $"{m.Model.Id.Entry} {m.Name} ({m.Model.GetType().Name})");
        return new CmdResult(true, string.Join("\n", found));
    }
}

public class StsVeilConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_veil";
    public override string Args => "[on|off]";
    public override string Description => "Hide enemy intent icons so intents are judged by ear (toggle without argument, also F9).";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var enabled = args.Length == 0 ? !IntentVeil.Enabled : args[0] switch
        {
            "on" => true,
            "off" => false,
            _ => (bool?)null,
        };
        if (enabled is not { } value)
            return new CmdResult(false, $"Expected on or off, got '{args[0]}'.");
        IntentVeil.Set(value);
        var intents = value ? $"on: {IntentVeil.Apply()}" : "off";
        return new CmdResult(true, $"Intent veil {intents}; {NumberVeil.Report()}.");
    }
}