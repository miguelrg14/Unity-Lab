using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityLab.RTS.Army;
using UnityLab.RTS.Core;
using UnityLab.RTS.Units;
using UnityLab.Shared;

namespace UnityLab.Tests.PlayMode
{
    public class RtsPlayModeTests
    {
        [TearDown]
        public void TearDown()
        {
            GameSession.Autopilot = false;
            Time.timeScale = 1f;
        }

        static IEnumerator LoadBattlefield()
        {
            yield return SceneManager.LoadSceneAsync(Scenes.RtsBattlefield);
            yield return null;
        }

        static ArmyController Army(bool player) =>
            Object.FindObjectsByType<ArmyController>().First(a => a.PlayerControlled == player);

        [UnityTest]
        public IEnumerator Battlefield_HasAPlayerArmyAndAnAiArmy()
        {
            yield return LoadBattlefield();
            ArmyController player = Army(true);
            ArmyController ai = Army(false);

            Assert.That(player.MainBase, Is.Not.Null);
            Assert.That(ai.MainBase, Is.Not.Null);
            Assert.That(player.Team.TeamNumber, Is.Not.EqualTo(ai.Team.TeamNumber));
            Assert.That(ai.GetComponent<AiCommander>(), Is.Not.Null, "The opponent is played by the AI.");
            Assert.That(player.GetComponent<AiCommander>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator Training_SpendsResourcesAndPlacesTheUnitOnTheNavMesh()
        {
            yield return LoadBattlefield();
            ArmyController player = Army(true);
            Treasury treasury = player.MainBase.Treasury;
            int stoneBefore = treasury.Stone;

            Unit unit = player.Train(player.MainBase, UnitKind.Melee);
            yield return null;

            Assert.That(unit, Is.Not.Null);
            Assert.That(treasury.Stone, Is.EqualTo(stoneBefore - player.MainBase.CostOf(UnitKind.Melee).stone));
            Assert.That(unit.Agent.isOnNavMesh, Is.True, "The baked NavMesh covers the spawn point.");
            Assert.That(player.Units, Does.Contain(unit));
        }

        [UnityTest]
        public IEnumerator Training_WithoutResources_DoesNothing()
        {
            yield return LoadBattlefield();
            ArmyController player = Army(true);
            Treasury treasury = player.MainBase.Treasury;
            treasury.TrySpend(treasury.Stone, treasury.Wood);

            Assert.That(player.Train(player.MainBase, UnitKind.Artillery), Is.Null);
            Assert.That(player.Units, Is.Empty);
        }

        [UnityTest]
        public IEnumerator OrderedUnit_WalksToTheDestination()
        {
            yield return LoadBattlefield();
            ArmyController player = Army(true);
            Unit unit = player.Train(player.MainBase, UnitKind.Melee);
            yield return null;
            Vector3 start = unit.transform.position;
            Vector3 target = start + (Army(false).MainBase.transform.position - start).normalized * 6f;

            unit.OrderMove(target);
            Time.timeScale = 3f;
            yield return new WaitForSeconds(6f);

            Assert.That(Vector3.Distance(Flat(unit.transform.position), Flat(target)), Is.LessThan(1.5f));
            Assert.That(unit.State, Is.EqualTo(Unit.UnitState.Idle));
        }

        [UnityTest]
        public IEnumerator EnemiesInRange_AreSeenThroughTheVisionMatrix_AndFight()
        {
            yield return LoadBattlefield();
            ArmyController player = Army(true);
            ArmyController ai = Army(false);
            Object.Destroy(ai.GetComponent<AiCommander>());

            Unit mine = player.Train(player.MainBase, UnitKind.Melee);
            Unit theirs = ai.Train(ai.MainBase, UnitKind.Melee);
            yield return null;

            // Put them next to each other in open ground and let them fight.
            Vector3 middle = (player.MainBase.transform.position + ai.MainBase.transform.position) * 0.5f;
            mine.Agent.Warp(middle + Vector3.left * 1.5f);
            theirs.Agent.Warp(middle + Vector3.right * 1.5f);
            yield return null;
            yield return null;

            Assert.That(mine.EnemiesInSight, Does.Contain(theirs));
            Assert.That(theirs.EnemiesInSight, Does.Contain(mine));

            mine.OrderAttack(theirs);
            theirs.OrderAttack(mine);
            Time.timeScale = 4f;
            float deadline = Time.time + 60f;
            while (mine != null && theirs != null && mine.IsAlive && theirs.IsAlive && Time.time < deadline)
                yield return null;

            bool someoneDied = mine == null || theirs == null || !mine.IsAlive || !theirs.IsAlive;
            Assert.That(someoneDied, Is.True, "Melee units in contact kill each other eventually.");
        }

        [UnityTest]
        public IEnumerator Autopilot_BothArmiesHarvestTrainAndFight()
        {
            GameSession.Autopilot = true;
            yield return LoadBattlefield();
            Time.timeScale = 4f;

            var seen = new HashSet<Unit>();
            bool delivered = false;
            ArmyController player = Army(true);
            int startStone = player.MainBase.Treasury.Stone + player.MainBase.Treasury.Wood;
            float deadline = Time.time + 150f;
            while (Time.time < deadline)
            {
                foreach (Unit unit in Object.FindObjectsByType<Unit>())
                    seen.Add(unit);
                foreach (Unit unit in seen)
                {
                    if (unit is HarvesterUnit harvester && harvester != null && harvester.Harvest == HarvesterUnit.HarvestState.ReturningToBase)
                        delivered = true;
                }
                if (seen.Count(u => u == null) >= 2 && delivered)
                    break;
                yield return new WaitForSeconds(0.5f);
            }

            Assert.That(seen.Count, Is.GreaterThanOrEqualTo(4), "Both commanders train units.");
            Assert.That(delivered, Is.True, "Harvesters fill up and head back to base.");
            Assert.That(seen.Count(u => u == null), Is.GreaterThanOrEqualTo(1), "The armies meet and fight.");
            Assert.That(startStone, Is.GreaterThan(0));
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
