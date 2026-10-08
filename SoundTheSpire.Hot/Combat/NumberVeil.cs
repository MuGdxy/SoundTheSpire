using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.sts2.Core.Nodes.TopBar;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// Part of the veil (<see cref="IntentVeil.Enabled"/>): hides the numbers of the health bars (HP and block amount;
/// the bar itself and the block shield stay), the floating damage and heal numbers, and the top bar HP during combat.
/// The health bar re-sets its labels on every refresh, so the patches hide them again right after; on toggling,
/// <see cref="Refresh"/> makes the bars redraw.
/// </summary>
public static class NumberVeil
{
    private static readonly AccessTools.FieldRef<NHealthBar, MegaLabel> HpLabel =
        AccessTools.FieldRefAccess<NHealthBar, MegaLabel>("_hpLabel");
    private static readonly AccessTools.FieldRef<NHealthBar, MegaLabel> BlockLabel =
        AccessTools.FieldRefAccess<NHealthBar, MegaLabel>("_blockLabel");
    private static readonly AccessTools.FieldRef<NTopBarHp, MegaLabel> TopBarLabel =
        AccessTools.FieldRefAccess<NTopBarHp, MegaLabel>("_hpLabel");

    private static readonly HashSet<NHealthBar> Bars = new();
    private static NTopBarHp? _topBar;

    public static void Poll()
    {
        if (_topBar is { } topBar && GodotObject.IsInstanceValid(topBar))
            TopBarLabel(topBar).Visible = !(IntentVeil.Enabled && CombatReader.CurrentCombat != null);
    }

    /// <summary>
    /// Finds the bars and top bar already in the scene (they predate a hot reload) and redraws every health bar, so a
    /// toggle shows at once.
    /// </summary>
    public static void Refresh()
    {
        if (Engine.GetMainLoop() is SceneTree tree)
            Scan(tree.Root);
        Bars.RemoveWhere(bar => !GodotObject.IsInstanceValid(bar));
        foreach (var bar in Bars)
            bar.RefreshValues();
        Poll();
    }

    /// <returns>Which number labels are showing, for the console.</returns>
    public static string Report()
    {
        Bars.RemoveWhere(bar => !GodotObject.IsInstanceValid(bar));
        var hp = Bars.Count(bar => HpLabel(bar).Visible);
        var block = Bars.Count(bar => BlockLabel(bar).Visible);
        var top = _topBar is { } topBar && GodotObject.IsInstanceValid(topBar) ? TopBarLabel(topBar).Visible.ToString() : "none";
        return $"{Bars.Count} health bars, hp numbers showing {hp}, block numbers showing {block}, top bar hp showing {top}";
    }

    private static void Scan(Node node)
    {
        switch (node)
        {
            case NHealthBar bar:
                Bars.Add(bar);
                break;
            case NTopBarHp topBar:
                _topBar = topBar;
                break;
        }
        foreach (var child in node.GetChildren())
            Scan(child);
    }

    [HarmonyPatch(typeof(NHealthBar), "RefreshText")]
    private static class HpTextPatch
    {
        private static void Postfix(NHealthBar __instance)
        {
            Bars.Add(__instance);
            if (IntentVeil.Enabled)
                HpLabel(__instance).Visible = false;
        }
    }

    [HarmonyPatch(typeof(NHealthBar), "RefreshBlockUi")]
    private static class BlockTextPatch
    {
        private static void Postfix(NHealthBar __instance) => BlockLabel(__instance).Visible = !IntentVeil.Enabled;
    }

    [HarmonyPatch(typeof(NTopBarHp), nameof(NTopBarHp._Ready))]
    private static class TopBarPatch
    {
        private static void Postfix(NTopBarHp __instance) => _topBar = __instance;
    }

    [HarmonyPatch(typeof(NDamageNumVfx), nameof(NDamageNumVfx._Ready))]
    private static class DamageNumberPatch
    {
        private static void Postfix(NDamageNumVfx __instance) => __instance.Visible = !IntentVeil.Enabled;
    }

    [HarmonyPatch(typeof(NHealNumVfx), nameof(NHealNumVfx._Ready))]
    private static class HealNumberPatch
    {
        private static void Postfix(NHealNumVfx __instance) => __instance.Visible = !IntentVeil.Enabled;
    }
}
