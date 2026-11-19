using System;
using System.Collections.Generic;

namespace UnityLab.RTS.Core
{
    /// <summary>Anything that can see and be seen on the battlefield's ground plane.</summary>
    public interface IVisionAgent
    {
        float X { get; }
        float Z { get; }
        float VisionRadius { get; }
    }

    /// <summary>
    /// Keeps the squared distance between every unit of army A and every unit of army B and reports
    /// when an enemy enters or leaves each unit's vision radius.
    ///
    /// Rows are units of army A and columns units of army B, so a pair is computed once per update
    /// for both sides and units never test allies. Before the exact distance, a cheap per-axis
    /// check discards pairs that cannot possibly see each other.
    /// </summary>
    public sealed class VisionMatrix<T> where T : class, IVisionAgent
    {
        /// <summary>Stored for pairs that are out of every range.</summary>
        public const float Far = float.MaxValue;

        readonly List<T> armyA = new List<T>();
        readonly List<T> armyB = new List<T>();
        readonly List<List<float>> distances = new List<List<float>>();

        /// <summary>Called with (observer, enemy) when the enemy comes into the observer's range.</summary>
        public event Action<T, T> EnemyEntered;

        /// <summary>Called with (observer, enemy) when the enemy leaves the range or is removed.</summary>
        public event Action<T, T> EnemyLeft;

        public IReadOnlyList<T> ArmyA => armyA;
        public IReadOnlyList<T> ArmyB => armyB;

        /// <summary>Squared distance between armyA[row] and armyB[column], or <see cref="Far"/>.</summary>
        public float GetSquaredDistance(int row, int column) => distances[row][column];

        /// <summary>Adds a unit to army 0 (A) or army 1 (B).</summary>
        public void Add(T unit, int army)
        {
            if (unit == null)
                throw new ArgumentNullException(nameof(unit));
            if (armyA.Contains(unit) || armyB.Contains(unit))
                return;

            if (army == 0)
            {
                armyA.Add(unit);
                var row = new List<float>(armyB.Count);
                for (int i = 0; i < armyB.Count; i++)
                    row.Add(Far);
                distances.Add(row);
            }
            else if (army == 1)
            {
                armyB.Add(unit);
                foreach (List<float> row in distances)
                    row.Add(Far);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(army), "Only armies 0 and 1 are supported.");
            }
        }

        /// <summary>Removes a unit; every enemy that was seeing it gets an <see cref="EnemyLeft"/>.</summary>
        public void Remove(T unit)
        {
            int row = armyA.IndexOf(unit);
            if (row >= 0)
            {
                for (int column = 0; column < armyB.Count; column++)
                    NotifyRemoval(unit, armyB[column], distances[row][column]);
                armyA.RemoveAt(row);
                distances.RemoveAt(row);
                return;
            }

            int col = armyB.IndexOf(unit);
            if (col >= 0)
            {
                for (int r = 0; r < armyA.Count; r++)
                {
                    NotifyRemoval(unit, armyA[r], distances[r][col]);
                    distances[r].RemoveAt(col);
                }
                armyB.RemoveAt(col);
            }
        }

        /// <summary>Removes every unit without raising events (used when a scene unloads).</summary>
        public void Clear()
        {
            armyA.Clear();
            armyB.Clear();
            distances.Clear();
        }

        /// <summary>Recomputes every pair. Call once per frame.</summary>
        public void Update()
        {
            for (int row = 0; row < armyA.Count; row++)
                for (int column = 0; column < armyB.Count; column++)
                    UpdatePair(row, column);
        }

        void UpdatePair(int row, int column)
        {
            T a = armyA[row];
            T b = armyB[column];
            float previous = distances[row][column];
            float reach = Math.Max(a.VisionRadius, b.VisionRadius);

            float dx = Math.Abs(a.X - b.X);
            float dz = Math.Abs(a.Z - b.Z);
            float current = dx <= reach && dz <= reach ? dx * dx + dz * dz : Far;
            distances[row][column] = current;

            Notify(a, b, previous, current);
            Notify(b, a, previous, current);
        }

        void Notify(T observer, T target, float previous, float current)
        {
            float range = observer.VisionRadius * observer.VisionRadius;
            bool sawBefore = previous <= range;
            bool seesNow = current <= range;
            if (!sawBefore && seesNow)
                EnemyEntered?.Invoke(observer, target);
            else if (sawBefore && !seesNow)
                EnemyLeft?.Invoke(observer, target);
        }

        void NotifyRemoval(T removed, T observer, float distance)
        {
            if (distance <= observer.VisionRadius * observer.VisionRadius)
                EnemyLeft?.Invoke(observer, removed);
        }
    }
}
