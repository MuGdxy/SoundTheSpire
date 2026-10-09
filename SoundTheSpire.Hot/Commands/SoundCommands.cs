using Godot;
using HarmonyLib;
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
            $"source {MusicClock.ActiveProfile?.SourceTrackName ?? "none"} " +
            $"clock {MusicClock.Tempo:F2} BPM {MusicClock.BeatsPerBar}/{MusicClock.BeatUnit} callback {MusicClock.HasFmodBeat} " +
            $"playback {MusicClock.PlaybackState?.ToString() ?? "none"} " +
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

public class StsAnnounceConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_announce";
    public override string Args => "";
    public override string Description => "Replay the localized identity-only announcement for the current encounter.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        VoicePlayback.Stop();
        return new CmdResult(EncounterAnnouncer.Replay(out var report), report);
    }
}

public class StsSpeakConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_speak";
    public override string Args => "<text>";
    public override string Description => "Send text to the active screen reader, with SAPI as fallback.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(false, "Usage: sts_speak " + Args);
        var text = string.Join(" ", args);
        var success = AccessibilitySpeech.Output(text, interrupt: true);
        return new CmdResult(success, $"{AccessibilitySpeech.DriverName}: {text}");
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

public class StsCardVoiceConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_card_voice";
    public override string Args => "<card_id|localized title>";
    public override string Description => "Play a generated card-name announcement.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(false, "Usage: sts_card_voice " + Args);
        var query = string.Join(" ", args);
        var card = ModelDb.AllCards.FirstOrDefault(candidate =>
                candidate.Id.Entry.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? ModelDb.AllCards.FirstOrDefault(candidate =>
                candidate.Title.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? ModelDb.AllCards.FirstOrDefault(candidate =>
                candidate.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (card == null)
            return new CmdResult(false, $"No card matches '{query}'.");
        return new CmdResult(CardAnnouncer.Play(card), $"{card.Id.Entry}: {card.Title}");
    }
}

public class StsCardAnnounceSettingConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_card_announce";
    public override string Args => "[on|off]";
    public override string Description => "Enable or disable card-name announcements.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(true, $"Card name announcements are {(CardAnnouncer.Enabled ? "on" : "off")}.");
        var enabled = args[0] switch
        {
            "on" => true,
            "off" => false,
            _ => (bool?)null,
        };
        if (enabled is not { } value)
            return new CmdResult(false, $"Expected on or off, got '{args[0]}'.");
        CardAnnouncer.SetEnabled(value);
        return new CmdResult(true, $"Card name announcements {(value ? "on" : "off")}.");
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

public class StsVoiceCatalogConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_voice_catalog";
    public override string Args => "<path.json>";
    public override string Description => "Export every monster ID and current localized name for encounter voice authoring.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(false, "Usage: sts_voice_catalog " + Args);
        var path = string.Join(" ", args);
        var catalog = ModelDb.Monsters
            .OrderBy(monster => monster.Id.Entry)
            .ToDictionary(
                monster => monster.Id.Entry,
                monster => new
                {
                    file = $"monsters/{monster.Id.Entry.ToLowerInvariant()}.ogg",
                    text = monster.Title.GetFormattedText(),
                });
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(
            catalog,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return new CmdResult(true, $"Exported {catalog.Count} localized monster names to {path}.");
    }
}

public class StsCardVoiceCatalogConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_card_voice_catalog";
    public override string Args => "<path.json>";
    public override string Description => "Export every card ID and current localized title for voice generation.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(false, "Usage: sts_card_voice_catalog " + Args);
        var path = string.Join(" ", args);
        var catalog = ModelDb.AllCards
            .OrderBy(card => card.Id.Entry)
            .ToDictionary(
                card => card.Id.Entry,
                card => new
                {
                    file = $"cards/{card.Id.Entry.ToLowerInvariant()}.ogg",
                    text = card.Title,
                });
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(
            catalog,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return new CmdResult(true, $"Exported {catalog.Count} localized card names to {path}.");
    }
}

public class StsEncounterRosterCatalogConsoleCmd : AbstractConsoleCmd
{
    private sealed class Entry
    {
        public string File { get; set; } = "";
        public string Text { get; set; } = "";
        public HashSet<string> Encounters { get; set; } = new();
    }

    public override string CmdName => "sts_roster_catalog";
    public override string Args => "<path.json> [samples=512]";
    public override string Description => "Enumerate possible initial enemy rosters and export full localized encounter lines.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return new CmdResult(false, "Usage: sts_roster_catalog " + Args);
        var path = args[0];
        var samples = args.Length > 1 && int.TryParse(args[1], out var parsed) ? Math.Clamp(parsed, 1, 4096) : 512;
        var rngField = AccessTools.Field(typeof(EncounterModel), "_rng");
        var rosters = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var failures = new List<string>();

        foreach (var canonical in ModelDb.AllEncounters.OrderBy(encounter => encounter.Id.Entry))
        {
            try
            {
                for (var seed = 0; seed < samples; seed++)
                {
                    var encounter = canonical.ToMutable();
                    rngField.SetValue(encounter, new Rng((ulong)seed));
                    encounter.GenerateMonstersWithSlots(NullRunState.Instance);
                    var monsters = encounter.MonstersWithSlots.Select(pair => pair.Item1).ToList();
                    var key = string.Join("+", monsters.Select(monster => monster.Id.Entry).Order(StringComparer.Ordinal));
                    if (!rosters.TryGetValue(key, out var entry))
                    {
                        entry = new Entry
                        {
                            File = $"encounters/rosters/{RosterHash(key)}.ogg",
                            Text = EncounterText(monsters),
                        };
                        rosters.Add(key, entry);
                    }
                    entry.Encounters.Add(canonical.Id.Entry);
                }
            }
            catch (Exception e)
            {
                failures.Add($"{canonical.Id.Entry}: {e.Message}");
            }
        }

        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(
            rosters,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        var failureText = failures.Count == 0 ? "" : $" Failures: {string.Join(" | ", failures)}";
        return new CmdResult(true, $"Exported {rosters.Count} unique rosters from {ModelDb.AllEncounters.Count()} encounters ({samples} seeds each).{failureText}");
    }

    private static string RosterHash(string key)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string EncounterText(IReadOnlyList<MonsterModel> monsters)
    {
        var labels = monsters
            .GroupBy(monster => monster.Id.Entry)
            .Select(group =>
            {
                var name = group.First().Title.GetFormattedText();
                var count = group.Count();
                return count == 1 ? name : $"{name}{ChineseCount(count)}只";
            })
            .ToList();
        var roster = labels.Count switch
        {
            0 => "未知敌人",
            1 => labels[0],
            2 => $"{labels[0]}与{labels[1]}",
            _ => $"{string.Join("、", labels.Take(labels.Count - 1))}与{labels[^1]}",
        };
        return $"敌人来袭。{roster}。";
    }

    private static string ChineseCount(int count) => count switch
    {
        2 => "两",
        3 => "三",
        4 => "四",
        5 => "五",
        6 => "六",
        7 => "七",
        8 => "八",
        9 => "九",
        10 => "十",
        _ => count.ToString(),
    };
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