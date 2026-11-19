using System.Net.Sockets;
using System.Threading.Channels;
using UnityLab.Racing.Protocol;

namespace UnityLab.RaceServer;

/// <summary>One connected client: its socket, its identity once joined and its outgoing queue.</summary>
sealed class ClientSession
{
    readonly TcpClient client;
    readonly Channel<byte[]> outbox;
    int closed;

    public ClientSession(TcpClient client, int sendQueueCapacity)
    {
        this.client = client;
        Stream = client.GetStream();
        RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        outbox = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(sendQueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
    }

    public NetworkStream Stream { get; }
    public string RemoteEndPoint { get; }

    /// <summary>Set once the client has joined. Written and read under the server's lock.</summary>
    public PlayerInfo? Player { get; set; }

    public string DisplayName => Player != null ? $"{Player.Nickname} (#{Player.Id})" : "Unjoined client";

    public void Send(Message message) => TrySendFrame(MessageCodec.EncodeFrame(message));

    /// <returns>False when the client's queue is full.</returns>
    public bool TrySendFrame(byte[] frame) => outbox.Writer.TryWrite(frame);

    /// <summary>Queues a last message; the connection closes once it has been written.</summary>
    public void SendAndClose(Message message)
    {
        Send(message);
        Complete();
    }

    /// <summary>No more messages will be queued; the sender finishes after flushing.</summary>
    public void Complete() => outbox.Writer.TryComplete();

    public async Task RunSenderAsync(CancellationToken token)
    {
        await foreach (byte[] frame in outbox.Reader.ReadAllAsync(token))
            await Stream.WriteAsync(frame, token);
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref closed, 1) == 1)
            return;
        Complete();
        client.Close();
    }
}

/// <summary>Hands out the lowest free starting slot.</summary>
public sealed class SlotAllocator
{
    readonly bool[] taken;

    public SlotAllocator(int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        taken = new bool[count];
    }

    public int Capacity => taken.Length;
    public int InUse => taken.Count(t => t);

    /// <returns>The slot index, or -1 when every slot is taken.</returns>
    public int Acquire()
    {
        for (int i = 0; i < taken.Length; i++)
        {
            if (!taken[i])
            {
                taken[i] = true;
                return i;
            }
        }
        return -1;
    }

    public void Release(int slot)
    {
        if (slot < 0 || slot >= taken.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));
        taken[slot] = false;
    }
}
