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
    }

    private void PollTestHotkey()
    {
        var down = Input.IsKeyPressed(Key.F8);
        if (down && !_testKeyWasDown && SynthEngine.Instance is { } engine)
            SoundTest.Play(engine);
        _testKeyWasDown = down;
    }
}
