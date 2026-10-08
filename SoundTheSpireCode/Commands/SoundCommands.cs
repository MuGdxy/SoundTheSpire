using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using SoundTheSpire.SoundTheSpireCode.Audio;

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
