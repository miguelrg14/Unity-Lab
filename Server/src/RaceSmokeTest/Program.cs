using UnityLab.Racing.Protocol;

// Usage: RaceSmokeTest [host] [port]
// Exit code 0 when two clients can join, see each other, exchange a car state and see a disconnect.
string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 ? int.Parse(args[1]) : ProtocolConstants.DefaultPort;
TimeSpan timeout = TimeSpan.FromSeconds(10);

try
{
    using var alice = new RaceClient();
    using var bob = new RaceClient();

    await ConnectWithRetries(alice, "Alice");
    Welcome aliceWelcome = await Expect<Welcome>(alice);
    Step($"Alice joined as player {aliceWelcome.You.Id} in slot {aliceWelcome.You.Slot}");

    await ConnectWithRetries(bob, "Bob");
    Welcome bobWelcome = await Expect<Welcome>(bob);
    Check(bobWelcome.Others.Any(p => p.Nickname == "Alice"), "Bob sees Alice in the welcome list");
    PlayerJoined joined = await Expect<PlayerJoined>(alice);
    Check(joined.Player.Nickname == "Bob", "Alice is told that Bob joined");

    bob.Send(new CarState { PlayerId = 99, Position = new NetVector3(10, 0, 20), Rotation = NetQuaternion.Identity, WaypointsPassed = 3, Lap = 1 });
    CarState state = await Expect<CarState>(alice);
    Check(state.PlayerId == bobWelcome.You.Id && state.Position.Z == 20, "Alice receives Bob's car state stamped with Bob's id");

    bob.Dispose();
    PlayerLeft left = await Expect<PlayerLeft>(alice);
    Check(left.PlayerId == bobWelcome.You.Id, "Alice is told that Bob left");

    Console.WriteLine("SMOKE TEST PASSED");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("SMOKE TEST FAILED: " + exception.Message);
    return 1;
}

async Task ConnectWithRetries(RaceClient client, string name)
{
    // The server may still be starting when the test begins.
    for (int attempt = 1; attempt <= 20; attempt++)
    {
        if (await client.ConnectAsync(host, port, name, TimeSpan.FromSeconds(2)))
            return;
        await Task.Delay(500); // A failed attempt leaves the client disconnected and reusable.
    }
    throw new Exception($"Could not connect to {host}:{port}.");
}

async Task<T> Expect<T>(RaceClient client) where T : Message
{
    DateTime deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        if (client.TryReceive(out Message message))
            return message as T ?? throw new Exception($"Expected {typeof(T).Name}, got {message.Type}.");
        await Task.Delay(10);
    }
    throw new Exception($"Timed out waiting for {typeof(T).Name} ({client.LastError}).");
}

void Check(bool condition, string description)
{
    if (!condition)
        throw new Exception("Check failed: " + description);
    Step(description);
}

void Step(string text) => Console.WriteLine("  ok  " + text);
