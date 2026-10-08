#if DEBUG
using System.Net;
using System.Net.Sockets;
using System.Text;
using GameDevConsole = MegaCrit.Sts2.Core.DevConsole.DevConsole;

namespace SoundTheSpire.SoundTheSpireCode.Core;

/// <summary>
/// Debug builds only: accepts one dev-console command per connection on localhost and replies with the result,
/// so scenario scripts can drive the game from outside.
/// </summary>
internal static class DebugBridge
{
    public const int Port = 47800;

    private static GameDevConsole? _console;

    public static void Start()
    {
        new Thread(Listen) { IsBackground = true, Name = "SoundTheSpire.DebugBridge" }.Start();
    }

    private static void Listen()
    {
        var listener = new TcpListener(IPAddress.Loopback, Port);
        try
        {
            listener.Start();
        }
        catch (SocketException e)
        {
            MainFile.Logger.Error($"Debug bridge could not listen on {Port}: {e.Message}");
            return;
        }
        MainFile.Logger.Info($"Debug bridge listening on 127.0.0.1:{Port}");

        while (true)
        {
            try
            {
                using var client = listener.AcceptTcpClient();
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var reply = MainThread.Invoke(() => Execute(line));
                writer.Write(reply.Wait(TimeSpan.FromSeconds(10)) ? reply.Result : "fail: timed out waiting for main thread");
            }
            catch (Exception e)
            {
                MainFile.Logger.Error($"Debug bridge request failed: {e}");
            }
        }
    }

    private static string Execute(string command)
    {
        try
        {
            _console ??= new GameDevConsole(shouldAllowDebugCommands: true);
            var result = _console.ProcessCommand(command);
            return (result.success ? "ok: " : "fail: ") + result.msg;
        }
        catch (Exception e)
        {
            return "fail: " + e;
        }
    }
}
#endif
