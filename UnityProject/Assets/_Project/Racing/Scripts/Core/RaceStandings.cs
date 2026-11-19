using System;
using System.Collections.Generic;

namespace UnityLab.Racing.Core
{
    /// <summary>
    /// Orders competitors for the leaderboard:
    /// 1. those who finished, by finish time;
    /// 2. then by waypoints crossed (laps are included in that count);
    /// 3. ties broken by who is closer to their next waypoint.
    /// </summary>
    public static class RaceStandings
    {
        public static List<T> Rank<T>(IEnumerable<T> competitors, Func<T, RaceProgress> progressOf, Func<T, float> distanceToNextWaypoint)
        {
            var ranked = new List<T>(competitors);
            // Stable sort so competitors that are exactly level keep their previous order.
            var indexed = new List<KeyValuePair<int, T>>(ranked.Count);
            for (int i = 0; i < ranked.Count; i++)
                indexed.Add(new KeyValuePair<int, T>(i, ranked[i]));

            indexed.Sort((a, b) =>
            {
                int result = Compare(progressOf(a.Value), progressOf(b.Value), distanceToNextWaypoint(a.Value), distanceToNextWaypoint(b.Value));
                return result != 0 ? result : a.Key.CompareTo(b.Key);
            });

            for (int i = 0; i < indexed.Count; i++)
                ranked[i] = indexed[i].Value;
            return ranked;
        }

        /// <summary>Negative when <paramref name="a"/> is ahead of <paramref name="b"/>.</summary>
        public static int Compare(RaceProgress a, RaceProgress b, float distanceA, float distanceB)
        {
            if (a.Finished != b.Finished)
                return a.Finished ? -1 : 1;
            if (a.Finished)
                return Nullable.Compare(a.FinishTime, b.FinishTime);
            if (a.WaypointsPassed != b.WaypointsPassed)
                return b.WaypointsPassed.CompareTo(a.WaypointsPassed);
            return distanceA.CompareTo(distanceB);
        }

        /// <summary>"1st", "2nd", "3rd", "4th"...</summary>
        public static string Ordinal(int position)
        {
            int lastTwo = position % 100;
            if (lastTwo >= 11 && lastTwo <= 13)
                return position + "th";
            switch (position % 10)
            {
                case 1: return position + "st";
                case 2: return position + "nd";
                case 3: return position + "rd";
                default: return position + "th";
            }
        }
    }
}
