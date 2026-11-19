using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityLab.RTS.Core;

namespace UnityLab.Tests.EditMode
{
    public class HealthTests
    {
        [Test]
        public void OnlyTheKillingBlowReturnsTrue()
        {
            var health = new Health(10);
            int deaths = 0;
            health.Died += () => deaths++;

            Assert.That(health.ApplyDamage(6), Is.False);
            Assert.That(health.ApplyDamage(6), Is.True);
            Assert.That(health.ApplyDamage(6), Is.False, "Hitting a dead unit again is not a second kill.");
            Assert.That(health.Current, Is.EqualTo(0));
            Assert.That(deaths, Is.EqualTo(1));
        }

        [Test]
        public void HealIsCappedAndDoesNotRevive()
        {
            var health = new Health(10);
            health.ApplyDamage(4);
            health.Heal(100);
            Assert.That(health.Current, Is.EqualTo(10));

            health.ApplyDamage(10);
            health.Heal(5);
            Assert.That(health.IsAlive, Is.False);
        }

        [Test]
        public void InvincibleTakesNoDamage()
        {
            var health = new Health(10) { Invincible = true };
            Assert.That(health.ApplyDamage(50), Is.False);
            Assert.That(health.Normalized, Is.EqualTo(1f));
        }

        [Test]
        public void NegativeValuesAreRejected()
        {
            var health = new Health(10);
            Assert.Throws<ArgumentOutOfRangeException>(() => health.ApplyDamage(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => health.Heal(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Health(0));
        }
    }

    public class VisionMatrixTests
    {
        sealed class Agent : IVisionAgent
        {
            public string Name;
            public float X { get; set; }
            public float Z { get; set; }
            public float VisionRadius { get; set; } = 10;
            public override string ToString() => Name;
        }

        VisionMatrix<Agent> matrix;
        List<string> events;

        [SetUp]
        public void SetUp()
        {
            matrix = new VisionMatrix<Agent>();
            events = new List<string>();
            matrix.EnemyEntered += (observer, enemy) => events.Add($"{observer} sees {enemy}");
            matrix.EnemyLeft += (observer, enemy) => events.Add($"{observer} lost {enemy}");
        }

        [Test]
        public void EnteringRange_IsReportedOnceForEachSide()
        {
            var a = new Agent { Name = "A" };
            var b = new Agent { Name = "B", X = 50 };
            matrix.Add(a, 0);
            matrix.Add(b, 1);

            matrix.Update();
            Assert.That(events, Is.Empty);

            b.X = 6;
            matrix.Update();
            matrix.Update();
            Assert.That(events, Is.EqualTo(new[] { "A sees B", "B sees A" }));
            Assert.That(matrix.GetSquaredDistance(0, 0), Is.EqualTo(36f));
        }

        [Test]
        public void DifferentRadii_AreRespectedPerObserver()
        {
            var sniper = new Agent { Name = "Sniper", VisionRadius = 20 };
            var grunt = new Agent { Name = "Grunt", VisionRadius = 5, Z = 15 };
            matrix.Add(sniper, 0);
            matrix.Add(grunt, 1);

            matrix.Update();
            Assert.That(events, Is.EqualTo(new[] { "Sniper sees Grunt" }));
        }

        [Test]
        public void LeavingRange_IsReported()
        {
            var a = new Agent { Name = "A" };
            var b = new Agent { Name = "B", X = 5 };
            matrix.Add(a, 0);
            matrix.Add(b, 1);
            matrix.Update();
            events.Clear();

            b.X = 100;
            matrix.Update();
            Assert.That(events, Is.EqualTo(new[] { "A lost B", "B lost A" }));
            Assert.That(matrix.GetSquaredDistance(0, 0), Is.EqualTo(VisionMatrix<Agent>.Far));
        }

        [Test]
        public void RemovingAUnit_NotifiesOnlyTheEnemiesThatSawIt()
        {
            var a = new Agent { Name = "A" };
            var near = new Agent { Name = "Near", X = 3 };
            var far = new Agent { Name = "Far", X = 300 };
            matrix.Add(a, 0);
            matrix.Add(near, 1);
            matrix.Add(far, 1);
            matrix.Update();
            events.Clear();

            matrix.Remove(a);
            Assert.That(events, Is.EqualTo(new[] { "Near lost A" }));
            Assert.That(matrix.ArmyA.Count, Is.EqualTo(0));
        }

        [Test]
        public void RemovingAColumn_KeepsTheMatrixConsistent()
        {
            var a = new Agent { Name = "A" };
            var b1 = new Agent { Name = "B1", X = 100 };
            var b2 = new Agent { Name = "B2", X = 4 };
            matrix.Add(a, 0);
            matrix.Add(b1, 1);
            matrix.Add(b2, 1);
            matrix.Update();

            matrix.Remove(b1);
            matrix.Update();
            Assert.That(matrix.ArmyB.Count, Is.EqualTo(1));
            Assert.That(matrix.GetSquaredDistance(0, 0), Is.EqualTo(16f));
        }

        [Test]
        public void AddingTwice_IsIgnored_AndUnknownArmiesThrow()
        {
            var a = new Agent { Name = "A" };
            matrix.Add(a, 0);
            matrix.Add(a, 0);
            Assert.That(matrix.ArmyA.Count, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => matrix.Add(new Agent(), 2));
        }
    }

    public class HarvestSpotsTests
    {
        [Test]
        public void HarvestersQueueWhenFullAndInheritTheFreedSpot()
        {
            var spots = new HarvestSpots<string>(2);
            Assert.That(spots.Request("h1"), Is.EqualTo(0));
            Assert.That(spots.Request("h2"), Is.EqualTo(1));
            Assert.That(spots.Request("h3"), Is.EqualTo(-1));
            Assert.That(spots.Request("h4"), Is.EqualTo(-1));
            Assert.That(spots.QueueLength, Is.EqualTo(2));

            Assert.That(spots.Leave("h1", out string next), Is.EqualTo(0));
            Assert.That(next, Is.EqualTo("h3"), "First come, first served.");
            Assert.That(spots.IsTaken(0), Is.True);
            Assert.That(spots.QueueLength, Is.EqualTo(1));
        }

        [Test]
        public void LeavingTheQueue_DoesNotFreeASpot()
        {
            var spots = new HarvestSpots<string>(1);
            spots.Request("h1");
            spots.Request("h2");

            Assert.That(spots.Leave("h2", out string next), Is.EqualTo(-1));
            Assert.That(next, Is.Null);
            Assert.That(spots.QueueLength, Is.Zero);
            Assert.That(spots.IsTaken(0), Is.True);
        }

        [Test]
        public void RequestingTwice_ReturnsTheSameSpot()
        {
            var spots = new HarvestSpots<string>(3);
            Assert.That(spots.Request("h1"), Is.EqualTo(spots.Request("h1")));
        }

        [Test]
        public void ResourceStock_GivesOnlyWhatIsLeft()
        {
            var stock = new ResourceStock(5);
            Assert.That(stock.Take(3), Is.EqualTo(3));
            Assert.That(stock.Take(3), Is.EqualTo(2));
            Assert.That(stock.Take(3), Is.EqualTo(0));
            Assert.That(stock.IsDepleted, Is.True);
        }
    }

    public class TreasuryAndSelectionTests
    {
        [Test]
        public void TrySpend_IsAllOrNothing()
        {
            var treasury = new Treasury(stone: 50, wood: 10);
            Assert.That(treasury.TrySpend(40, 20), Is.False);
            Assert.That(treasury.Stone, Is.EqualTo(50));
            Assert.That(treasury.TrySpend(40, 10), Is.True);
            Assert.That(treasury.Stone, Is.EqualTo(10));
            Assert.That(treasury.Wood, Is.EqualTo(0));
        }

        [Test]
        public void SelectionBox_WorksInAnyDragDirection()
        {
            SelectionBox box = SelectionBox.FromCorners(300, 50, 100, 250);
            Assert.That(box.XMin, Is.EqualTo(100));
            Assert.That(box.YMax, Is.EqualTo(250));
            Assert.That(box.Contains(200, 100), Is.True);
            Assert.That(box.Contains(200, 100, depth: -1), Is.False, "Units behind the camera are never selected.");
            Assert.That(box.Contains(50, 100), Is.False);
        }

        [Test]
        public void SelectionBox_SmallDragIsAClick()
        {
            Assert.That(SelectionBox.FromCorners(10, 10, 12, 11).IsClick(), Is.True);
            Assert.That(SelectionBox.FromCorners(10, 10, 60, 11).IsClick(), Is.False);
        }
    }
}
