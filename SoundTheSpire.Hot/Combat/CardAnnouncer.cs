using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using SoundTheSpire.Hot.Audio;

namespace SoundTheSpire.Hot.Combat;

/// <summary>Reads the localized card name whenever a card holder gains mouse, keyboard or controller focus.</summary>
public static class CardAnnouncer
{
    private const ulong DebounceMs = 250;
    private const string ConfigPath = "user://sound_the_spire_card_voice.cfg";
    private const string ConfigSection = "accessibility";
    private const string ConfigKey = "card_name_announcements";
    private const string SettingsRowName = "SoundTheSpireCardAnnouncements";

    private static VoiceManifest _voices = VoiceManifest.Empty;
    private static AudioStreamPlayer? _player;
    private static string _lastCardId = "";
    private static ulong _lastTicks;
    private static bool _localeSubscribed;

    public static bool Enabled { get; private set; } = true;

    public static void Initialize()
    {
        LoadSetting();
        _voices = VoiceManifest.LoadCurrent();
        TryInitializeLocalization();
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;
        _player = new AudioStreamPlayer
        {
            Name = "SoundTheSpireCardVoice",
            VolumeDb = _voices.Data.VolumeDb,
        };
        tree.Root.AddChild(_player);
    }

    public static void Shutdown()
    {
        if (_localeSubscribed && LocManager.Instance is { } localization)
            localization.UnsubscribeToLocaleChange(OnLocaleChanged);
        _localeSubscribed = false;
        if (_player != null && GodotObject.IsInstanceValid(_player))
            _player.QueueFree();
        _player = null;
        _lastCardId = "";
        _lastTicks = 0;
    }

    public static void Poll() => TryInitializeLocalization();

    private static void TryInitializeLocalization()
    {
        if (_localeSubscribed || LocManager.Instance is not { } localization)
            return;
        localization.SubscribeToLocaleChange(OnLocaleChanged);
        _localeSubscribed = true;
        OnLocaleChanged();
    }

    public static bool Play(CardModel card)
    {
        if (!Enabled)
            return false;
        var id = card.Id.Entry;
        var now = Time.GetTicksMsec();
        if (id == _lastCardId && now - _lastTicks < DebounceMs)
            return false;
        _lastCardId = id;
        _lastTicks = now;

        var line = TryGet(_voices.Data.Cards, id);
        var path = _voices.Resolve(line);
        if (path == null || _player == null || !GodotObject.IsInstanceValid(_player))
            return AccessibilitySpeech.Output(card.Title, interrupt: true);

        try
        {
            var stream = AudioStreamOggVorbis.LoadFromFile(path);
            if (stream == null)
                return AccessibilitySpeech.Output(card.Title, interrupt: true);
            stream.Loop = false;
            _player.Stop();
            _player.Stream = stream;
            _player.Play();
            MainFile.Logger.Info($"Card announced: {card.Id.Entry} {card.Title}");
            return true;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Card voice failed ({path}): {e.Message}");
            return AccessibilitySpeech.Output(card.Title, interrupt: true);
        }
    }

    private static void OnLocaleChanged()
    {
        _player?.Stop();
        _voices = VoiceManifest.LoadCurrent();
        if (_player != null && GodotObject.IsInstanceValid(_player))
            _player.VolumeDb = _voices.Data.VolumeDb;
        _lastCardId = "";
    }

    public static void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
            _player?.Stop();
        var config = new ConfigFile();
        config.Load(ConfigPath);
        config.SetValue(ConfigSection, ConfigKey, enabled);
        var error = config.Save(ConfigPath);
        if (error != Error.Ok)
            MainFile.Logger.Warn($"Could not save card announcement setting: {error}");
        MainFile.Logger.Info($"Card name announcements {(enabled ? "on" : "off")}");
    }

    private static void LoadSetting()
    {
        var config = new ConfigFile();
        if (config.Load(ConfigPath) == Error.Ok)
            Enabled = config.GetValue(ConfigSection, ConfigKey, true).AsBool();
    }

    private static VoiceLineData? TryGet(Dictionary<string, VoiceLineData> lines, string id) =>
        lines.TryGetValue(id, out var exact)
            ? exact
            : lines.FirstOrDefault(pair => pair.Key.Equals(id, StringComparison.OrdinalIgnoreCase)).Value;

    [HarmonyPatch(typeof(NCardHolder), "OnFocus")]
    private static class CardFocusPatch
    {
        private static void Postfix(NCardHolder __instance)
        {
            if (__instance.CardModel is { } card)
                Play(card);
        }
    }

    [HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen._Ready))]
    private static class SettingsPatch
    {
        private static void Postfix(NSettingsScreen __instance)
        {
            var content = __instance.GetNode<NSettingsPanel>("%SoundSettings").Content;
            if (content.HasNode(SettingsRowName))
                return;

            var row = new HBoxContainer
            {
                Name = SettingsRowName,
                CustomMinimumSize = new Vector2(0, 64),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            var label = new Label
            {
                Text = "Sound the Spire 卡牌名称播报",
                CustomMinimumSize = new Vector2(370, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            var toggle = new CheckButton
            {
                Name = "Toggle",
                Text = Enabled ? "开启" : "关闭",
                ButtonPressed = Enabled,
                CustomMinimumSize = new Vector2(180, 0),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                TooltipText = "选中卡牌时播放本地化卡名；关闭后仍保留其他战场声音",
            };
            toggle.Toggled += enabled =>
            {
                toggle.Text = enabled ? "开启" : "关闭";
                SetEnabled(enabled);
            };

            row.AddChild(label);
            row.AddChild(toggle);
            content.AddChild(row);

            var volumeRow = content.GetNodeOrNull<Node>("SoundTheSpireInstrumentVolume");
            var musicRow = content.GetNodeOrNull<Node>("BgmVolume");
            var anchor = volumeRow ?? musicRow;
            if (anchor != null)
                content.MoveChild(row, anchor.GetIndex() + 1);
        }
    }
}
