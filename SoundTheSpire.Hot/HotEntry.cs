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
        context.OnFrame(VoicePlayback.Poll);
        context.OnFrame(EncounterAnnouncer.Poll);
        context.OnFrame(CardAnnouncer.Poll);
        context.OnFrame(IntentAnnouncer.PollReplayKey);
        context.OnFrame(DefenseMonitor.Poll);
        context.OnFrame(IntentVeil.Poll);
        context.OnFrame(NumberVeil.Poll);
        context.OnFrame(ListeningTutorial.Poll);
        context.OnFrame(TutorialButton.Poll);
        AccessibilitySpeech.Initialize();
        EncounterAnnouncer.Initialize();
        CardAnnouncer.Initialize();
        NumberVeil.Refresh();
        CombatManager.Instance.CombatSetUp += EncounterAnnouncer.OnCombatSetUp;
        CombatManager.Instance.CombatBegan += EncounterAnnouncer.OnCombatBegan;
        CombatManager.Instance.CreaturesChanged += EncounterAnnouncer.OnCreaturesChanged;
        CombatManager.Instance.CombatEnded += EncounterAnnouncer.OnCombatEnded;
        CombatManager.Instance.TurnStarted += IntentAnnouncer.OnTurnStarted;
        CombatManager.Instance.TurnStarted += ListeningTutorial.OnTurnStarted;
        CombatManager.Instance.CombatWon += ListeningTutorial.OnCombatWon;
        CombatManager.Instance.CombatEnded += ListeningTutorial.OnCombatEnded;
        if (CombatReader.CurrentCombat is { CurrentSide: CombatSide.Player } combat)
            DefenseMonitor.StartWatching(combat);
    }

    public void Unload()
    {
        CombatManager.Instance.CombatSetUp -= EncounterAnnouncer.OnCombatSetUp;
        CombatManager.Instance.CombatBegan -= EncounterAnnouncer.OnCombatBegan;
        CombatManager.Instance.CreaturesChanged -= EncounterAnnouncer.OnCreaturesChanged;
        CombatManager.Instance.CombatEnded -= EncounterAnnouncer.OnCombatEnded;
        CombatManager.Instance.TurnStarted -= IntentAnnouncer.OnTurnStarted;
        CombatManager.Instance.TurnStarted -= ListeningTutorial.OnTurnStarted;
        CombatManager.Instance.CombatWon -= ListeningTutorial.OnCombatWon;
        CombatManager.Instance.CombatEnded -= ListeningTutorial.OnCombatEnded;
        CardAnnouncer.Shutdown();
        EncounterAnnouncer.Shutdown();
        AccessibilitySpeech.Shutdown();
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
