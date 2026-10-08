using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using SoundTheSpire.SoundTheSpireCode.Audio;
using SoundTheSpire.SoundTheSpireCode.Combat;

namespace SoundTheSpire.SoundTheSpireCode.Commands;

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
    public override string Args => "[screen_index]";
    public override string Description => "Play the enemy intent motif: all enemies, or one by left-to-right position.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
            return IntentAnnouncer.PlayAll()
                ? new CmdResult(true, "Playing all enemy intents.")
                : new CmdResult(false, "Not in combat or synth not running.");

        if (!int.TryParse(args[0], out var index))
            return new CmdResult(false, $"Bad screen index '{args[0]}'.");
        return IntentAnnouncer.PlayEnemy(index)
            ? new CmdResult(true, $"Playing intent of enemy {index} (left to right).")
            : new CmdResult(false, $"No enemy at screen index {index}, or not in combat.");
    }
}

/// <summary>Reports whether audio actually reaches Godot's master bus, since the agent cannot listen.</summary>
public class StsStatusConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_status";
    public override string Args => "";
    public override string Description => "Synth engine and master bus status.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (SynthEngine.Instance is not { } engine)
            return new CmdResult(false, "Synth engine is not running.");

        var peakLeft = AudioServer.GetBusPeakVolumeLeftDb(0, 0);
        var peakRight = AudioServer.GetBusPeakVolumeRightDb(0, 0);
        return new CmdResult(true,
            $"streaming {engine.IsStreaming} rate {engine.SampleRate} voices {engine.ActiveVoices} skips {engine.Skips} " +
            $"master_peak_db L {peakLeft:F1} R {peakRight:F1} device {AudioServer.OutputDevice} latency {AudioServer.GetOutputLatency():F3}s");
    }
}
