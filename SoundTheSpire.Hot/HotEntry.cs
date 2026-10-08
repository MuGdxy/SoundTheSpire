using Godot;
using SoundTheSpire.Hot.Audio;
using SoundTheSpire.Hot.Combat;

namespace SoundTheSpire.Hot;

public sealed class HotEntry : IHotModule
{
    private bool _testKeyWasDown;

    public void Load(HotContext context)
    {
        context.OnFrame(PollTestHotkey);
        context.OnFrame(IntentAnnouncer.PollReplayKey);
        context.OnFrame(DefenseMonitor.Poll);
        if (CombatReader.CurrentCombat is { CurrentSide: MegaCrit.Sts2.Core.Combat.CombatSide.Player } combat)
            DefenseMonitor.StartWatching(combat);
    }

    private void PollTestHotkey()
    {
        var down = Input.IsKeyPressed(Key.F8);
        if (down && !_testKeyWasDown && SynthEngine.Instance is { } engine)
            SoundTest.Play(engine);
        _testKeyWasDown = down;
    }
}
