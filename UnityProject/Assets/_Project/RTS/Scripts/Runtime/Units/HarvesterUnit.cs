using UnityEngine;
using UnityEngine.Serialization;
using UnityLab.RTS.Army;
using UnityLab.RTS.Core;
using UnityLab.RTS.World;

namespace UnityLab.RTS.Units
{
    /// <summary>
    /// Worker that gathers stone or wood. Its cycle: walk to the resource → get a spot (or queue) →
    /// harvest until the bag is full → carry it back to its base → return to the same resource.
    /// </summary>
    public class HarvesterUnit : Unit
    {
        public enum HarvestState
        {
            None,
            GoingToResource,
            GoingToSpot,
            Waiting,
            Harvesting,
            ReturningToBase,
        }

        [Header("Harvesting")]
        [FormerlySerializedAs("harvestAmountPerChop")]
        [SerializeField, Min(1)] int amountPerHit = 1;
        [FormerlySerializedAs("storage_max")]
        [SerializeField, Min(1)] int capacity = 10;
        [Tooltip("Seconds between hits.")]
        [FormerlySerializedAs("harvestRate")]
        [SerializeField] float harvestInterval = 1f;
        [Tooltip("Distance to the base's edge at which the load is delivered.")]
        [SerializeField] float deliveryDistance = 2f;

        ResourceNode resource;
        float nextHarvestTime;

        public HarvestState Harvest { get; private set; } = HarvestState.None;
        public int CarriedStone { get; private set; }
        public int CarriedWood { get; private set; }
        public int Carried => CarriedStone + CarriedWood;
        public override UnitKind Kind => UnitKind.Harvester;

        protected override void UpdateIdle()
        {
            // The agent can stop short of an unreachable point (the centre of a resource or a
            // building), so arrival is also checked here, not only while walking.
            switch (Harvest)
            {
                case HarvestState.GoingToSpot:
                    Harvest = HarvestState.Harvesting;
                    break;
                case HarvestState.GoingToResource:
                    ArriveAtResource(force: true);
                    return;
                case HarvestState.ReturningToBase:
                    if (Home != null && IsAtBase())
                        Deliver();
                    else
                        ReturnToBase();
                    return;
            }
            if (Harvest != HarvestState.Harvesting)
                return;

            if (resource == null || resource.IsDepleted || Carried >= capacity)
            {
                ReturnToBase();
                return;
            }

            if (Time.time >= nextHarvestTime)
            {
                nextHarvestTime = Time.time + harvestInterval;
                // Store what the node actually gave, which is less than asked when it runs out.
                int taken = resource.Take(Mathf.Min(amountPerHit, capacity - Carried));
                if (resource.Type == ResourceType.Stone)
                    CarriedStone += taken;
                else
                    CarriedWood += taken;
            }
        }

        protected override void UpdateGoingTo()
        {
            base.UpdateGoingTo();
            switch (Harvest)
            {
                case HarvestState.GoingToResource:
                    ArriveAtResource(force: false);
                    break;
                case HarvestState.ReturningToBase:
                    if (Home != null && IsAtBase())
                        Deliver();
                    break;
            }
        }

        void ArriveAtResource(bool force)
        {
            if (resource == null || resource.IsDepleted)
            {
                resource = null;
                Harvest = HarvestState.None;
                return;
            }
            bool near = (resource.transform.position - transform.position).sqrMagnitude < resource.ArrivalDistanceSquared;
            if (!near && !force)
                return;

            if (resource.RequestSpot(this, out Vector3 spot))
            {
                Harvest = HarvestState.GoingToSpot;
                GoTo(spot);
            }
            else
            {
                SetIdle();
                Harvest = HarvestState.Waiting;
            }
        }

        bool IsAtBase()
        {
            Vector3 closest = Home.ClosestPoint(transform.position);
            return (closest - transform.position).sqrMagnitude <= deliveryDistance * deliveryDistance;
        }

        void Deliver()
        {
            Home.Deposit(CarriedStone, CarriedWood);
            CarriedStone = 0;
            CarriedWood = 0;

            if (resource != null && !resource.IsDepleted)
            {
                Harvest = HarvestState.GoingToResource;
                GoTo(resource.transform.position);
            }
            else
            {
                resource = null;
                Harvest = HarvestState.None;
                SetIdle();
            }
        }

        void ReturnToBase()
        {
            LeaveResource();
            if (Home == null)
            {
                Harvest = HarvestState.None;
                SetIdle();
                return;
            }
            Harvest = HarvestState.ReturningToBase;
            GoTo(Home.ClosestPoint(transform.position));
        }

        /// <summary>Called by the resource when a queued harvester gets a spot.</summary>
        public void OnSpotAssigned(ResourceNode node, Vector3 spot)
        {
            resource = node;
            Harvest = HarvestState.GoingToSpot;
            GoTo(spot);
        }

        /// <summary>Sends the harvester to work on a resource.</summary>
        public void OrderHarvest(ResourceNode node)
        {
            if (node == null || node.IsDepleted)
                return;
            if (node != resource)
                LeaveResource();
            resource = node;

            if (Carried >= capacity)
            {
                ReturnToBase();
                return;
            }
            Harvest = HarvestState.GoingToResource;
            GoTo(node.transform.position);
        }

        public override void OrderMove(Vector3 position)
        {
            LeaveResource();
            Harvest = HarvestState.None;
            base.OrderMove(position);
        }

        public override void OrderInteract(Transform target)
        {
            if (target.TryGetComponent(out ResourceNode node))
            {
                OrderHarvest(node);
                return;
            }
            if (target.TryGetComponent(out ArmyBase armyBase) && armyBase == Home)
            {
                ReturnToBase();
                return;
            }
            LeaveResource();
            Harvest = HarvestState.None;
            base.OrderInteract(target);
        }

        /// <summary>Gives up the spot or the queue place, so other harvesters can use it.</summary>
        void LeaveResource()
        {
            if (resource != null && (Harvest == HarvestState.Harvesting || Harvest == HarvestState.GoingToSpot || Harvest == HarvestState.Waiting))
                resource.Leave(this);
        }

        protected override void OnDied()
        {
            LeaveResource();
            Harvest = HarvestState.None;
            base.OnDied();
        }

        protected override string DebugLabel => $"{State} / {Harvest} ({Carried}/{capacity})";
    }
}
