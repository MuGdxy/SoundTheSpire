using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
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
        return new CmdResult(true, $"Intent veil {(value ? $"on: {IntentVeil.Apply()}" : "off")}.");
    }
}