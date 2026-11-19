namespace UnityLab.Racing.Protocol
{
    /// <summary>Identifies each message on the wire. The value is the first byte of a payload.</summary>
    public enum MessageType : byte
    {
        JoinRequest = 1,
        Welcome = 2,
        PlayerJoined = 3,
        PlayerLeft = 4,
        CarState = 5,
        Rejected = 6,
    }

    /// <summary>Common base of every message exchanged between the race server and its clients.</summary>
    public abstract class Message
    {
        public abstract MessageType Type { get; }
    }

    /// <summary>Position in world space. Kept engine-free so the server does not depend on Unity.</summary>
    public struct NetVector3
    {
        public float X, Y, Z;

        public NetVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>Rotation as a quaternion, sent as-is to avoid Euler angle wrap-around problems.</summary>
    public struct NetQuaternion
    {
        public float X, Y, Z, W;

        public NetQuaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static NetQuaternion Identity => new NetQuaternion(0, 0, 0, 1);
    }

    /// <summary>A connected player as the other clients see it.</summary>
    public sealed class PlayerInfo
    {
        public byte Id;
        public byte Slot;
        public string Nickname;

        public PlayerInfo(byte id, byte slot, string nickname)
        {
            Id = id;
            Slot = slot;
            Nickname = nickname;
        }
    }

    /// <summary>Client → server. First message a client must send after connecting.</summary>
    public sealed class JoinRequest : Message
    {
        public override MessageType Type => MessageType.JoinRequest;
        public byte ProtocolVersion = ProtocolConstants.Version;
        public string Nickname;

        public JoinRequest(string nickname, byte protocolVersion = ProtocolConstants.Version)
        {
            Nickname = nickname;
            ProtocolVersion = protocolVersion;
        }
    }

    /// <summary>Server → new client. Assigns its id and starting slot and lists who is already racing.</summary>
    public sealed class Welcome : Message
    {
        public override MessageType Type => MessageType.Welcome;
        public PlayerInfo You;
        public PlayerInfo[] Others;

        public Welcome(PlayerInfo you, PlayerInfo[] others)
        {
            You = you;
            Others = others;
        }
    }

    /// <summary>Server → existing clients when someone joins.</summary>
    public sealed class PlayerJoined : Message
    {
        public override MessageType Type => MessageType.PlayerJoined;
        public PlayerInfo Player;

        public PlayerJoined(PlayerInfo player) => Player = player;
    }

    /// <summary>Server → remaining clients when someone disconnects.</summary>
    public sealed class PlayerLeft : Message
    {
        public override MessageType Type => MessageType.PlayerLeft;
        public byte PlayerId;

        public PlayerLeft(byte playerId) => PlayerId = playerId;
    }

    /// <summary>
    /// Snapshot of one car. Clients send their own; the server overwrites <see cref="PlayerId"/>
    /// with the sender's real id before relaying it, so a client cannot move someone else's car.
    /// </summary>
    public sealed class CarState : Message
    {
        public override MessageType Type => MessageType.CarState;
        public byte PlayerId;
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public ushort WaypointsPassed;
        public byte Lap;
        public bool Finished;
    }

    /// <summary>Server → client right before closing a connection it will not accept.</summary>
    public sealed class Rejected : Message
    {
        public override MessageType Type => MessageType.Rejected;
        public string Reason;

        public Rejected(string reason) => Reason = reason;
    }

    public static class ProtocolConstants
    {
        /// <summary>Bumped whenever the wire format changes.</summary>
        public const byte Version = 1;

        /// <summary>Largest payload accepted. Anything bigger is treated as a protocol violation.</summary>
        public const int MaxPayloadSize = 1024;

        /// <summary>Size of the length prefix that precedes every payload.</summary>
        public const int HeaderSize = 2;

        public const int MaxNicknameLength = 16;

        public const int DefaultPort = 7777;
    }
}
