using System;

namespace UnityLab.Racing.Core
{
    /// <summary>
    /// Tracks one competitor through the circuit's checkpoints (waypoints).
    /// Waypoint 0 is the start/finish line and sits just ahead of the grid, so the first time it is
    /// crossed the race starts; crossing it again after every other waypoint completes a lap.
    /// Waypoints must be passed in order, which prevents shortcuts and driving backwards.
    /// </summary>
    public sealed class RaceProgress
    {
        public RaceProgress(int waypointCount, int totalLaps)
        {
            if (waypointCount < 2)
                throw new ArgumentOutOfRangeException(nameof(waypointCount), "A circuit needs at least two waypoints.");
            if (totalLaps < 1)
                throw new ArgumentOutOfRangeException(nameof(totalLaps), "A race needs at least one lap.");

            WaypointCount = waypointCount;
            TotalLaps = totalLaps;
        }

        public int WaypointCount { get; }
        public int TotalLaps { get; }

        /// <summary>Index of the waypoint the competitor has to cross next.</summary>
        public int NextWaypoint { get; private set; }

        /// <summary>Waypoints crossed in order since the start. Used to rank competitors.</summary>
        public int WaypointsPassed { get; private set; }

        public int CompletedLaps { get; private set; }

        /// <summary>Lap shown to the player: 1-based and never above the total.</summary>
        public int CurrentLap => Math.Min(CompletedLaps + 1, TotalLaps);

        public bool Finished { get; private set; }

        /// <summary>Race time at which the competitor finished, or null.</summary>
        public double? FinishTime { get; private set; }

        /// <summary>Registers that the competitor touched a waypoint.</summary>
        /// <returns>True if it was the expected waypoint and progress advanced.</returns>
        public bool RegisterWaypoint(int index, double raceTime)
        {
            if (Finished || index != NextWaypoint)
                return false;

            WaypointsPassed++;
            NextWaypoint = (index + 1) % WaypointCount;

            bool crossedFinishLine = index == 0 && WaypointsPassed > 1;
            if (crossedFinishLine)
            {
                CompletedLaps++;
                if (CompletedLaps == TotalLaps)
                {
                    Finished = true;
                    FinishTime = raceTime;
                }
            }
            return true;
        }

        /// <summary>Overwrites the state with values reported by a remote client.</summary>
        public void ApplyRemote(int waypointsPassed, bool finished, double raceTime)
        {
            WaypointsPassed = Math.Max(0, waypointsPassed);
            NextWaypoint = WaypointsPassed % WaypointCount;
            CompletedLaps = Math.Min(TotalLaps, Math.Max(0, (WaypointsPassed - 1) / WaypointCount));
            if (finished && !Finished)
                FinishTime = raceTime;
            Finished = finished;
        }
    }
}
