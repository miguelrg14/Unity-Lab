using System;
using System.Collections.Generic;

namespace UnityLab.Racing.Protocol
{
    /// <summary>
    /// Rebuilds frames from a TCP byte stream. TCP has no message boundaries: one read can return
    /// half a frame or several frames at once, so bytes are buffered until a whole frame is available.
    /// </summary>
    public sealed class FrameReader
    {
        const int MaxFrameSize = ProtocolConstants.HeaderSize + ProtocolConstants.MaxPayloadSize;

        readonly byte[] buffer = new byte[MaxFrameSize];
        int count;

        /// <summary>Bytes received that do not yet form a whole frame.</summary>
        public int BufferedBytes => count;

        /// <summary>
        /// Adds bytes read from the socket and returns every payload they complete, in order.
        /// Any amount of data can be passed in one call.
        /// </summary>
        /// <exception cref="ProtocolException">The peer sent an empty or oversized frame.</exception>
        public List<byte[]> Feed(byte[] data, int offset, int length)
        {
            var payloads = new List<byte[]>();
            while (length > 0)
            {
                int chunk = Math.Min(buffer.Length - count, length);
                Buffer.BlockCopy(data, offset, buffer, count, chunk);
                count += chunk;
                offset += chunk;
                length -= chunk;

                while (TryExtract(out byte[] payload))
                    payloads.Add(payload);
            }
            return payloads;
        }

        bool TryExtract(out byte[] payload)
        {
            payload = null;
            if (count < ProtocolConstants.HeaderSize)
                return false;

            // The header is validated as soon as it arrives, before waiting for the body.
            int length = buffer[0] | (buffer[1] << 8);
            if (length == 0)
                throw new ProtocolException("Empty frame.");
            if (length > ProtocolConstants.MaxPayloadSize)
                throw new ProtocolException($"Frame of {length} bytes exceeds the {ProtocolConstants.MaxPayloadSize} byte limit.");

            int frameSize = ProtocolConstants.HeaderSize + length;
            if (count < frameSize)
                return false;

            payload = new byte[length];
            Buffer.BlockCopy(buffer, ProtocolConstants.HeaderSize, payload, 0, length);
            count -= frameSize;
            Buffer.BlockCopy(buffer, frameSize, buffer, 0, count);
            return true;
        }
    }
}
