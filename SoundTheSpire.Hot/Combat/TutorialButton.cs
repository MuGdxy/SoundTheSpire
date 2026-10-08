using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Runs;
using SoundTheSpire.Hot.Commands;

namespace SoundTheSpire.Hot.Combat;

/// <summary>
/// Native main-menu entry for a dedicated unsaved tutorial run. Normal runs never expose or accept a tutorial entry.
/// </summary>
public static class TutorialButton
{
    private const string Text = "声音教学关";
    private const string Seed = "LISTEN";

    private static NMainMenuTextButton? _menuButton;
    private static bool _startAfterRun;

    public static void Poll()
    {
        EnsureMainMenuButton();

        if (_startAfterRun && StsRunConsoleCmd.IsRunReady && !CombatManager.Instance.IsInProgress)
        {
            _startAfterRun = false;
            ListeningTutorial.AllowDedicatedEntry();
            ListeningTutorial.Request();
        }
    }

    public static void Remove()
    {
        if (_menuButton != null && GodotObject.IsInstanceValid(_menuButton))
            _menuButton.QueueFree();
        _menuButton = null;
        _startAfterRun = false;
    }

    private static void EnsureMainMenuButton()
    {
        if (_menuButton != null && GodotObject.IsInstanceValid(_menuButton))
            return;
        _menuButton = null;
        if (RunManager.Instance.IsInProgress || NGame.Instance?.MainMenu is not { } menu)
            return;
        ListeningTutorial.OnMainMenuReached();

        var settings = menu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/SettingsButton");
        if (settings?.GetParent() is not { } parent)
            return;
        var flags = (int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts | Node.DuplicateFlags.UseInstantiation);
        _menuButton = (NMainMenuTextButton)settings.Duplicate(flags);
        _menuButton.Name = "SoundTheSpireTutorialButton";
        parent.AddChild(_menuButton);
        parent.MoveChild(_menuButton, settings.GetIndex());
        if (_menuButton.label != null)
            _menuButton.label.Text = Text;
        _menuButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OnPressed()));
    }

    private static void OnPressed()
    {
        var result = StartFromMainMenu();
        if (result.task != null)
            TaskHelper.RunSafely(result.task);
        if (!result.success)
            MainFile.Logger.Warn($"Tutorial button: {result.msg}");
    }

    /// <summary>Starts the dedicated unsaved tutorial run. Rejected anywhere except the main menu.</summary>
    public static CmdResult StartFromMainMenu()
    {
        if (RunManager.Instance.IsInProgress)
            return new CmdResult(false, "The tutorial can only be started from the main menu.");
        var result = StsRunConsoleCmd.Start("ironclad", Seed, ModelDb.Act<Underdocks>());
        if (!result.success)
            return result;
        _startAfterRun = true;
        return result;
    }
}
