using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using SoundTheSpire.SoundTheSpireCode.Audio;
using SoundTheSpire.SoundTheSpireCode.Core;

namespace SoundTheSpire.SoundTheSpireCode.Commands;

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
            $"master_peak_db L {peakLeft:F1} R {peakRight:F1} device {AudioServer.OutputDevice} latency {AudioServer.GetOutputLatency():F3}s " +
            $"hot_generation {HotModuleHost.Generation}");
    }
}

public class StsReloadConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "sts_reload";
    public override string Args => "";
    public override string Description => $"Reload {HotModuleHost.AssemblyName}.dll from the mod folder without restarting the game.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        try
        {
            return new CmdResult(true, HotModuleHost.Load());
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Hot reload failed: {e}");
            return new CmdResult(false, $"Hot reload failed: {e.Message}");
        }
    }
}
