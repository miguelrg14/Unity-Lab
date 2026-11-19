using UnityEngine;
using UnityLab.Racing.Core;

namespace UnityLab.Racing.Race
{
    /// <summary>
    /// A car taking part in the race: its name, its progress around the circuit and its position.
    /// Local cars advance their progress by touching waypoints; remote cars receive it over the network.
    /// </summary>
    public class Competitor : MonoBehaviour
    {
        [SerializeField] string nickname = "Player";

        public string Nickname
        {
            get => nickname;
            set => nickname = value;
        }

        /// <summary>1-based place in the race, updated by the circuit.</summary>
        public int Position { get; set; }

        public RaceProgress Progress { get; private set; }
        public CircuitManager Circuit { get; private set; }

        /// <summary>Remote cars get their progress from their owner instead of from triggers.</summary>
        public bool IsRemote { get; set; }

        /// <summary>The car the person at this computer is driving.</summary>
        public bool IsLocalPlayer { get; set; }

        void Awake()
        {
            IsLocalPlayer = TryGetComponent(out Cars.PlayerCarInput _);
            if (IsLocalPlayer)
                nickname = Shared.GameSession.Nickname;
        }

        void Start()
        {
            // Competitors placed in the scene join the race by themselves; spawned ones are
            // registered explicitly by whoever spawns them.
            if (Circuit == null)
            {
                CircuitManager circuit = FindAnyObjectByType<CircuitManager>();
                if (circuit != null)
                    circuit.Register(this);
            }
        }

        internal void JoinRace(CircuitManager circuit, RaceProgress progress)
        {
            Circuit = circuit;
            Progress = progress;
        }

        void OnTriggerEnter(Collider other)
        {
            if (IsRemote || Circuit == null || !other.TryGetComponent(out Waypoint waypoint))
                return;
            Circuit.ReportWaypoint(this, waypoint.Index);
        }

        void OnDestroy()
        {
            if (Circuit != null)
                Circuit.Unregister(this);
        }
    }
}
