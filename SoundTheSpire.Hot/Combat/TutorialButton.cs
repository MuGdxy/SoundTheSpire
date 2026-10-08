using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using SoundTheSpire.Hot.Commands;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// On-screen entry to the listening tutorial, shown on the bare main menu and in a run outside combat. From the menu it
/// starts an unsaved Ironclad run and enters the tutorial once the run is ready; in a run it does what F10 does.
/// </summary>
public static class TutorialButton
{
    private const string Text = "声音教学关 (F10)";
    private const string Seed = "LISTEN";

    private static CanvasLayer? _layer;
    private static Button? _button;
    private static bool _startAfterRun;

    public static void Poll()
    {
        if (_startAfterRun && StsRunConsoleCmd.IsRunReady && !CombatManager.Instance.IsInProgress)
        {
            _startAfterRun = false;
            ListeningTutorial.Request();
        }

        var visible = !_startAfterRun && (OnBareMainMenu || RunManager.Instance.IsInProgress && !CombatManager.Instance.IsInProgress);
        if (visible && _button == null)
            Create();
        if (_button != null && GodotObject.IsInstanceValid(_button))
            _button.Visible = visible;
    }

    public static void Remove()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer))
            _layer.QueueFree();
        _layer = null;
        _button = null;
        _startAfterRun = false;
    }

    private static bool OnBareMainMenu =>
        !RunManager.Instance.IsInProgress && NGame.Instance?.MainMenu is { } menu && !menu.SubmenuStack.SubmenusOpen;

    private static void Create()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;
        _layer = new CanvasLayer { Layer = 50 };
        _button = new Button
        {
            Text = Text,
            FocusMode = Control.FocusModeEnum.None,
            AnchorLeft = 0, AnchorRight = 0, AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = 24, OffsetTop = -84, OffsetRight = 264, OffsetBottom = -32,
        };
        _button.AddThemeFontSizeOverride("font_size", 24);
        _button.Pressed += OnPressed;
        _layer.AddChild(_button);
        tree.Root.AddChildSafely(_layer);
    }

    private static void OnPressed()
    {
        if (RunManager.Instance.IsInProgress)
        {
            ListeningTutorial.Request();
            return;
        }
        var result = StsRunConsoleCmd.Start("ironclad", Seed);
        if (!result.success)
        {
            MainFile.Logger.Warn($"Tutorial button: {result.msg}");
            return;
        }
        if (result.task != null)
            TaskHelper.RunSafely(result.task);
        _startAfterRun = true;
    }
}
