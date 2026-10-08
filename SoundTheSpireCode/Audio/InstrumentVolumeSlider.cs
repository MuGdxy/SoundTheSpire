using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace SoundTheSpire.SoundTheSpireCode.Audio;

/// <summary>Adds a dedicated Sound the Spire volume row to the game's sound settings panel.</summary>
[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen._Ready))]
public static class InstrumentVolumeSlider
{
    private const string RowName = "SoundTheSpireInstrumentVolume";

    public static void Postfix(NSettingsScreen __instance)
    {
        var content = __instance.GetNode<NSettingsPanel>("%SoundSettings").Content;
        if (content.HasNode(RowName))
            return;

        var row = new HBoxContainer
        {
            Name = RowName,
            CustomMinimumSize = new Vector2(0, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        var label = new Label
        {
            Text = "Sound the Spire 乐段音量",
            CustomMinimumSize = new Vector2(370, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var slider = new HSlider
        {
            Name = "Slider",
            MinValue = 0,
            MaxValue = InstrumentVolume.MaxValue * 100,
            Step = 5,
            Value = InstrumentVolume.Value * 100,
            CustomMinimumSize = new Vector2(420, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = "单独调整 Sound the Spire 的乐器与乐段音量；超过 100% 会放大输出",
        };
        var valueLabel = new Label
        {
            Text = $"{slider.Value:0}%",
            CustomMinimumSize = new Vector2(80, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        slider.ValueChanged += value =>
        {
            valueLabel.Text = $"{value:0}%";
            InstrumentVolume.Set((float)value / 100f);
        };

        row.AddChild(label);
        row.AddChild(slider);
        row.AddChild(valueLabel);
        content.AddChild(row);

        var musicRow = content.GetNodeOrNull<Node>("BgmVolume");
        if (musicRow != null)
            content.MoveChild(row, musicRow.GetIndex() + 1);
    }
}
