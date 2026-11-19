using System;
using System.Collections.Generic;

namespace UnityLab.RTS.Core
{
    /// <summary>
    /// Fixed number of places around a resource where harvesters can work. Harvesters that find
    /// every spot taken wait in a first-come, first-served queue and are handed the next free spot.
    /// </summary>
    public sealed class HarvestSpots<T> where T : class
    {
        readonly T[] occupants;
        readonly List<T> queue = new List<T>();

        public HarvestSpots(int count)
        {
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "A resource needs at least one spot.");
            occupants = new T[count];
        }

        public int Count => occupants.Length;
        public int QueueLength => queue.Count;

        public bool IsTaken(int spot) => occupants[spot] != null;

        /// <summary>Gives the harvester a free spot, or queues it.</summary>
        /// <returns>The spot index, or -1 if the harvester has to wait.</returns>
        public int Request(T harvester)
        {
            int existing = Array.IndexOf(occupants, harvester);
            if (existing >= 0)
                return existing;

            for (int i = 0; i < occupants.Length; i++)
            {
                if (occupants[i] == null)
                {
                    occupants[i] = harvester;
                    queue.Remove(harvester);
                    return i;
                }
            }

            if (!queue.Contains(harvester))
                queue.Add(harvester);
            return -1;
        }

        /// <summary>Frees the harvester's spot or removes it from the queue.</summary>
        /// <param name="next">The queued harvester that now owns the freed spot, if any.</param>
        /// <returns>The freed spot index, or -1 if the harvester was only queued or unknown.</returns>
        public int Leave(T harvester, out T next)
        {
            next = null;
            if (queue.Remove(harvester))
                return -1;

            int spot = Array.IndexOf(occupants, harvester);
            if (spot < 0)
                return -1;

            occupants[spot] = null;
            if (queue.Count > 0)
            {
                next = queue[0];
                queue.RemoveAt(0);
                occupants[spot] = next;
            }
            return spot;
        }
    }

    /// <summary>A finite amount of a resource that can be taken until it runs out.</summary>
    public sealed class ResourceStock
    {
        public ResourceStock(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            Remaining = amount;
        }

        public int Remaining { get; private set; }
        public bool IsDepleted => Remaining == 0;

        /// <summary>Takes up to <paramref name="amount"/> and returns what was actually taken.</summary>
        public int Take(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            int taken = Math.Min(amount, Remaining);
            Remaining -= taken;
            return taken;
        }
    }
}
