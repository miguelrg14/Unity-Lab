using System.Net;
using System.Runtime.InteropServices;
using UnityLab.Racing.Protocol;
using UnityLab.RaceServer;

// Usage: RaceServer [--port 7777] [--max-players 4] [--bind 0.0.0.0]
// RACE_SERVER_PORT and RACE_SERVER_MAX_PLAYERS environment variables are also honoured (handy in Docker).

int port = ReadInt("--port", "RACE_SERVER_PORT", ProtocolConstants.DefaultPort);
int maxPlayers = ReadInt("--max-players", "RACE_SERVER_MAX_PLAYERS", 4);
string bind = ReadArg("--bind") ?? "0.0.0.0";

if (port is < 1 or > 65535 || maxPlayers is < 1 or > 64 || !IPAddress.TryParse(bind, out IPAddress? address))
{
    Console.Error.WriteLine("Invalid arguments. Usage: RaceServer [--port 1-65535] [--max-players 1-64] [--bind IP]");
    return 2;
}

// Ctrl+C and `docker stop` (SIGTERM) both shut the server down cleanly.
using var cancel = new CancellationTokenSource();
void Stop(PosixSignalContext context)
{
    context.Cancel = true;
    cancel.Cancel();
}
using PosixSignalRegistration sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
using PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);

var server = new RaceServer(
    new RaceServerOptions { Address = address, Port = port, MaxPlayers = maxPlayers },
    message => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}"));

Console.WriteLine("Unity Lab race server - press Ctrl+C to stop.");
await server.RunAsync(cancel.Token);
return 0;

string? ReadArg(string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

int ReadInt(string name, string environment, int fallback)
{
    string? text = ReadArg(name) ?? Environment.GetEnvironmentVariable(environment);
    return text == null ? fallback : int.TryParse(text, out int value) ? value : -1;
}
