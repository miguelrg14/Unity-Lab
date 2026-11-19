using System.Collections.Generic;
using NUnit.Framework;
using UnityLab.Racing.Core;

namespace UnityLab.Tests.EditMode
{
    public class RaceProgressTests
    {
        static void DriveLap(RaceProgress progress, int waypoints, double time = 0)
        {
            for (int i = 0; i < waypoints; i++)
                progress.RegisterWaypoint((progress.NextWaypoint), time);
        }

        [Test]
        public void StartsBeforeTheStartLine()
        {
            var progress = new RaceProgress(4, 2);
            Assert.That(progress.NextWaypoint, Is.EqualTo(0));
            Assert.That(progress.CurrentLap, Is.EqualTo(1));
            Assert.That(progress.Finished, Is.False);
        }

        [Test]
        public void OnlyTheExpectedWaypointCounts()
        {
            var progress = new RaceProgress(4, 2);
            Assert.That(progress.RegisterWaypoint(2, 0), Is.False, "Skipping ahead is a shortcut.");
            Assert.That(progress.RegisterWaypoint(0, 0), Is.True);
            Assert.That(progress.RegisterWaypoint(0, 0), Is.False, "Touching the same waypoint twice does not count.");
            Assert.That(progress.WaypointsPassed, Is.EqualTo(1));
        }

        [Test]
        public void CrossingTheStartLineAgainCompletesALap()
        {
            var progress = new RaceProgress(4, 3);
            progress.RegisterWaypoint(0, 0); // start
            progress.RegisterWaypoint(1, 0);
            progress.RegisterWaypoint(2, 0);
            progress.RegisterWaypoint(3, 0);
            Assert.That(progress.CompletedLaps, Is.EqualTo(0));

            progress.RegisterWaypoint(0, 0);
            Assert.That(progress.CompletedLaps, Is.EqualTo(1));
            Assert.That(progress.CurrentLap, Is.EqualTo(2));
        }

        [Test]
        public void FinishesAfterTheLastLapAndStopsCounting()
        {
            var progress = new RaceProgress(3, 2);
            progress.RegisterWaypoint(0, 0);
            DriveLap(progress, 3, 10);
            Assert.That(progress.Finished, Is.False);
            DriveLap(progress, 3, 42.5);

            Assert.That(progress.Finished, Is.True);
            Assert.That(progress.FinishTime, Is.EqualTo(42.5));
            Assert.That(progress.CurrentLap, Is.EqualTo(2), "The displayed lap never exceeds the total.");
            Assert.That(progress.RegisterWaypoint(progress.NextWaypoint, 50), Is.False);
        }

        [Test]
        public void ApplyRemote_ReconstructsLapsFromTheWaypointCount()
        {
            var progress = new RaceProgress(4, 3);
            progress.ApplyRemote(waypointsPassed: 6, finished: false, raceTime: 0);

            Assert.That(progress.CompletedLaps, Is.EqualTo(1));
            Assert.That(progress.NextWaypoint, Is.EqualTo(2));

            progress.ApplyRemote(13, true, 99);
            Assert.That(progress.Finished, Is.True);
            Assert.That(progress.FinishTime, Is.EqualTo(99));
        }

        [Test]
        public void InvalidCircuit_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new RaceProgress(1, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new RaceProgress(4, 0));
        }
    }

    public class RaceStandingsTests
    {
        sealed class Racer
        {
            public string Name;
            public RaceProgress Progress = new RaceProgress(4, 1);
            public float Distance;
        }

        static List<string> Names(IEnumerable<Racer> racers)
        {
            var names = new List<string>();
            foreach (Racer racer in racers)
                names.Add(racer.Name);
            return names;
        }

        static List<Racer> Rank(params Racer[] racers) =>
            RaceStandings.Rank(racers, r => r.Progress, r => r.Distance);

        [Test]
        public void MoreWaypointsIsAhead()
        {
            var a = new Racer { Name = "A" };
            var b = new Racer { Name = "B" };
            b.Progress.RegisterWaypoint(0, 0);

            Assert.That(Names(Rank(a, b)), Is.EqualTo(new[] { "B", "A" }));
        }

        [Test]
        public void TieIsBrokenByDistanceToTheNextWaypoint()
        {
            var a = new Racer { Name = "A", Distance = 30 };
            var b = new Racer { Name = "B", Distance = 12 };

            Assert.That(Names(Rank(a, b)), Is.EqualTo(new[] { "B", "A" }));
        }

        [Test]
        public void FinishedRacersComeFirstInFinishOrder()
        {
            var slow = new Racer { Name = "Slow" };
            var fast = new Racer { Name = "Fast" };
            var driving = new Racer { Name = "Driving" };
            foreach (int wp in new[] { 0, 1, 2, 3, 0 })
            {
                slow.Progress.RegisterWaypoint(wp, 20);
                fast.Progress.RegisterWaypoint(wp, 10);
            }
            for (int i = 0; i < 4; i++)
                driving.Progress.RegisterWaypoint(i, 0);

            Assert.That(Names(Rank(driving, slow, fast)), Is.EqualTo(new[] { "Fast", "Slow", "Driving" }));
        }

        [Test]
        public void ExactTiesKeepTheirOrder()
        {
            var a = new Racer { Name = "A" };
            var b = new Racer { Name = "B" };
            Assert.That(Names(Rank(a, b)), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(Names(Rank(b, a)), Is.EqualTo(new[] { "B", "A" }));
        }

        [TestCase(1, "1st")]
        [TestCase(2, "2nd")]
        [TestCase(3, "3rd")]
        [TestCase(4, "4th")]
        [TestCase(11, "11th")]
        [TestCase(22, "22nd")]
        public void Ordinal(int position, string expected)
        {
            Assert.That(RaceStandings.Ordinal(position), Is.EqualTo(expected));
        }
    }
}
