using System.Collections.Concurrent;
using Godot;

namespace SoundTheSpire.SoundTheSpireCode.Core;

/// <summary>
/// Runs work on Godot's main thread once per frame. Game state, Godot nodes and the synthesizer
/// must only be touched from the main thread.
/// </summary>
public static class MainThread
{
    private static readonly ConcurrentQueue<Action> Pending = new();

    public static event Action? Frame;

    public static void Install(SceneTree tree)
    {
        tree.ProcessFrame += OnProcessFrame;
    }

    public static Task<T> Invoke<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending.Enqueue(() =>
        {
            try
            {
                tcs.SetResult(work());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });
        return tcs.Task;
    }

    private static void OnProcessFrame()
    {
        while (Pending.TryDequeue(out var work))
            work();

        if (Frame == null)
            return;

        foreach (var handler in Frame.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception e)
            {
                MainFile.Logger.Error($"Frame handler {handler.Method.Name} failed: {e}");
            }
        }
    }
}
