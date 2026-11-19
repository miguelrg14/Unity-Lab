using System.Collections.Generic;
using UnityEngine;
using UnityLab.RTS.Core;
using UnityLab.RTS.Units;
using UnityLab.RTS.World;

namespace UnityLab.RTS.Army
{
    /// <summary>
    /// Simple opponent that plays an army through the same API the player uses:
    /// keeps a couple of harvesters working, trains soldiers with what they bring back, defends,
    /// and sends a wave at the enemy base once it has enough soldiers.
    /// It thinks once per <see cref="decisionInterval"/>, not every frame.
    /// </summary>
    [RequireComponent(typeof(ArmyController))]
    public class AiCommander : MonoBehaviour
    {
        [SerializeField] float decisionInterval = 1f;
        [SerializeField] int desiredHarvesters = 2;
        [SerializeField] int waveSize = 4;
        [SerializeField] float defenceRadius = 20f;

        ArmyController army;
        ArmyController enemy;
        ResourceNode[] resources;
        float nextDecision;
        int trainCounter;

        void Awake() => army = GetComponent<ArmyController>();

        void Start()
        {
            foreach (ArmyController other in FindObjectsByType<ArmyController>())
            {
                if (other != army)
                    enemy = other;
            }
            resources = FindObjectsByType<ResourceNode>();
            nextDecision = Time.time + 0.5f;
        }

        void Update()
        {
            if (Time.time < nextDecision || army.MainBase == null)
                return;
            nextDecision = Time.time + decisionInterval;

            var harvesters = new List<HarvesterUnit>();
            var soldiers = new List<Unit>();
            foreach (Unit unit in army.Units)
            {
                if (!unit.IsAlive)
                    continue;
                if (unit is HarvesterUnit harvester)
                    harvesters.Add(harvester);
                else
                    soldiers.Add(unit);
            }

            TrainUnits(harvesters.Count);
            ManageHarvesters(harvesters);
            CommandSoldiers(soldiers);
        }

        void TrainUnits(int harvesterCount)
        {
            ArmyBase home = army.MainBase;
            if (harvesterCount < desiredHarvesters && home.CanAfford(UnitKind.Harvester))
            {
                army.Train(home, UnitKind.Harvester);
                return;
            }

            UnitKind next = trainCounter % 3 == 2 ? UnitKind.Artillery : UnitKind.Melee;
            if (home.CanAfford(next) && army.Train(home, next) != null)
                trainCounter++;
        }

        void ManageHarvesters(List<HarvesterUnit> harvesters)
        {
            foreach (HarvesterUnit harvester in harvesters)
            {
                if (harvester.Harvest != HarvesterUnit.HarvestState.None)
                    continue;
                ResourceNode node = PickResource(harvester.transform.position);
                if (node != null)
                    harvester.OrderHarvest(node);
            }
        }

        /// <summary>The nearest non-empty node of whichever resource the army has less of.</summary>
        ResourceNode PickResource(Vector3 from)
        {
            Treasury treasury = army.MainBase.Treasury;
            ResourceType wanted = treasury.Stone <= treasury.Wood ? ResourceType.Stone : ResourceType.Wood;

            ResourceNode best = null;
            float bestScore = float.MaxValue;
            foreach (ResourceNode node in resources)
            {
                if (node == null || node.IsDepleted)
                    continue;
                float score = (node.transform.position - from).sqrMagnitude * (node.Type == wanted ? 1f : 4f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = node;
                }
            }
            return best;
        }

        void CommandSoldiers(List<Unit> soldiers)
        {
            if (enemy == null)
                return;

            Vector3 home = army.MainBase.transform.position;
            Unit threat = NearestEnemy(home, defenceRadius);
            bool attackWave = soldiers.Count >= waveSize;

            foreach (Unit soldier in soldiers)
            {
                if (soldier.State != Unit.UnitState.Idle)
                    continue;

                // Fight whatever is already in sight, then defend, then attack in waves.
                Unit target = NearestOf(soldier.EnemiesInSight, soldier.transform.position) ?? threat;
                if (target == null && attackWave)
                    target = NearestEnemy(soldier.transform.position, float.MaxValue);

                if (target != null)
                    soldier.OrderAttack(target);
                else if (attackWave && enemy.MainBase != null)
                    soldier.OrderMove(enemy.MainBase.ClosestPoint(soldier.transform.position));
            }
        }

        Unit NearestEnemy(Vector3 from, float radius) => NearestOf(enemy.Units, from, radius);

        static Unit NearestOf(IReadOnlyList<Unit> candidates, Vector3 from, float radius = float.MaxValue)
        {
            Unit best = null;
            float bestDistance = radius * radius;
            foreach (Unit unit in candidates)
            {
                if (unit == null || !unit.IsAlive)
                    continue;
                float distance = (unit.transform.position - from).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = unit;
                }
            }
            return best;
        }
    }
}
