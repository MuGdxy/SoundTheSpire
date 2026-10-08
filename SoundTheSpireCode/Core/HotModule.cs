namespace SoundTheSpire.SoundTheSpireCode.Core;

/// <summary>Entry point of the hot-reloadable assembly. Exactly one non-abstract implementation must exist there.</summary>
public interface IHotModule
{
    void Load(HotContext context);

    void Unload()
    {
    }
}

/// <summary>Resources a hot module registers through here are released by the host when the module is replaced.</summary>
public sealed class HotContext
{
    private readonly List<Action> _frameHandlers = new();

    public void OnFrame(Action handler)
    {
        _frameHandlers.Add(handler);
        MainThread.Frame += handler;
    }

    internal void Release()
    {
        foreach (var handler in _frameHandlers)
            MainThread.Frame -= handler;
        _frameHandlers.Clear();
    }
}
