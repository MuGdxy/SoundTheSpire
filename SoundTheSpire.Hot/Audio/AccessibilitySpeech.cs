using System.Runtime.InteropServices;

namespace SoundTheSpire.Hot.Audio;

/// <summary>
/// Sends dynamic combat text to the player's active screen reader and braille display through Tolk.
/// Windows SAPI fallback is deliberately disabled; without an active screen reader, dynamic text stays silent.
/// </summary>
public static class AccessibilitySpeech
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidCall();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BoolArgument([MarshalAs(UnmanagedType.I1)] bool value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint DetectCall();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool OutputCall(
        [MarshalAs(UnmanagedType.LPWStr)] string text,
        [MarshalAs(UnmanagedType.I1)] bool interrupt);

    private static nint _library;
    private static VoidCall? _unload;
    private static OutputCall? _output;

    public static string DriverName { get; private set; } = "none";
    public static bool IsAvailable => _output != null;

    public static void Initialize()
    {
        if (_library != 0 || !OperatingSystem.IsWindows())
            return;

        var path = Path.Combine(MainFile.ModDirectory, "Tolk.dll");
        if (!File.Exists(path))
        {
            MainFile.Logger.Warn($"Screen reader bridge not found: {path}");
            return;
        }

        try
        {
            _library = NativeLibrary.Load(path);
            Get<BoolArgument>("Tolk_TrySAPI")(false);
            Get<BoolArgument>("Tolk_PreferSAPI")(false);
            Get<VoidCall>("Tolk_Load")();
            _unload = Get<VoidCall>("Tolk_Unload");
            _output = Get<OutputCall>("Tolk_Output");
            var name = Get<DetectCall>("Tolk_DetectScreenReader")();
            DriverName = name == 0 ? "none" : Marshal.PtrToStringUni(name) ?? "unknown";
            MainFile.Logger.Info($"Accessibility speech ready: {DriverName}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Screen reader bridge failed: {e.Message}");
            Shutdown();
        }
    }

    public static bool Output(string text, bool interrupt = false)
    {
        if (string.IsNullOrWhiteSpace(text) || _output == null)
            return false;
        try
        {
            var success = _output(text, interrupt);
            MainFile.Logger.Info($"Accessibility speech ({DriverName}): {text}");
            return success;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Accessibility speech failed: {e.Message}");
            return false;
        }
    }

    public static void Shutdown()
    {
        try
        {
            _unload?.Invoke();
        }
        catch
        {
            // The game is shutting down or the native driver already unloaded.
        }
        _unload = null;
        _output = null;
        DriverName = "none";
        if (_library != 0)
        {
            NativeLibrary.Free(_library);
            _library = 0;
        }
    }

    private static T Get<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
}
