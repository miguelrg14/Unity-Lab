using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityLab.Racing.Cars;
using UnityLab.Racing.Protocol;
using UnityLab.Racing.Race;
using UnityLab.Shared;

namespace UnityLab.Racing.Online
{
    /// <summary>
    /// Connects to the race server with the settings chosen in the menu, places the local car on its
    /// starting slot, spawns a <see cref="RemoteCar"/> for every other player and sends the local
    /// car's state at a fixed rate. All networking runs on <see cref="RaceClient"/>'s thread; this
    /// component only consumes its queue on Unity's main thread.
    /// </summary>
    public class OnlineRaceManager : MonoBehaviour
    {
        [FormerlySerializedAs("circuitMng")]
        [SerializeField] CircuitManager circuit;
        [Tooltip("The local player's car.")]
        [FormerlySerializedAs("competitorInfo")]
        [SerializeField] Competitor localCompetitor;
        [FormerlySerializedAs("onlineCarPrefab")]
        [SerializeField] RemoteCar remoteCarPrefab;
        [Tooltip("Parent of the spawned remote cars.")]
        [FormerlySerializedAs("entitiesParent")]
        [SerializeField] Transform competitorsRoot;
        [SerializeField] float sendRate = 20f;
        [SerializeField] float connectTimeout = 5f;

        readonly Dictionary<byte, RemoteCar> remoteCars = new Dictionary<byte, RemoteCar>();
        RaceClient client;
        float nextSend;
        TMP_Text statusText;
        GameObject statusPanel;

        public bool IsConnected => client != null && client.State == ConnectionState.Connected && LocalPlayerId.HasValue;
        public byte? LocalPlayerId { get; private set; }
        public int RemotePlayerCount => remoteCars.Count;

        void Start()
        {
            BuildStatusPanel();
            if (!GameSession.SupportsOnlinePlay)
            {
                ShowStatus("Online races need the desktop build:\nbrowsers cannot open TCP connections.", error: true);
                return;
            }
            StartCoroutine(Connect());
        }

        IEnumerator Connect()
        {
            string host = GameSession.ServerHost;
            int port = GameSession.ServerPort;
            ShowStatus($"Connecting to {host}:{port}...", error: false);

            // The local car waits on the grid until the server assigns a slot.
            SetLocalCarFrozen(true);
            client = new RaceClient();
            Task<bool> connect = client.ConnectAsync(host, port, GameSession.Nickname, TimeSpan.FromSeconds(connectTimeout));
            while (!connect.IsCompleted)
                yield return null;

            if (!connect.Result)
                ShowStatus($"Could not connect to {host}:{port}.\n{client.LastError}", error: true);
        }

        void Update()
        {
            if (client == null)
                return;

            while (client.TryReceive(out Message message))
                Handle(message);

            if (LocalPlayerId.HasValue && client.State == ConnectionState.Disconnected)
            {
                ShowStatus("Disconnected from the server.\n" + client.LastError, error: true);
                LocalPlayerId = null;
            }

            if (IsConnected && Time.time >= nextSend)
            {
                nextSend = Time.time + 1f / sendRate;
                SendLocalState();
            }
        }

        void Handle(Message message)
        {
            switch (message)
            {
                case Welcome welcome:
                    LocalPlayerId = welcome.You.Id;
                    localCompetitor.Nickname = welcome.You.Nickname;
                    PlaceOnGrid(localCompetitor.transform, welcome.You.Slot);
                    SetLocalCarFrozen(false);
                    foreach (PlayerInfo other in welcome.Others)
                        SpawnRemote(other);
                    HideStatus();
                    break;
                case PlayerJoined joined:
                    SpawnRemote(joined.Player);
                    break;
                case PlayerLeft left:
                    if (remoteCars.TryGetValue(left.PlayerId, out RemoteCar car))
                    {
                        remoteCars.Remove(left.PlayerId);
                        Destroy(car.gameObject);
                    }
                    break;
                case CarState state:
                    if (remoteCars.TryGetValue(state.PlayerId, out RemoteCar remote))
                        remote.ApplyState(state);
                    break;
                case Rejected rejected:
                    ShowStatus("The server refused the connection:\n" + rejected.Reason, error: true);
                    break;
            }
        }

        void SpawnRemote(PlayerInfo player)
        {
            if (remoteCars.ContainsKey(player.Id))
                return;
            Transform spawn = circuit.GetSpawnpoint(player.Slot);
            RemoteCar car = Instantiate(remoteCarPrefab, spawn.position, spawn.rotation, competitorsRoot);
            car.name = "Remote car - " + player.Nickname;
            car.Initialize(circuit, player.Nickname);
            remoteCars[player.Id] = car;
        }

        void SendLocalState()
        {
            Transform car = localCompetitor.transform;
            Vector3 p = car.position;
            Quaternion r = car.rotation;
            client.Send(new CarState
            {
                Position = new NetVector3(p.x, p.y, p.z),
                Rotation = new NetQuaternion(r.x, r.y, r.z, r.w),
                WaypointsPassed = (ushort)Mathf.Min(localCompetitor.Progress.WaypointsPassed, ushort.MaxValue),
                Lap = (byte)localCompetitor.Progress.CurrentLap,
                Finished = localCompetitor.Progress.Finished,
            });
        }

        void PlaceOnGrid(Transform car, int slot)
        {
            Transform spawn = circuit.GetSpawnpoint(slot);
            car.SetPositionAndRotation(spawn.position, spawn.rotation);
            if (car.TryGetComponent(out Rigidbody body))
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
        }

        void SetLocalCarFrozen(bool frozen)
        {
            if (localCompetitor.TryGetComponent(out Rigidbody body))
                body.isKinematic = frozen;
            if (localCompetitor.TryGetComponent(out PlayerCarInput input))
                input.enabled = !frozen;
        }

        void BuildStatusPanel()
        {
            Canvas canvas = UiFactory.CreateCanvas("Connection Status", 40);
            RectTransform card = UiFactory.Panel(canvas.transform, "Status Card", UiTheme.Panel);
            UiFactory.Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(760, 0), Vector2.zero);
            card.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            UiFactory.Vertical(card, 20, 40);
            statusText = UiFactory.Label(card, string.Empty, 30, UiTheme.Text, TextAlignmentOptions.Center);
            UiFactory.Button(card, "Back to menu", SceneNavigator.BackToMenu, primary: false);
            statusPanel = canvas.gameObject;
        }

        void ShowStatus(string text, bool error)
        {
            statusText.text = text;
            statusText.color = error ? UiTheme.Danger : UiTheme.Text;
            statusPanel.SetActive(true);
            if (error)
                Debug.LogWarning("[Online] " + text.Replace('\n', ' '));
        }

        void HideStatus() => statusPanel.SetActive(false);

        void OnDestroy() => client?.Dispose();
    }
}
