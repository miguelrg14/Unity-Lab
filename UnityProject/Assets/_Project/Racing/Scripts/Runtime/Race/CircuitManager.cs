using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityLab.Racing.Core;

namespace UnityLab.Racing.Race
{
    /// <summary>
    /// Owns the race: numbers the waypoints (its children, in hierarchy order), registers the
    /// competitors, forwards waypoint crossings to their <see cref="RaceProgress"/> and keeps the
    /// standings up to date.
    /// </summary>
    public class CircuitManager : MonoBehaviour
    {
        [FormerlySerializedAs("maxLap")]
        [SerializeField, Min(1)] int laps = 2;
        [Tooltip("Starting grid positions, front to back.")]
        [SerializeField] List<Transform> spawnpoints = new List<Transform>();
        [Tooltip("Seconds between standings refreshes (the distance tie-break changes constantly).")]
        [SerializeField] float standingsInterval = 0.25f;

        readonly List<Waypoint> waypoints = new List<Waypoint>();
        readonly List<Competitor> competitors = new List<Competitor>();
        List<Competitor> standings = new List<Competitor>();
        float raceStartTime;
        float nextStandingsUpdate;

        public int Laps => laps;
        public int WaypointCount => waypoints.Count;
        public IReadOnlyList<Competitor> Standings => standings;
        public IReadOnlyList<Transform> Spawnpoints => spawnpoints;
        public float RaceTime => Time.time - raceStartTime;

        /// <summary>Raised whenever the order of the standings may have changed.</summary>
        public event Action StandingsChanged;

        /// <summary>Raised once per competitor when it crosses the finish line.</summary>
        public event Action<Competitor> CompetitorFinished;

        void Awake()
        {
            foreach (Transform child in transform)
            {
                if (child.TryGetComponent(out Waypoint waypoint))
                {
                    waypoint.Index = waypoints.Count;
                    waypoints.Add(waypoint);
                }
            }
            if (waypoints.Count < 2)
                Debug.LogError($"{name}: a circuit needs at least two Waypoint children.", this);
            raceStartTime = Time.time;
        }

        public void Register(Competitor competitor)
        {
            if (competitors.Contains(competitor))
                return;
            competitor.JoinRace(this, new RaceProgress(waypoints.Count, laps));
            competitors.Add(competitor);
            RefreshStandings();
        }

        public void Unregister(Competitor competitor)
        {
            if (competitors.Remove(competitor))
                RefreshStandings();
        }

        public void ReportWaypoint(Competitor competitor, int waypointIndex)
        {
            RaceProgress progress = competitor.Progress;
            bool wasFinished = progress.Finished;
            if (!progress.RegisterWaypoint(waypointIndex, RaceTime))
                return;

            RefreshStandings();
            if (!wasFinished && progress.Finished)
                CompetitorFinished?.Invoke(competitor);
        }

        /// <summary>Applies the progress a remote player reported.</summary>
        public void ApplyRemoteProgress(Competitor competitor, int waypointsPassed, bool finished)
        {
            bool wasFinished = competitor.Progress.Finished;
            competitor.Progress.ApplyRemote(waypointsPassed, finished, RaceTime);
            if (!wasFinished && finished)
                CompetitorFinished?.Invoke(competitor);
        }

        public Vector3 GetWaypointPosition(int index) => waypoints[index].transform.position;

        public Transform GetSpawnpoint(int slot) =>
            spawnpoints.Count == 0 ? transform : spawnpoints[Mathf.Clamp(slot, 0, spawnpoints.Count - 1)];

        void Update()
        {
            if (Time.time >= nextStandingsUpdate)
                RefreshStandings();
        }

        void RefreshStandings()
        {
            nextStandingsUpdate = Time.time + standingsInterval;
            competitors.RemoveAll(c => c == null);
            standings = RaceStandings.Rank(competitors, c => c.Progress, DistanceToNextWaypoint);
            for (int i = 0; i < standings.Count; i++)
                standings[i].Position = i + 1;
            StandingsChanged?.Invoke();
        }

        float DistanceToNextWaypoint(Competitor competitor) =>
            Vector3.Distance(competitor.transform.position, GetWaypointPosition(competitor.Progress.NextWaypoint));

        void OnDrawGizmos()
        {
            // Draw the racing line through the waypoints, in order.
            Gizmos.color = Color.cyan;
            Transform previous = null;
            Transform first = null;
            foreach (Transform child in transform)
            {
                if (!child.TryGetComponent(out Waypoint _))
                    continue;
                if (previous != null)
                    Gizmos.DrawLine(previous.position, child.position);
                else
                    first = child;
                previous = child;
            }
            if (previous != null && first != null)
                Gizmos.DrawLine(previous.position, first.position);
        }
    }
}
