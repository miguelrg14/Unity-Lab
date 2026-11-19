using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using UnityLab.Racing.Protocol;

namespace UnityLab.RaceServer;

public sealed class RaceServerOptions
{
    public IPAddress Address { get; init; } = IPAddress.Any;
    public int Port { get; init; } = ProtocolConstants.DefaultPort;

    /// <summary>The circuit has four starting slots.</summary>
    public int MaxPlayers { get; init; } = 4;

    /// <summary>Time a new connection has to send its <see cref="JoinRequest"/>.</summary>
    public TimeSpan JoinTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Frames waiting to be sent to one client before it is considered too slow and dropped.</summary>
    public int SendQueueCapacity { get; init; } = 256;
}

/// <summary>
/// Relay server for online races. Each client simulates its own car and reports its state; the
/// server assigns identities and starting slots, stamps every state with the sender's id and
/// forwards it to everybody else.
///
/// Concurrency model: one task accepts connections, each client has a receive task and a send
/// task fed by its own channel, so a slow client never blocks the others. Shared state (the
/// player list and the slots) is only touched under <see cref="gate"/>.
/// </summary>
public sealed class RaceServer : IAsyncDisposable
{
    readonly RaceServerOptions options;
    readonly Action<string> log;
    readonly object gate = new();
    readonly SlotAllocator slots;
    readonly Dictionary<byte, ClientSession> players = new();
    readonly ConcurrentDictionary<ClientSession, Task> sessions = new();
    readonly CancellationTokenSource shutdown = new();
    TcpListener? listener;
    Task? acceptLoop;
    int nextId;

    public RaceServer(RaceServerOptions options, Action<string>? log = null)
    {
        this.options = options;
        this.log = log ?? (_ => { });
        slots = new SlotAllocator(options.MaxPlayers);
    }

    /// <summary>Where the server is listening; useful when started on port 0.</summary>
    public IPEndPoint LocalEndPoint => (IPEndPoint)(listener ?? throw new InvalidOperationException("Not started.")).LocalEndpoint;

    public int PlayerCount
    {
        get { lock (gate) return players.Count; }
    }

    public void Start()
    {
        listener = new TcpListener(options.Address, options.Port);
        listener.Start();
        log($"Listening on {LocalEndPoint} (max {options.MaxPlayers} players).");
        acceptLoop = AcceptLoopAsync(shutdown.Token);
    }

    /// <summary>Runs until the token is cancelled.</summary>
    public async Task RunAsync(CancellationToken token)
    {
        Start();
        try
        {
            await Task.Delay(Timeout.Infinite, token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (shutdown.IsCancellationRequested)
            return;

        shutdown.Cancel();
        listener?.Stop();
        foreach (ClientSession session in sessions.Keys)
            session.Close();

        try
        {
            if (acceptLoop != null)
                await acceptLoop;
            await Task.WhenAll(sessions.Values);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Expected while tearing down.
        }
        log("Server stopped.");
    }

    async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener!.AcceptTcpClientAsync(token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            client.NoDelay = true;
            var session = new ClientSession(client, options.SendQueueCapacity);
            sessions[session] = RunSessionAsync(session, token);
        }
    }

    async Task RunSessionAsync(ClientSession session, CancellationToken token)
    {
        string endpoint = session.RemoteEndPoint;
        log($"Connection from {endpoint}.");
        Task sender = session.RunSenderAsync(token);
        string reason = "disconnected";
        try
        {
            await ReceiveLoopAsync(session, token);
        }
        catch (ProtocolException exception)
        {
            reason = "protocol error: " + exception.Message;
        }
        catch (TimeoutException exception)
        {
            reason = exception.Message;
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            reason = "connection closed";
        }
        finally
        {
            RemovePlayer(session);
            session.Complete();
            try { await sender; } catch (Exception) { /* the socket is going away anyway */ }
            session.Close();
            sessions.TryRemove(session, out _);
            log($"{session.DisplayName} ({endpoint}) left: {reason}.");
        }
    }

    async Task ReceiveLoopAsync(ClientSession session, CancellationToken token)
    {
        var reader = new FrameReader();
        var buffer = new byte[4096];
        NetworkStream stream = session.Stream;

        using (var joinDeadline = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            joinDeadline.CancelAfter(options.JoinTimeout);
            while (session.Player == null)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer, joinDeadline.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new TimeoutException("did not send a join request in time");
                }

                if (read == 0)
                    return;

                List<byte[]> payloads = reader.Feed(buffer, 0, read);
                for (int i = 0; i < payloads.Count; i++)
                {
                    Message message = MessageCodec.DecodePayload(payloads[i]);
                    if (session.Player == null)
                    {
                        if (message is not JoinRequest join)
                            throw new ProtocolException($"expected JoinRequest, got {message.Type}");
                        if (!TryJoin(session, join))
                            return;
                    }
                    else
                    {
                        Handle(session, message);
                    }
                }
            }
        }

        while (!token.IsCancellationRequested)
        {
            int read = await stream.ReadAsync(buffer, token);
            if (read == 0)
                return;

            foreach (byte[] payload in reader.Feed(buffer, 0, read))
                Handle(session, MessageCodec.DecodePayload(payload));
        }
    }

    bool TryJoin(ClientSession session, JoinRequest join)
    {
        if (join.ProtocolVersion != ProtocolConstants.Version)
        {
            session.SendAndClose(new Rejected($"Protocol version {join.ProtocolVersion} is not supported (server uses {ProtocolConstants.Version})."));
            return false;
        }

        PlayerInfo you;
        PlayerInfo[] others;
        lock (gate)
        {
            int slot = slots.Acquire();
            if (slot < 0)
            {
                session.SendAndClose(new Rejected("The race is full."));
                return false;
            }

            var names = players.Values.Select(p => p.Player!.Nickname).ToList();
            string nickname = Nickname.MakeUnique(Nickname.Sanitize(join.Nickname), names);
            byte id = (byte)(nextId++ % 256);
            while (players.ContainsKey(id))
                id = (byte)(nextId++ % 256);

            you = new PlayerInfo(id, (byte)slot, nickname);
            others = players.Values.Select(p => p.Player!).ToArray();
            session.Player = you;
            players[id] = session;

            // Queued under the lock so every client sees joins in the same order.
            session.Send(new Welcome(you, others));
            BroadcastExcept(session, new PlayerJoined(you));
        }

        log($"{you.Nickname} joined as player {you.Id} in slot {you.Slot}.");
        return true;
    }

    void Handle(ClientSession session, Message message)
    {
        switch (message)
        {
            case CarState state:
                // Never trust the id a client claims: it can only move its own car.
                state.PlayerId = session.Player!.Id;
                lock (gate)
                    BroadcastExcept(session, state);
                break;
            case JoinRequest:
                throw new ProtocolException("already joined");
            default:
                throw new ProtocolException($"clients cannot send {message.Type}");
        }
    }

    void RemovePlayer(ClientSession session)
    {
        lock (gate)
        {
            PlayerInfo? player = session.Player;
            if (player == null || !players.Remove(player.Id))
                return;
            slots.Release(player.Slot);
            BroadcastExcept(session, new PlayerLeft(player.Id));
        }
    }

    /// <summary>Must be called while holding <see cref="gate"/>.</summary>
    void BroadcastExcept(ClientSession sender, Message message)
    {
        byte[] frame = MessageCodec.EncodeFrame(message);
        foreach (ClientSession other in players.Values)
        {
            if (other != sender && !other.TrySendFrame(frame))
            {
                log($"{other.DisplayName} is not keeping up; disconnecting.");
                other.Close();
            }
        }
    }
}
