using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityLab.RTS.Core;
using UnityLab.RTS.Selection;
using UnityLab.RTS.Units;

namespace UnityLab.RTS.Army
{
    /// <summary>
    /// An army's headquarters: trains units in exchange for resources and receives what the
    /// harvesters bring back. Units appear at its "SpawnPoint" child.
    /// </summary>
    [RequireComponent(typeof(TeamMember), typeof(Selectable))]
    public class ArmyBase : MonoBehaviour
    {
        [Serializable]
        public struct UnitCost
        {
            public int stone;
            public int wood;

            public UnitCost(int stone, int wood)
            {
                this.stone = stone;
                this.wood = wood;
            }

            public override string ToString() =>
                stone > 0 && wood > 0 ? $"{stone} stone + {wood} wood" : stone > 0 ? $"{stone} stone" : $"{wood} wood";
        }

        [Header("Unit prefabs")]
        [FormerlySerializedAs("baseUnitPrefab")]
        [SerializeField] GameObject meleeUnitPrefab;
        [SerializeField] GameObject artilleryUnitPrefab;
        [SerializeField] GameObject harvesterUnitPrefab;

        [Header("Economy")]
        [SerializeField] int startingStone = 120;
        [SerializeField] int startingWood = 60;
        [SerializeField] UnitCost meleeCost = new UnitCost(30, 0);
        [SerializeField] UnitCost artilleryCost = new UnitCost(40, 20);
        [SerializeField] UnitCost harvesterCost = new UnitCost(0, 20);

        Transform spawnPoint;
        Collider[] colliders;

        public TeamMember Team { get; private set; }
        public Selectable Selectable { get; private set; }
        public Treasury Treasury { get; private set; }

        void Awake()
        {
            Team = GetComponent<TeamMember>();
            Selectable = GetComponent<Selectable>();
            Treasury = new Treasury(startingStone, startingWood);
            colliders = GetComponentsInChildren<Collider>();
            spawnPoint = transform.Find("SpawnPoint");
            if (spawnPoint == null)
            {
                Debug.LogWarning($"{name} has no 'SpawnPoint' child; units will appear at the base's pivot.", this);
                spawnPoint = transform;
            }
        }

        public void SetTeamColor(Color color)
        {
            Team.Color = color;
            Selectable.SetTeamColor(color);
        }

        public UnitCost CostOf(UnitKind kind) =>
            kind == UnitKind.Melee ? meleeCost : kind == UnitKind.Artillery ? artilleryCost : harvesterCost;

        public bool CanAfford(UnitKind kind)
        {
            UnitCost cost = CostOf(kind);
            return PrefabOf(kind) != null && Treasury.CanAfford(cost.stone, cost.wood);
        }

        /// <summary>Spends the resources and spawns the unit.</summary>
        /// <returns>The new unit, or null when the army cannot afford it.</returns>
        public Unit Train(UnitKind kind)
        {
            GameObject prefab = PrefabOf(kind);
            UnitCost cost = CostOf(kind);
            if (prefab == null || !Treasury.TrySpend(cost.stone, cost.wood))
                return null;

            Vector2 jitter = UnityEngine.Random.insideUnitCircle;
            Vector3 position = spawnPoint.position + new Vector3(jitter.x, 0f, jitter.y);
            Unit unit = Instantiate(prefab, position, spawnPoint.rotation).GetComponent<Unit>();
            unit.Home = this;
            return unit;
        }

        public void Deposit(int stone, int wood) => Treasury.Deposit(stone, wood);

        /// <summary>Closest point on the building, so harvesters can deliver at its edge.</summary>
        public Vector3 ClosestPoint(Vector3 from)
        {
            Vector3 best = transform.position;
            float bestDistance = float.MaxValue;
            foreach (Collider c in colliders)
            {
                if (c == null || !c.enabled || c.isTrigger || (c is MeshCollider mesh && !mesh.convex))
                    continue;
                Vector3 point = c.ClosestPoint(from);
                float distance = (point - from).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = point;
                }
            }
            return best;
        }

        GameObject PrefabOf(UnitKind kind) =>
            kind == UnitKind.Melee ? meleeUnitPrefab : kind == UnitKind.Artillery ? artilleryUnitPrefab : harvesterUnitPrefab;
    }
}
