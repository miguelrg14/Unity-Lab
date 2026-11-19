using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using UnityLab.Racing.Protocol;

namespace UnityLab.RaceServer.Tests;

/// <summary>
/// Integration tests: a real server on a free local port and real TCP clients, using the same
/// <see cref="RaceClient"/> class the Unity game uses.
/// </summary>
[TestFixture]
[Timeout(20000)]
public class RaceServerTests
{
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    RaceServer server = null!;
    readonly List<RaceClient> clients = new();
    readonly List<string> log = new();

    [SetUp]
    public void SetUp()
    {
        server = new RaceServer(
            new RaceServerOptions { Address = IPAddress.Loopback, Port = 0, MaxPlayers = 3, JoinTimeout = TimeSpan.FromMilliseconds(500) },
            message => { lock (log) log.Add(message); });
        server.Start();
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (RaceClient client in clients)
            client.Dispose();
        await server.DisposeAsync();
    }

    async Task<RaceClient> ConnectAsync(string nickname)
    {
        var client = new RaceClient();
        clients.Add(client);
        bool connected = await client.ConnectAsync("127.0.0.1", server.LocalEndPoint.Port, nickname, Wait);
        Assert.That(connected, Is.True, client.LastError);
        return client;
    }

    static async Task<T> ReceiveAsync<T>(RaceClient client) where T : Message
    {
        DateTime deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            if (client.TryReceive(out Message message))
            {
                if (message is T typed)
                    return typed;
                Assert.Fail($"Expected {typeof(T).Name} but received {message.Type}.");
            }
            await Task.Delay(5);
        }
        Assert.Fail($"Timed out waiting for {typeof(T).Name}. Client state: {client.State}, error: {client.LastError}");
        return null!;
    }

    static async Task AssertNothingReceivedAsync(RaceClient client)
    {
        await Task.Delay(200);
        Assert.That(client.TryReceive(out Message message), Is.False, () => $"Unexpected {message?.Type}.");
    }

    static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        DateTime deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Timed out waiting until " + what);
            await Task.Delay(10);
        }
    }

    [Test]
    public async Task FirstPlayer_GetsSlotZeroAndAnEmptyList()
    {
        RaceClient ana = await ConnectAsync("Ana");
        Welcome welcome = await ReceiveAsync<Welcome>(ana);

        Assert.That(welcome.You.Slot, Is.EqualTo(0));
        Assert.That(welcome.You.Nickname, Is.EqualTo("Ana"));
        Assert.That(welcome.Others, Is.Empty);
    }

    [Test]
    public async Task SecondPlayer_SeesTheFirst_AndTheFirstIsTold()
    {
        RaceClient ana = await ConnectAsync("Ana");
        Welcome anaWelcome = await ReceiveAsync<Welcome>(ana);
        RaceClient luis = await ConnectAsync("Luis");
        Welcome luisWelcome = await ReceiveAsync<Welcome>(luis);

        Assert.That(luisWelcome.You.Slot, Is.EqualTo(1));
        Assert.That(luisWelcome.Others.Single().Id, Is.EqualTo(anaWelcome.You.Id));

        PlayerJoined joined = await ReceiveAsync<PlayerJoined>(ana);
        Assert.That(joined.Player.Nickname, Is.EqualTo("Luis"));
        Assert.That(joined.Player.Id, Is.EqualTo(luisWelcome.You.Id));
    }

    [Test]
    public async Task DuplicateNicknames_AreMadeUnique()
    {
        RaceClient first = await ConnectAsync("Ana");
        await ReceiveAsync<Welcome>(first);
        RaceClient second = await ConnectAsync("ana");
        Welcome welcome = await ReceiveAsync<Welcome>(second);

        Assert.That(welcome.You.Nickname, Is.EqualTo("ana 2"));
    }

    [Test]
    public async Task CarState_IsRelayedToOthers_WithTheSendersRealId()
    {
        RaceClient ana = await ConnectAsync("Ana");
        await ReceiveAsync<Welcome>(ana);
        RaceClient luis = await ConnectAsync("Luis");
        Welcome luisWelcome = await ReceiveAsync<Welcome>(luis);
        await ReceiveAsync<PlayerJoined>(ana);

        // Luis pretends to be someone else; the server must overwrite the id.
        luis.Send(new CarState { PlayerId = 200, Position = new NetVector3(1, 2, 3), Rotation = NetQuaternion.Identity, WaypointsPassed = 4, Lap = 1 });

        CarState relayed = await ReceiveAsync<CarState>(ana);
        Assert.That(relayed.PlayerId, Is.EqualTo(luisWelcome.You.Id));
        Assert.That(relayed.Position.Z, Is.EqualTo(3));
        Assert.That(relayed.WaypointsPassed, Is.EqualTo(4));
        await AssertNothingReceivedAsync(luis);
    }

    [Test]
    public async Task Disconnect_IsAnnounced_AndFreesTheSlot()
    {
        RaceClient ana = await ConnectAsync("Ana");
        await ReceiveAsync<Welcome>(ana);
        RaceClient luis = await ConnectAsync("Luis");
        Welcome luisWelcome = await ReceiveAsync<Welcome>(luis);
        await ReceiveAsync<PlayerJoined>(ana);

        luis.Dispose();
        PlayerLeft left = await ReceiveAsync<PlayerLeft>(ana);
        Assert.That(left.PlayerId, Is.EqualTo(luisWelcome.You.Id));

        RaceClient eva = await ConnectAsync("Eva");
        Welcome evaWelcome = await ReceiveAsync<Welcome>(eva);
        Assert.That(evaWelcome.You.Slot, Is.EqualTo(1), "Luis's slot is reused.");
    }

    [Test]
    public async Task FullRace_RejectsTheExtraPlayer()
    {
        for (int i = 0; i < 3; i++)
            await ReceiveAsync<Welcome>(await ConnectAsync("P" + i));

        RaceClient extra = await ConnectAsync("Extra");
        Rejected rejected = await ReceiveAsync<Rejected>(extra);
        Assert.That(rejected.Reason, Does.Contain("full"));
        await WaitUntilAsync(() => extra.State == ConnectionState.Disconnected, "the server closes the connection");
        Assert.That(server.PlayerCount, Is.EqualTo(3));
    }

    [Test]
    public async Task WrongProtocolVersion_IsRejected()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, server.LocalEndPoint.Port);
        NetworkStream stream = tcp.GetStream();
        byte[] frame = MessageCodec.EncodeFrame(new JoinRequest("Old", protocolVersion: 99));
        await stream.WriteAsync(frame);

        Message reply = await ReadOneAsync(stream);
        Assert.That(reply, Is.TypeOf<Rejected>());
        Assert.That(((Rejected)reply).Reason, Does.Contain("version"));
    }

    [Test]
    public async Task GarbageData_DropsOnlyThatClient()
    {
        RaceClient ana = await ConnectAsync("Ana");
        await ReceiveAsync<Welcome>(ana);

        using var attacker = new TcpClient();
        await attacker.ConnectAsync(IPAddress.Loopback, server.LocalEndPoint.Port);
        // A header announcing a 65 KB frame, far above the limit.
        await attacker.GetStream().WriteAsync(new byte[] { 0xFF, 0xFF, 1, 2, 3 });

        Assert.That(await ReadToEndAsync(attacker.GetStream()), Is.Zero, "The server closes the connection.");
        await WaitUntilAsync(() => Logged("protocol error"), "the server logs the protocol error");

        RaceClient luis = await ConnectAsync("Luis");
        Assert.That((await ReceiveAsync<Welcome>(luis)).You.Slot, Is.EqualTo(1), "The server keeps working.");
    }

    [Test]
    public async Task ClientThatNeverJoins_IsDisconnectedAfterTheTimeout()
    {
        using var idle = new TcpClient();
        await idle.ConnectAsync(IPAddress.Loopback, server.LocalEndPoint.Port);

        Assert.That(await ReadToEndAsync(idle.GetStream()), Is.Zero);
        await WaitUntilAsync(() => Logged("did not send a join request in time"), "the timeout is logged");
    }

    [Test]
    public async Task SendingSomethingOtherThanJoinFirst_IsAProtocolError()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, server.LocalEndPoint.Port);
        await tcp.GetStream().WriteAsync(MessageCodec.EncodeFrame(new CarState { Rotation = NetQuaternion.Identity }));

        Assert.That(await ReadToEndAsync(tcp.GetStream()), Is.Zero);
        await WaitUntilAsync(() => Logged("expected JoinRequest"), "the error is logged");
    }

    [Test]
    public async Task ManyClients_ExchangeStatesConcurrently()
    {
        var racers = new List<(RaceClient Client, byte Id)>();
        for (int i = 0; i < 3; i++)
        {
            RaceClient client = await ConnectAsync("R" + i);
            racers.Add((client, (await ReceiveAsync<Welcome>(client)).You.Id));
        }

        const int statesPerClient = 50;
        await Task.WhenAll(racers.Select(r => Task.Run(() =>
        {
            for (int n = 0; n < statesPerClient; n++)
                r.Client.Send(new CarState { Position = new NetVector3(n, 0, 0), Rotation = NetQuaternion.Identity });
        })));

        foreach ((RaceClient client, byte id) in racers)
        {
            var counts = new Dictionary<byte, int>();
            DateTime deadline = DateTime.UtcNow + Wait;
            while (counts.Values.Sum() < statesPerClient * 2 && DateTime.UtcNow < deadline)
            {
                if (client.TryReceive(out Message message) && message is CarState state)
                    counts[state.PlayerId] = counts.GetValueOrDefault(state.PlayerId) + 1;
                else
                    await Task.Delay(2);
            }

            Assert.That(counts.ContainsKey(id), Is.False, "A client never receives its own state.");
            Assert.That(counts.Values, Is.All.EqualTo(statesPerClient), "Every state of every other client arrives.");
        }
    }

    bool Logged(string text)
    {
        lock (log)
            return log.Any(line => line.Contains(text));
    }

    static async Task<Message> ReadOneAsync(NetworkStream stream)
    {
        var reader = new FrameReader();
        var buffer = new byte[1024];
        using var timeout = new CancellationTokenSource(Wait);
        while (true)
        {
            int read = await stream.ReadAsync(buffer, timeout.Token);
            if (read == 0)
                throw new IOException("Closed before a message arrived.");
            List<byte[]> payloads = reader.Feed(buffer, 0, read);
            if (payloads.Count > 0)
                return MessageCodec.DecodePayload(payloads[0]);
        }
    }

    /// <summary>Reads until the server closes the connection and returns the bytes that came with the close.</summary>
    static async Task<int> ReadToEndAsync(NetworkStream stream)
    {
        var buffer = new byte[1024];
        using var timeout = new CancellationTokenSource(Wait);
        try
        {
            return await stream.ReadAsync(buffer, timeout.Token);
        }
        catch (IOException)
        {
            return 0; // Connection reset counts as closed.
        }
    }
}

public class SlotAllocatorTests
{
    [Test]
    public void HandsOutTheLowestFreeSlot()
    {
        var slots = new SlotAllocator(3);
        Assert.That(slots.Acquire(), Is.EqualTo(0));
        Assert.That(slots.Acquire(), Is.EqualTo(1));
        Assert.That(slots.Acquire(), Is.EqualTo(2));
        Assert.That(slots.Acquire(), Is.EqualTo(-1));

        slots.Release(1);
        Assert.That(slots.InUse, Is.EqualTo(2));
        Assert.That(slots.Acquire(), Is.EqualTo(1));
    }

    [Test]
    public void InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlotAllocator(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlotAllocator(2).Release(5));
    }
}
