using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// Hides the enemies' intent icons so the intents have to be told apart by ear. The game only fades intents through
/// their container's modulate and hides the container itself when a creature dies, so the veil owns
/// <see cref="CanvasItem.Visible"/> and gives back only the containers it hid, for creatures still alive.
/// </summary>
public static class IntentVeil
{
    public const Key ToggleKey = Key.F9;

    public static bool Enabled { get; private set; } = true;

    private static readonly List<(Control Container, MegaCrit.Sts2.Core.Entities.Creatures.Creature Creature)> Hidden = new();
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
}
