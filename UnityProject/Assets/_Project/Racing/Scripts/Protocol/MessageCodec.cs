using System;
using System.IO;
using System.Text;

namespace UnityLab.Racing.Protocol
{
    /// <summary>Thrown when bytes received from the network do not form a valid message.</summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
    }

    /// <summary>
    /// Turns messages into length-prefixed frames and back.
    /// Frame layout: [payload length: uint16 little-endian][payload]. Payload: [type: byte][fields].
    /// Binary encoding keeps floats exact and independent of the machine's culture settings.
    /// </summary>
    public static class MessageCodec
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        /// <summary>Encodes a message as a complete frame, ready to be written to a stream.</summary>
        public static byte[] EncodeFrame(Message message)
        {
            byte[] payload = EncodePayload(message);
            if (payload.Length > ProtocolConstants.MaxPayloadSize)
                throw new ProtocolException($"Payload of {payload.Length} bytes exceeds the {ProtocolConstants.MaxPayloadSize} byte limit.");

            var frame = new byte[ProtocolConstants.HeaderSize + payload.Length];
            frame[0] = (byte)(payload.Length & 0xFF);
            frame[1] = (byte)(payload.Length >> 8);
            Buffer.BlockCopy(payload, 0, frame, ProtocolConstants.HeaderSize, payload.Length);
            return frame;
        }

        public static byte[] EncodePayload(Message message)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)message.Type);
                switch (message)
                {
                    case JoinRequest join:
                        writer.Write(join.ProtocolVersion);
                        WriteString(writer, join.Nickname);
                        break;
                    case Welcome welcome:
                        WritePlayer(writer, welcome.You);
                        writer.Write((byte)welcome.Others.Length);
                        foreach (PlayerInfo other in welcome.Others)
                            WritePlayer(writer, other);
                        break;
                    case PlayerJoined joined:
                        WritePlayer(writer, joined.Player);
                        break;
                    case PlayerLeft left:
                        writer.Write(left.PlayerId);
                        break;
                    case CarState state:
                        writer.Write(state.PlayerId);
                        writer.Write(state.Position.X);
                        writer.Write(state.Position.Y);
                        writer.Write(state.Position.Z);
                        writer.Write(state.Rotation.X);
                        writer.Write(state.Rotation.Y);
                        writer.Write(state.Rotation.Z);
                        writer.Write(state.Rotation.W);
                        writer.Write(state.WaypointsPassed);
                        writer.Write(state.Lap);
                        writer.Write(state.Finished);
                        break;
                    case Rejected rejected:
                        WriteString(writer, rejected.Reason);
                        break;
                    default:
                        throw new ArgumentException($"Unknown message {message.GetType().Name}.");
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>Decodes one payload (without its length prefix). Never returns null.</summary>
        public static Message DecodePayload(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
                throw new ProtocolException("Empty payload.");

            using (var reader = new BinaryReader(new MemoryStream(payload)))
            {
                try
                {
                    Message message = ReadBody(reader, (MessageType)reader.ReadByte());
                    if (reader.BaseStream.Position != payload.Length)
                        throw new ProtocolException("Unexpected trailing bytes.");
                    return message;
                }
                catch (EndOfStreamException)
                {
                    throw new ProtocolException("Truncated payload.");
                }
                catch (DecoderFallbackException)
                {
                    throw new ProtocolException("Invalid UTF-8 text.");
                }
            }
        }

        static Message ReadBody(BinaryReader reader, MessageType type)
        {
            switch (type)
            {
                case MessageType.JoinRequest:
                {
                    byte version = reader.ReadByte();
                    return new JoinRequest(ReadString(reader), version);
                }
                case MessageType.Welcome:
                {
                    PlayerInfo you = ReadPlayer(reader);
                    var others = new PlayerInfo[reader.ReadByte()];
                    for (int i = 0; i < others.Length; i++)
                        others[i] = ReadPlayer(reader);
                    return new Welcome(you, others);
                }
                case MessageType.PlayerJoined:
                    return new PlayerJoined(ReadPlayer(reader));
                case MessageType.PlayerLeft:
                    return new PlayerLeft(reader.ReadByte());
                case MessageType.CarState:
                {
                    var state = new CarState
                    {
                        PlayerId = reader.ReadByte(),
                        Position = new NetVector3(ReadFinite(reader), ReadFinite(reader), ReadFinite(reader)),
                        Rotation = new NetQuaternion(ReadFinite(reader), ReadFinite(reader), ReadFinite(reader), ReadFinite(reader)),
                        WaypointsPassed = reader.ReadUInt16(),
                        Lap = reader.ReadByte(),
                        Finished = reader.ReadBoolean(),
                    };
                    return state;
                }
                case MessageType.Rejected:
                    return new Rejected(ReadString(reader));
                default:
                    throw new ProtocolException($"Unknown message type {(byte)type}.");
            }
        }

        static void WritePlayer(BinaryWriter writer, PlayerInfo player)
        {
            writer.Write(player.Id);
            writer.Write(player.Slot);
            WriteString(writer, player.Nickname);
        }

        static PlayerInfo ReadPlayer(BinaryReader reader)
        {
            byte id = reader.ReadByte();
            byte slot = reader.ReadByte();
            return new PlayerInfo(id, slot, ReadString(reader));
        }

        static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Utf8.GetBytes(value ?? string.Empty);
            if (bytes.Length > byte.MaxValue)
                throw new ProtocolException("String too long.");
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }

        static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadByte();
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
                throw new EndOfStreamException();
            return Utf8.GetString(bytes);
        }

        static float ReadFinite(BinaryReader reader)
        {
            float value = reader.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ProtocolException("Non-finite number.");
            return value;
        }
    }
}
