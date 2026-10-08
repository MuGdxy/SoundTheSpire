using Godot;
using MegaCrit.Sts2.Core.Combat;
using SoundTheSpire.Hot.Audio;
using SoundTheSpire.Hot.Combat;

namespace SoundTheSpire.Hot;

public sealed class HotEntry : IHotModule
{
    private bool _testKeyWasDown;

    public void Load(HotContext context)
    {
        MusicClock.RefreshCurrentTrack();
        context.OnFrame(PollTestHotkey);
        context.OnFrame(IntentAnnouncer.PollReplayKey);
        context.OnFrame(DefenseMonitor.Poll);
        context.OnFrame(IntentVeil.Poll);
        context.OnFrame(NumberVeil.Poll);
        context.OnFrame(ListeningTutorial.Poll);
        context.OnFrame(TutorialButton.Poll);
        NumberVeil.Refresh();
        CombatManager.Instance.TurnStarted += IntentAnnouncer.OnTurnStarted;
        CombatManager.Instance.TurnStarted += ListeningTutorial.OnTurnStarted;
        CombatManager.Instance.CombatWon += ListeningTutorial.OnCombatWon;
        CombatManager.Instance.CombatEnded += ListeningTutorial.OnCombatEnded;
        if (CombatReader.CurrentCombat is { CurrentSide: CombatSide.Player } combat)
            DefenseMonitor.StartWatching(combat);
    }

    public void Unload()
    {
        CombatManager.Instance.TurnStarted -= IntentAnnouncer.OnTurnStarted;
        CombatManager.Instance.TurnStarted -= ListeningTutorial.OnTurnStarted;
        CombatManager.Instance.CombatWon -= ListeningTutorial.OnCombatWon;
        CombatManager.Instance.CombatEnded -= ListeningTutorial.OnCombatEnded;
        IntentVeil.Set(false);
        ListeningTutorial.Stop();
        TutorialButton.Remove();
    }

    private void PollTestHotkey()
    {
        var down = Input.IsKeyPressed(Key.F8);
        if (down && !_testKeyWasDown && SynthEngine.Instance is { } engine)
            SoundTest.Play(engine);
        _testKeyWasDown = down;
    }
}
