using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityLab.Racing.Cars;
using UnityLab.Racing.Online;
using UnityLab.Racing.Protocol;
using UnityLab.Racing.Race;
using UnityLab.Shared;

namespace UnityLab.Tests.PlayMode
{
    public class RacingPlayModeTests
    {
        [SetUp]
        public void SetUp() => GameSession.Autopilot = true;

        [TearDown]
        public void TearDown()
        {
            GameSession.Autopilot = false;
            Time.timeScale = 1f;
        }

        static IEnumerator Load(string scene)
        {
            yield return SceneManager.LoadSceneAsync(scene);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SinglePlayer_HasACircuitAndFourCompetitors()
        {
            yield return Load(Scenes.RacingSinglePlayer);
            CircuitManager circuit = Object.FindAnyObjectByType<CircuitManager>();

            Assert.That(circuit, Is.Not.Null);
            Assert.That(circuit.WaypointCount, Is.GreaterThan(10));
            Assert.That(circuit.Spawnpoints.Count, Is.EqualTo(4), "The grid has a slot for each car.");
            Assert.That(circuit.Standings.Count, Is.EqualTo(4));
            Assert.That(circuit.Standings.Count(c => c.IsLocalPlayer), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SinglePlayer_EveryCarDrivesThroughTheWaypoints()
        {
            yield return Load(Scenes.RacingSinglePlayer);
            CircuitManager circuit = Object.FindAnyObjectByType<CircuitManager>();
            Time.timeScale = 2f;

            yield return new WaitForSeconds(20f);

            foreach (Competitor competitor in circuit.Standings)
                Assert.That(competitor.Progress.WaypointsPassed, Is.GreaterThanOrEqualTo(3), competitor.Nickname + " is stuck.");
            Assert.That(circuit.Standings.Select(c => c.Position), Is.EquivalentTo(new[] { 1, 2, 3, 4 }));
        }

        [UnityTest]
        public IEnumerator Car_ResetPutsItBackUprightOnTheTrack()
        {
            yield return Load(Scenes.RacingSinglePlayer);
            CarController car = Object.FindObjectsByType<CarController>(FindObjectsSortMode.None).First(c => c.GetComponent<PlayerCarInput>() != null);

            car.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
            car.ResetCar();
            yield return new WaitForFixedUpdate();

            Assert.That(Vector3.Dot(car.transform.up, Vector3.up), Is.GreaterThan(0.95f));
        }

        /// <summary>
        /// The online scene against a scripted server built from the shared protocol classes:
        /// checks the handshake, the grid slot, spawning remote cars and sending our own state.
        /// </summary>
        [UnityTest]
        public IEnumerator Online_JoinsSpawnsRemoteCarsAndSendsState()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var fake = new FakeServer(listener);
            var serverThread = new Thread(fake.Run) { IsBackground = true };
            serverThread.Start();

            GameSession.ServerHost = "127.0.0.1";
            GameSession.ServerPort = port;
            GameSession.Nickname = "Tester";
            try
            {
                yield return Load(Scenes.RacingOnline);
                OnlineRaceManager online = Object.FindAnyObjectByType<OnlineRaceManager>();
                Assert.That(online, Is.Not.Null);

                float deadline = Time.realtimeSinceStartup + 10f;
                while ((!online.IsConnected || online.RemotePlayerCount == 0) && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(online.IsConnected, Is.True, "Connected and welcomed.");
                Assert.That(fake.JoinedNickname, Is.EqualTo("Tester"));
                Assert.That(online.RemotePlayerCount, Is.EqualTo(1));

                CircuitManager circuit = Object.FindAnyObjectByType<CircuitManager>();
                Competitor local = circuit.Standings.First(c => c.IsLocalPlayer);
                Assert.That(Vector3.Distance(local.transform.position, circuit.GetSpawnpoint(1).position), Is.LessThan(3f),
                    "The local car starts on the slot the server assigned.");

                // The remote car glides to the position the server reported.
                fake.SendRemoteState(new Vector3(5f, 1f, 5f));
                yield return new WaitForSeconds(1.5f);
                RemoteCar remote = Object.FindAnyObjectByType<RemoteCar>();
                Assert.That(Vector3.Distance(remote.transform.position, new Vector3(5f, 1f, 5f)), Is.LessThan(0.5f));
                Assert.That(remote.GetComponent<Competitor>().Nickname, Is.EqualTo("Rival"));

                deadline = Time.realtimeSinceStartup + 3f;
                while (fake.StatesReceived == 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(fake.StatesReceived, Is.GreaterThan(0), "The local car's state is sent to the server.");
            }
            finally
            {
                fake.Stop();
                listener.Stop();
            }
        }

        sealed class FakeServer
        {
            readonly TcpListener listener;
            NetworkStream stream;
            volatile bool stopped;
            public volatile string JoinedNickname;
            public volatile int StatesReceived;

            public FakeServer(TcpListener listener) => this.listener = listener;

            public void Run()
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    stream = client.GetStream();
                    var reader = new FrameReader();
                    var buffer = new byte[4096];
                    while (!stopped)
                    {
                        int read = stream.Read(buffer, 0, buffer.Length);
                        if (read == 0)
                            return;
                        foreach (byte[] payload in reader.Feed(buffer, 0, read))
                        {
                            Message message = MessageCodec.DecodePayload(payload);
                            if (message is JoinRequest join)
                            {
                                JoinedNickname = join.Nickname;
                                Write(new Welcome(new PlayerInfo(7, 1, join.Nickname), new PlayerInfo[0]));
                                Write(new PlayerJoined(new PlayerInfo(8, 0, "Rival")));
                            }
                            else if (message is CarState)
                            {
                                StatesReceived++;
                            }
                        }
                    }
                }
                catch (System.Exception) when (stopped)
                {
                    // Listener or socket closed by the test.
                }
                catch (System.IO.IOException)
                {
                    // Client went away.
                }
            }

            public void SendRemoteState(Vector3 position) =>
                Write(new CarState { PlayerId = 8, Position = new NetVector3(position.x, position.y, position.z), Rotation = NetQuaternion.Identity, WaypointsPassed = 1, Lap = 1 });

            void Write(Message message)
            {
                byte[] frame = MessageCodec.EncodeFrame(message);
                lock (this)
                    stream.Write(frame, 0, frame.Length);
            }

            public void Stop()
            {
                stopped = true;
                stream?.Dispose();
            }
        }
    }
}
