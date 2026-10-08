using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using SoundTheSpire.Hot.Combat;

namespace SoundTheSpire.Hot.Audio;

/// <summary>Musical status begin/end cues projected onto the song chord at their quantized playback beat.</summary>
public static class StatusMotif
{
    private const int Channel = 11;

    private static void Play(PowerModel power, bool applied)
    {
        if (SynthEngine.Instance is not { } engine ||
            MusicClock.ActiveProfile is not { StatusChange: { } config } profile ||
            !power.IsVisible ||
            !CombatManager.Instance.IsInProgress ||
            CombatManager.Instance.IsEnding)
            return;

        var type = power.TypeForCurrentAmount;
        if (type is not (PowerType.Buff or PowerType.Debuff))
            return;

        var at = MusicClock.DelayToNextSubdivision(Math.Max(1, config.SubdivisionsPerBeat));
        var chord = HarmonyTimeline.ChordAt(at, out var bar);
        if (chord == null)
            return;

        var anchor = profile.BuffNotes.FirstOrDefault(60);
        var keys = IntentVoicing.ChordKeys(chord, anchor);
        var ascending = (type == PowerType.Buff) == applied;
        if (!ascending)
            Array.Reverse(keys);

        var phrase = new Phrase();
        var step = MusicClock.BeatSeconds * config.StepBeats;
        var duration = MusicClock.BeatSeconds * config.NoteDurationBeats;
        var velocity = applied ? config.ApplyVelocity : config.RemoveVelocity;
        for (var i = 0; i < keys.Length; i++)
            phrase.Note(i * step, duration, velocity, keys[i]);
        phrase.ApplyGainDb(HarmonyTimeline.LoudnessGainDbAt(at, out _));

        var owner = power.Owner;
        var pan = Pan(owner);
        var program = type == PowerType.Buff ? config.BuffProgram : config.DebuffProgram;
        engine.Schedule(at, synth =>
        {
            synth.SetProgram(Channel, program);
            synth.SetPan(Channel, pan);
        });
        phrase.ScheduleOn(engine, at, Channel);
        MainFile.Logger.Info(
            $"Status {(applied ? "applied" : "removed")}: {type}, harmony bar {bar}, {chord.Name}.");
    }

    private static int Pan(Creature owner)
    {
        if (owner.IsPlayer)
            return 28;
        if (owner.CombatState is { } combat &&
            CombatReader.ReadEnemies(combat).FirstOrDefault(enemy => ReferenceEquals(enemy.Creature, owner)) is { } info)
            return (int)Math.Round(16 + info.ScreenX * 95);
        return 64;
    }

    [HarmonyPatch(typeof(Creature), nameof(Creature.ApplyPowerInternal))]
    private static class ApplyPatch
    {
        private static void Postfix(PowerModel power) => Play(power, applied: true);
    }

    [HarmonyPatch(typeof(Creature), nameof(Creature.RemovePowerInternal))]
    private static class RemovePatch
    {
        private static void Postfix(PowerModel power) => Play(power, applied: false);
    }
}
