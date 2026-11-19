using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityLab.Racing.Protocol;

namespace UnityLab.Tests.EditMode
{
    public class MessageCodecTests
    {
        static T RoundTrip<T>(T message) where T : Message
        {
            byte[] frame = MessageCodec.EncodeFrame(message);
            List<byte[]> payloads = new FrameReader().Feed(frame, 0, frame.Length);
            Assert.That(payloads.Count, Is.EqualTo(1));
            return (T)MessageCodec.DecodePayload(payloads[0]);
        }

        [Test]
        public void JoinRequest_RoundTrips()
        {
            JoinRequest decoded = RoundTrip(new JoinRequest("Ñandú"));
            Assert.That(decoded.Nickname, Is.EqualTo("Ñandú"));
            Assert.That(decoded.ProtocolVersion, Is.EqualTo(ProtocolConstants.Version));
        }

        [Test]
        public void Welcome_RoundTripsWithOtherPlayers()
        {
            var welcome = new Welcome(new PlayerInfo(3, 1, "Ana"), new[] { new PlayerInfo(1, 0, "Luis"), new PlayerInfo(2, 2, "Eva") });
            Welcome decoded = RoundTrip(welcome);

            Assert.That(decoded.You.Id, Is.EqualTo(3));
            Assert.That(decoded.You.Slot, Is.EqualTo(1));
            Assert.That(decoded.Others.Length, Is.EqualTo(2));
            Assert.That(decoded.Others[1].Nickname, Is.EqualTo("Eva"));
        }

        [Test]
        public void CarState_RoundTripsExactly()
        {
            var state = new CarState
            {
                PlayerId = 7,
                Position = new NetVector3(1.2345678f, -0.5f, 1e6f),
                Rotation = new NetQuaternion(0.1f, 0.2f, 0.3f, 0.927f),
                WaypointsPassed = 513,
                Lap = 2,
                Finished = true,
            };
            CarState decoded = RoundTrip(state);

            Assert.That(decoded.PlayerId, Is.EqualTo(7));
            Assert.That(decoded.Position.X, Is.EqualTo(1.2345678f));
            Assert.That(decoded.Position.Z, Is.EqualTo(1e6f));
            Assert.That(decoded.Rotation.W, Is.EqualTo(0.927f));
            Assert.That(decoded.WaypointsPassed, Is.EqualTo(513));
            Assert.That(decoded.Lap, Is.EqualTo(2));
            Assert.That(decoded.Finished, Is.True);
        }

        [Test]
        public void PlayerLeftAndRejected_RoundTrip()
        {
            Assert.That(RoundTrip(new PlayerLeft(9)).PlayerId, Is.EqualTo(9));
            Assert.That(RoundTrip(new Rejected("full")).Reason, Is.EqualTo("full"));
        }

        [Test]
        public void UnknownType_Throws()
        {
            Assert.Throws<ProtocolException>(() => MessageCodec.DecodePayload(new byte[] { 200 }));
        }

        [Test]
        public void TruncatedPayload_Throws()
        {
            byte[] payload = MessageCodec.EncodePayload(new CarState());
            Array.Resize(ref payload, payload.Length - 3);
            Assert.Throws<ProtocolException>(() => MessageCodec.DecodePayload(payload));
        }

        [Test]
        public void TrailingBytes_Throw()
        {
            byte[] payload = MessageCodec.EncodePayload(new PlayerLeft(1));
            Array.Resize(ref payload, payload.Length + 1);
            Assert.Throws<ProtocolException>(() => MessageCodec.DecodePayload(payload));
        }

        [Test]
        public void NaNPosition_IsRejected()
        {
            var state = new CarState { Position = new NetVector3(float.NaN, 0, 0), Rotation = NetQuaternion.Identity };
            byte[] payload = MessageCodec.EncodePayload(state);
            Assert.Throws<ProtocolException>(() => MessageCodec.DecodePayload(payload));
        }

        [Test]
        public void InvalidUtf8_IsRejected()
        {
            // JoinRequest, version 1, string of length 2 with an invalid UTF-8 sequence.
            byte[] payload = { (byte)MessageType.JoinRequest, 1, 2, 0xC3, 0x28 };
            Assert.Throws<ProtocolException>(() => MessageCodec.DecodePayload(payload));
        }
    }

    public class FrameReaderTests
    {
        static byte[] Frames(params Message[] messages)
        {
            var all = new List<byte>();
            foreach (Message message in messages)
                all.AddRange(MessageCodec.EncodeFrame(message));
            return all.ToArray();
        }

        [Test]
        public void ByteByByte_ProducesOneFrameAtTheEnd()
        {
            byte[] data = Frames(new JoinRequest("Driver"));
            var reader = new FrameReader();
            int produced = 0;
            for (int i = 0; i < data.Length; i++)
            {
                produced += reader.Feed(data, i, 1).Count;
                if (i < data.Length - 1)
                    Assert.That(produced, Is.Zero, "No frame before its last byte arrives.");
            }
            Assert.That(produced, Is.EqualTo(1));
            Assert.That(reader.BufferedBytes, Is.Zero);
        }

        [Test]
        public void SeveralFramesInOneRead_AreAllReturnedInOrder()
        {
            byte[] data = Frames(new PlayerLeft(1), new PlayerLeft(2), new PlayerLeft(3));
            List<byte[]> payloads = new FrameReader().Feed(data, 0, data.Length);

            Assert.That(payloads.Count, Is.EqualTo(3));
            Assert.That(((PlayerLeft)MessageCodec.DecodePayload(payloads[2])).PlayerId, Is.EqualTo(3));
        }

        [Test]
        public void LargeBatchOfFrames_IsHandledInOneCall()
        {
            var messages = new Message[200];
            for (int i = 0; i < messages.Length; i++)
                messages[i] = new CarState { PlayerId = (byte)i, Rotation = NetQuaternion.Identity };
            byte[] data = Frames(messages);

            Assert.That(new FrameReader().Feed(data, 0, data.Length).Count, Is.EqualTo(200));
        }

        [Test]
        public void SplitAcrossReads_KeepsTheRemainder()
        {
            byte[] data = Frames(new PlayerLeft(1), new PlayerLeft(2));
            var reader = new FrameReader();

            Assert.That(reader.Feed(data, 0, 5).Count, Is.EqualTo(1));
            Assert.That(reader.BufferedBytes, Is.EqualTo(1));
            Assert.That(reader.Feed(data, 5, data.Length - 5).Count, Is.EqualTo(1));
        }

        [Test]
        public void OversizedHeader_IsRejectedImmediately()
        {
            int size = ProtocolConstants.MaxPayloadSize + 1;
            byte[] header = { (byte)(size & 0xFF), (byte)(size >> 8) };
            Assert.Throws<ProtocolException>(() => new FrameReader().Feed(header, 0, header.Length));
        }

        [Test]
        public void EmptyFrame_IsRejected()
        {
            byte[] header = { 0, 0 };
            Assert.Throws<ProtocolException>(() => new FrameReader().Feed(header, 0, header.Length));
        }

        [Test]
        public void EncodingAnOversizedMessage_Throws()
        {
            var welcome = new Welcome(new PlayerInfo(0, 0, "a"), new PlayerInfo[200]);
            for (int i = 0; i < welcome.Others.Length; i++)
                welcome.Others[i] = new PlayerInfo((byte)i, 0, new string('x', 16));
            Assert.Throws<ProtocolException>(() => MessageCodec.EncodeFrame(welcome));
        }
    }

    public class NicknameTests
    {
        [TestCase(null, "Player")]
        [TestCase("   ", "Player")]
        [TestCase("  Ana  ", "Ana")]
        [TestCase("A\tB\n\nC", "A B C")]
        [TestCase("ThisNameIsWayTooLongForTheBoard", "ThisNameIsWayToo")]
        [TestCase("Bad\u0000Name", "BadName")]
        public void Sanitize(string raw, string expected)
        {
            Assert.That(Nickname.Sanitize(raw), Is.EqualTo(expected));
        }

        [Test]
        public void MakeUnique_AddsANumberWhenTaken()
        {
            Assert.That(Nickname.MakeUnique("Ana", new[] { "Luis" }), Is.EqualTo("Ana"));
            Assert.That(Nickname.MakeUnique("Ana", new[] { "ana" }), Is.EqualTo("Ana 2"));
            Assert.That(Nickname.MakeUnique("Ana", new[] { "Ana", "Ana 2" }), Is.EqualTo("Ana 3"));
        }

        [Test]
        public void MakeUnique_StaysWithinTheLengthLimit()
        {
            string longName = new string('x', ProtocolConstants.MaxNicknameLength);
            string unique = Nickname.MakeUnique(longName, new[] { longName });
            Assert.That(unique.Length, Is.LessThanOrEqualTo(ProtocolConstants.MaxNicknameLength));
            Assert.That(unique, Does.EndWith(" 2"));
        }
    }
}
