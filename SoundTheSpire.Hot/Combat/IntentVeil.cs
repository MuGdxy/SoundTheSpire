using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// Hides the enemies' intent icons and explanations so the intents have to be told apart by ear. The game only fades intents through
/// their container's modulate and hides the container itself when a creature dies, so the veil owns
/// <see cref="CanvasItem.Visible"/> and gives back only the containers it hid, for creatures still alive.
/// </summary>
public static class IntentVeil
{
    public const Key ToggleKey = Key.F9;

    public static bool Enabled { get; private set; } = true;

    private static readonly List<(Control Container, Creature Creature)> Hidden = new();
    private static bool _toggleKeyWasDown;

    public static void Poll()
    {
        var down = Input.IsKeyPressed(ToggleKey);
        if (down && !_toggleKeyWasDown)
            Set(!Enabled);
        _toggleKeyWasDown = down;
        if (Enabled)
            Apply();
    }

    /// <returns>A short report of what was found, for the console.</returns>
    public static string Apply()
    {
        if (CombatReader.CurrentCombat is not { } combat)
            return "not in combat";
        if (NCombatRoom.Instance is not { } room)
            return "no combat room";
        int nodes = 0, hidden = 0;
        foreach (var enemy in combat.Enemies)
        {
            if (!enemy.IsAlive || room.GetCreatureNode(enemy) is not { } node || !GodotObject.IsInstanceValid(node))
                continue;
            nodes++;
            var container = node.IntentContainer;
            if (container.Visible)
            {
                container.Visible = false;
                Hidden.Add((container, enemy));
                hidden++;
            }
        }
        return $"{nodes} enemy nodes, {hidden} newly hidden, {Hidden.Count} held";
    }

    public static void Set(bool enabled)
    {
        if (Enabled == enabled)
            return;
        Enabled = enabled;
        MainFile.Logger.Info($"Intent veil {(enabled ? "on" : "off")}");
        if (!enabled)
            Restore();
    }

    public static void Restore()
    {
        foreach (var (container, creature) in Hidden)
        {
            if (GodotObject.IsInstanceValid(container) && creature.IsAlive)
                container.Visible = true;
        }
        Hidden.Clear();
    }

    /// <summary>
    /// Intent explanations, from the creature's hover tips or an intent icon's own, are built by
    /// <c>AbstractIntent.GetHoverTip</c>, which the JIT inlines, so they are caught where every hover tip is shown.
    /// Intent tips are recognised by their id (title from the "intents" table). While veiled, an enemy's intent tips
    /// collapse into one, so neither the intent type (the original title) nor the number of intents shows.
    /// </summary>
    public static IEnumerable<IHoverTip> VeilTips(IEnumerable<IHoverTip> tips)
    {
        var veiled = false;
        foreach (var tip in tips)
        {
            if (tip is HoverTip { Id: { } id } && id.StartsWith(IntentTipIdPrefix, StringComparison.Ordinal))
            {
                if (!veiled)
                    yield return VeiledTip(tip);
                veiled = true;
            }
            else
            {
                yield return tip;
            }
        }
    }

    private const string IntentTipIdPrefix = "LocString with Title=intents.";
    private const string VeiledTitle = "意图";
    private const string VeiledDescription = "你需要用声音判断意图";

    private static HoverTip VeiledTip(IHoverTip original)
    {
        object tip = (HoverTip)original;
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(tip, VeiledTitle);
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(tip, VeiledDescription);
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Icon)).SetValue(tip, null);
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Id)).SetValue(tip, "SoundTheSpire.VeiledIntent");
        return (HoverTip)tip;
    }

    private static bool BelongsToEnemy(Node? node)
    {
        for (; node != null; node = node.GetParent())
        {
            if (node is NCreature creature)
                return creature.Entity.IsEnemy;
        }
        return false;
    }

    [HarmonyPatch(typeof(NHoverTipSet), nameof(NHoverTipSet.CreateAndShow),
        new[] { typeof(Control), typeof(IEnumerable<IHoverTip>), typeof(HoverTipAlignment) })]
    private static class ShowTipsPatch
    {
        private static void Prefix(Control owner, ref IEnumerable<IHoverTip> hoverTips)
        {
            if (Enabled && BelongsToEnemy(owner))
                hoverTips = LastShown = VeilTips(hoverTips).ToList();
        }
    }

    /// <summary>The tips most recently shown for an enemy while veiled, for <c>sts_tips</c>.</summary>
    public static IReadOnlyList<IHoverTip>? LastShown { get; private set; }

    public static void ClearLastShown() => LastShown = null;
}
