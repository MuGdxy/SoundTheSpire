using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using SoundTheSpire.Hot.Audio;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// Encounter identity uses authored voice assets. Dynamic Buff/Debuff changes are complete localized text sent to the
/// player's screen reader (with SAPI fallback), never an audio-asset matrix.
/// </summary>
public static class EncounterAnnouncer
{
    private static readonly HashSet<Creature> Seen = new();
    private static readonly HashSet<Creature> SubscribedCreatures = new();
    private static readonly Queue<string> PendingSpeech = new();

    private static VoiceManifest _voices = VoiceManifest.Empty;
    private static CombatState? _combat;
    private static bool _hasAnnouncedEncounter;
    private static bool _localeSubscribed;

    public static string Status =>
        _voices.IsLoaded
            ? $"locale {_voices.Locale}, queued {VoicePlayback.QueuedClips}, playing {VoicePlayback.IsBusy}, reader {AccessibilitySpeech.DriverName}"
            : "no voice manifest loaded";

    public static void Initialize()
    {
        ReloadVoices();
        TryInitializeLocalization();

        // A hot reload inside combat must not announce the existing roster as reinforcements.
        if (CombatReader.CurrentCombat is CombatState combat)
            TrackWithoutAnnouncing(combat);
    }

    public static void Shutdown()
    {
        if (_localeSubscribed && LocManager.Instance is { } localization)
            localization.UnsubscribeToLocaleChange(OnLocaleChanged);
        _localeSubscribed = false;
        UnsubscribeAllCreatures();
        _combat = null;
        _hasAnnouncedEncounter = false;
        PendingSpeech.Clear();
        Seen.Clear();
        VoicePlayback.Shutdown();
    }

    /// <summary>Subscribe before BeforeCombatStart hooks can apply their opening statuses.</summary>
    public static void OnCombatSetUp(CombatState combat)
    {
        UnsubscribeAllCreatures();
        _combat = combat;
        _hasAnnouncedEncounter = false;
        PendingSpeech.Clear();
        SyncCreatureSubscriptions(combat);
    }

    public static void OnCombatBegan(CombatState combat)
    {
        _combat = combat;
        Seen.Clear();
        var enemies = LivingEnemies(combat);
        Seen.UnionWith(enemies);
        SyncCreatureSubscriptions(combat);
        Announce(combat, enemies, reinforcements: false);
        _hasAnnouncedEncounter = true;
    }

    public static void OnCreaturesChanged(CombatState combat)
    {
        if (!ReferenceEquals(_combat, combat) || !CombatManager.Instance.IsInProgress)
            return;

        SyncCreatureSubscriptions(combat);
        var added = LivingEnemies(combat).Where(Seen.Add).ToList();
        if (added.Count > 0)
            Announce(combat, added, reinforcements: true);
    }

    public static void OnCombatEnded(CombatRoom room)
    {
        if (!ReferenceEquals(_combat, room.CombatState))
            return;
        UnsubscribeAllCreatures();
        _combat = null;
        _hasAnnouncedEncounter = false;
        PendingSpeech.Clear();
        Seen.Clear();
        VoicePlayback.Stop();
    }

    /// <summary>Replays the current encounter identity, primarily for asset authoring and diagnostics.</summary>
    public static bool Replay(out string report)
    {
        if (CombatReader.CurrentCombat is not CombatState combat)
        {
            report = "Not in combat.";
            return false;
        }
        var enemies = LivingEnemies(combat);
        var queued = Announce(combat, enemies, reinforcements: false);
        report = queued > 0
            ? $"Queued {queued} encounter voice clips ({Status})."
            : $"No playable voice assets for encounter {combat.Encounter?.Id.Entry ?? "unknown"} ({Status}).";
        return queued > 0;
    }

    /// <summary>Wait until authored encounter speech ends, then hand queued status text to the screen reader.</summary>
    public static void Poll()
    {
        TryInitializeLocalization();
        if (!_hasAnnouncedEncounter || VoicePlayback.IsBusy)
            return;
        while (PendingSpeech.TryDequeue(out var text))
            AccessibilitySpeech.Output(text);
    }

    private static void TryInitializeLocalization()
    {
        if (_localeSubscribed || LocManager.Instance is not { } localization)
            return;
        localization.SubscribeToLocaleChange(OnLocaleChanged);
        _localeSubscribed = true;
        ReloadVoices();
    }

    private static void OnLocaleChanged()
    {
        VoicePlayback.Stop();
        PendingSpeech.Clear();
        ReloadVoices();
    }

    private static void ReloadVoices()
    {
        _voices = VoiceManifest.LoadCurrent();
        VoicePlayback.Initialize(_voices.Data.GapMs, _voices.Data.VolumeDb);
        VoicePlayback.Configure(_voices.Data.GapMs, _voices.Data.VolumeDb);
    }

    private static void TrackWithoutAnnouncing(CombatState combat)
    {
        _combat = combat;
        _hasAnnouncedEncounter = true;
        Seen.Clear();
        Seen.UnionWith(LivingEnemies(combat));
        SyncCreatureSubscriptions(combat);
    }

    private static List<Creature> LivingEnemies(CombatState combat) =>
        combat.Enemies.Where(e => e.IsAlive && e.Monster != null).ToList();

    private static void SyncCreatureSubscriptions(CombatState combat)
    {
        foreach (var creature in combat.Creatures.Where(SubscribedCreatures.Add))
        {
            creature.PowerApplied += OnPowerApplied;
            creature.PowerRemoved += OnPowerRemoved;
        }

        foreach (var creature in SubscribedCreatures.Where(c => !combat.Creatures.Contains(c)).ToList())
            UnsubscribeCreature(creature);
    }

    private static void UnsubscribeAllCreatures()
    {
        foreach (var creature in SubscribedCreatures.ToList())
            UnsubscribeCreature(creature);
    }

    private static void UnsubscribeCreature(Creature creature)
    {
        creature.PowerApplied -= OnPowerApplied;
        creature.PowerRemoved -= OnPowerRemoved;
        SubscribedCreatures.Remove(creature);
    }

    private static void OnPowerApplied(PowerModel power)
    {
        if (_combat == null || !ReferenceEquals(power.Owner.CombatState, _combat) || !power.IsVisible)
            return;
        var type = power.TypeForCurrentAmount;
        if (type is PowerType.Buff or PowerType.Debuff)
            AnnounceStatus(power, type, ended: false);
    }

    private static void OnPowerRemoved(PowerModel power)
    {
        if (_combat == null || !ReferenceEquals(power.Owner.CombatState, _combat) || !power.Owner.IsAlive || !power.IsVisible)
            return;
        var type = power.TypeForCurrentAmount;
        if (type is PowerType.Buff or PowerType.Debuff)
            AnnounceStatus(power, type, ended: true);
    }

    private static void AnnounceStatus(PowerModel power, PowerType type, bool ended)
    {
        var target = LocalContext.IsMe(power.Owner) ? "你" : power.Owner.Name;
        var status = power.Title.GetFormattedText();
        var text = ended
            ? $"{target}的{status}结束"
            : $"{target}{(type == PowerType.Buff ? "获得" : "受到")}{status}";
        PendingSpeech.Enqueue(text);
        MainFile.Logger.Info($"Status text queued: {text}");
    }

    private static int Announce(CombatState combat, IReadOnlyList<Creature> enemies, bool reinforcements)
    {
        if (!_voices.IsLoaded || enemies.Count == 0)
            return 0;

        if (!reinforcements &&
            combat.Encounter is { RoomType: RoomType.Boss or RoomType.Elite } encounter &&
            TryGet(_voices.Data.Encounters, encounter.Id.Entry) is { } special &&
            _voices.Resolve(special) is { } specialPath)
        {
            VoicePlayback.Enqueue(new[] { specialPath });
            MainFile.Logger.Info($"Encounter announced with authored line: {encounter.Id.Entry} ({special.Text})");
            return 1;
        }

        if (!reinforcements &&
            TryGet(_voices.Data.Rosters, RosterKey(enemies)) is { } rosterLine &&
            _voices.Resolve(rosterLine) is { } rosterPath)
        {
            VoicePlayback.Enqueue(new[] { rosterPath });
            MainFile.Logger.Info($"Encounter announced with roster line: {rosterLine.Text}");
            return 1;
        }

        var fallback = RosterText(enemies, reinforcements ? "增援" : "敌人来袭");
        PendingSpeech.Enqueue(fallback);
        MainFile.Logger.Info($"Encounter text fallback queued: {fallback}");
        return 1;
    }

    private static VoiceLineData? TryGet(Dictionary<string, VoiceLineData> lines, string id) =>
        lines.TryGetValue(id, out var exact)
            ? exact
            : lines.FirstOrDefault(pair => pair.Key.Equals(id, StringComparison.OrdinalIgnoreCase)).Value;

    private static string RosterKey(IEnumerable<Creature> enemies) =>
        string.Join("+", enemies.Select(enemy => enemy.ModelId.Entry).Order(StringComparer.Ordinal));

    private static string RosterText(IEnumerable<Creature> enemies, string prefix)
    {
        var labels = enemies
            .GroupBy(enemy => enemy.ModelId.Entry)
            .Select(group =>
            {
                var line = TryGet(_voices.Data.Monsters, group.Key);
                var name = string.IsNullOrWhiteSpace(line?.MatrixText) ? group.First().Name : line.MatrixText;
                return group.Count() == 1 ? name : $"{name}{group.Count()}只";
            })
            .ToList();
        return $"{prefix}。{string.Join("、", labels)}。";
    }

}
