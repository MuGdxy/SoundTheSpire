namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// Projects semantic thickness (note, fifth, chord, chord+bass) onto a song chord.
/// It does not know FMOD time or song sections; callers supply the chord returned by <see cref="HarmonyTimeline"/>.
/// </summary>
public static class IntentVoicing
{
    public readonly record struct AttackVoicing(
        int[] Lead,
        int[] Backing,
        int BackingVelocity);

    public static AttackVoicing Attack(
        MusicProfileData? profile,
        int tier,
        MusicChordChangeData? songChord,
        int? melodyNote,
        int[] defaultRoots)
    {
        var anchor = (profile?.AttackRoots ?? defaultRoots)[tier - 1];
        var leadPitchClass = profile?.IntentPitchMode switch
        {
            IntentPitchMode.Melody when melodyNote.HasValue => Mod(melodyNote.Value, 12),
            IntentPitchMode.ChordRoot => songChord?.Root,
            IntentPitchMode.Tonic => profile.TonicPitchClass,
            _ => songChord?.Root,
        };
        var root = leadPitchClass.HasValue ? NearestPitch(anchor, leadPitchClass.Value) : anchor;
        if (profile?.AttackBackingVelocities.Length == 5)
        {
            var backingVelocity = profile.AttackBackingVelocities[tier - 1];
            var backing = backingVelocity <= 0
                ? []
                : songChord != null
                    ? ChordKeys(songChord, root - 12)
                    : StaticChord(profile, tier, root - 12);
            return new AttackVoicing([root], backing, backingVelocity);
        }

        if (songChord == null)
        {
            var third = profile?.MinorThirdTiers.Contains(tier) == true ? 3 : 4;
            int[] keys = tier switch
            {
                1 => [anchor],
                2 => [anchor, anchor + 7],
                3 => [anchor, anchor + third, anchor + 7],
                _ => [anchor - 12, anchor, anchor + third, anchor + 7],
            };
            return new AttackVoicing(keys, [], 0);
        }

        if (profile?.IntentPitchMode is IntentPitchMode.Tonic or IntentPitchMode.ChordRoot)
            return new AttackVoicing([root], [], 0);
        var chord = ChordKeys(songChord, root);
        int[] lead = tier switch
        {
            1 => [root],
            2 => [root, root + Fifth(songChord)],
            3 => chord,
            _ => [root - 12, .. chord],
        };
        return new AttackVoicing(lead, [], 0);
    }

    public static int[] ChordKeys(MusicChordChangeData chord, int anchor)
    {
        var root = NearestPitch(anchor, chord.Root);
        var keys = chord.Intervals.Distinct().Order().Select(interval => root + interval).ToList();
        if (keys.Count == 1)
            keys.Add(root + 7);
        if (keys.Count == 2)
            keys.Add(root + 12);
        return keys.ToArray();
    }

    public static int[] TransposeFromTonic(int[] keys, MusicProfileData profile, MusicChordChangeData? chord)
    {
        if (chord == null)
            return keys;
        var delta = SignedPitchClassDistance(profile.TonicPitchClass, chord.Root);
        return keys.Select(key => key + delta).ToArray();
    }

    private static int Fifth(MusicChordChangeData chord) =>
        chord.Intervals.Contains(7) ? 7 : chord.Intervals.Where(interval => interval > 0).DefaultIfEmpty(7).Max();

    private static int[] StaticChord(MusicProfileData profile, int tier, int root)
    {
        var third = profile.MinorThirdTiers.Contains(tier) ? 3 : 4;
        return [root, root + third, root + 7];
    }

    private static int NearestPitch(int anchor, int pitchClass) =>
        anchor + SignedPitchClassDistance(Mod(anchor, 12), pitchClass);

    private static int SignedPitchClassDistance(int from, int to)
    {
        var distance = Mod(to - from, 12);
        return distance > 6 ? distance - 12 : distance;
    }

    private static int Mod(int value, int divisor) => (value % divisor + divisor) % divisor;
}
