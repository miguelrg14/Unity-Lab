using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace UnityLab.Racing.Protocol
{
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
    }

    /// <summary>
    /// Client side of the race protocol. Connecting and receiving happen off the caller's thread;
    /// received messages are queued so a game loop can consume them with <see cref="TryReceive"/>
    /// on its own thread (Unity's API can only be used from the main thread).
    /// </summary>
    public sealed class RaceClient : IDisposable
    {
        readonly ConcurrentQueue<Message> inbox = new ConcurrentQueue<Message>();
        readonly object sendLock = new object();
        TcpClient tcp;
        NetworkStream stream;
        Thread receiveThread;
        volatile ConnectionState state = ConnectionState.Disconnected;
        volatile string lastError;

        public ConnectionState State => state;

        /// <summary>Why the connection failed or closed, or null.</summary>
        public string LastError => lastError;

        /// <summary>Opens the connection and sends the join request.</summary>
        /// <returns>False if the server could not be reached in time; see <see cref="LastError"/>.</returns>
        public async Task<bool> ConnectAsync(string host, int port, string nickname, TimeSpan timeout)
        {
            if (state != ConnectionState.Disconnected)
                throw new InvalidOperationException("Already connected or connecting.");

            state = ConnectionState.Connecting;
            lastError = null;
            tcp = new TcpClient { NoDelay = true };
            try
            {
                Task connect = tcp.ConnectAsync(host, port);
                if (await Task.WhenAny(connect, Task.Delay(timeout)).ConfigureAwait(false) != connect)
                    throw new TimeoutException($"No answer from {host}:{port} after {timeout.TotalSeconds:0} s.");
                await connect.ConfigureAwait(false);

                stream = tcp.GetStream();
                state = ConnectionState.Connected;
                receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "RaceClient receive" };
                receiveThread.Start();
                Send(new JoinRequest(Nickname.Sanitize(nickname)));
                return true;
            }
            catch (Exception exception)
            {
                Close(exception is SocketException socket ? $"Could not connect: {socket.SocketErrorCode}" : exception.Message);
                return false;
            }
        }

        /// <summary>Sends a message. Safe to call from any thread; ignored when not connected.</summary>
        public void Send(Message message)
        {
            if (state != ConnectionState.Connected)
                return;

            byte[] frame = MessageCodec.EncodeFrame(message);
            try
            {
                lock (sendLock)
                    stream.Write(frame, 0, frame.Length);
            }
            catch (Exception exception) when (exception is IOException || exception is ObjectDisposedException)
            {
                Close("Connection lost while sending.");
            }
        }

        /// <summary>Takes the next message received from the server, if any.</summary>
        public bool TryReceive(out Message message) => inbox.TryDequeue(out message);

        public void Dispose() => Close(null);

        void ReceiveLoop()
        {
            var reader = new FrameReader();
            var chunk = new byte[4096];
            try
            {
                while (state == ConnectionState.Connected)
                {
                    int read = stream.Read(chunk, 0, chunk.Length);
                    if (read == 0)
                    {
                        Close("The server closed the connection.");
                        return;
                    }

                    foreach (byte[] payload in reader.Feed(chunk, 0, read))
                        inbox.Enqueue(MessageCodec.DecodePayload(payload));
                }
            }
            catch (ProtocolException exception)
            {
                Close("Invalid data from server: " + exception.Message);
            }
            catch (Exception exception) when (exception is IOException || exception is ObjectDisposedException)
            {
                // Expected when Dispose closes the socket under a blocking Read.
                Close(state == ConnectionState.Connected ? "Connection lost." : null);
            }
        }

        void Close(string reason)
        {
            if (reason != null && lastError == null)
                lastError = reason;
            state = ConnectionState.Disconnected;
            try { stream?.Dispose(); } catch (Exception) { /* already closed */ }
            try { tcp?.Close(); } catch (Exception) { /* already closed */ }
        }
    }
}
