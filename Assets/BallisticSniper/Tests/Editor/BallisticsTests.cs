#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

namespace BallisticSniper.Tests
{
    public sealed class BallisticsTests
    {
        [TestCase(200, 0.31f, 0.33f)]
        [TestCase(900, 1.70f, 1.74f)]
        public void VisualFlightTimeRetainsRealDistanceDifference(int range, float minimum, float maximum)
        {
            float visual = Ballistics.VisualFlightSeconds(range);
            Assert.That(visual, Is.InRange(minimum, maximum));
        }

        [Test]
        public void BullseyeAndRingScoresMatchNativeVersion()
        {
            Assert.That(GameRules.SteelScore(0.05f, 0.05f), Is.EqualTo(10));
            Assert.That(GameRules.SteelScore(0.20f, 0.00f), Is.EqualTo(7));
            Assert.That(GameRules.SteelScore(0.40f, 0.00f), Is.EqualTo(4));
            Assert.That(GameRules.SteelScore(0.60f, 0.00f), Is.EqualTo(0));
        }

        [Test]
        public void CampaignContentIsComplete()
        {
            Assert.That(GameRules.StageDefinitions.Length, Is.EqualTo(5));
            Assert.That(GameRules.OperationDefinitions.Length, Is.EqualTo(3));
            Assert.That(GameRules.Weapons.Length, Is.EqualTo(3));
            Assert.That(GameRules.CinematicNames.Length, Is.EqualTo(14));
            foreach (StageDefinition stage in GameRules.StageDefinitions)
            {
                Assert.That(stage.Targets.Length, Is.EqualTo(5));
                Assert.That(stage.HeightMil.Length, Is.EqualTo(5));
                Assert.That(stage.Motions.Length, Is.EqualTo(5));
            }
            foreach (OperationDefinition operation in GameRules.OperationDefinitions)
            {
                Assert.That(operation.RangeMetres, Is.GreaterThan(200));
                Assert.That(operation.Shots, Is.InRange(4, 5));
                Assert.That(operation.TargetDescription, Is.Not.Empty);
                Assert.That(operation.Complication, Is.Not.Empty);
            }
        }

        [Test]
        public void SelectableWeaponsProduceDifferentBallisticSolutions()
        {
            BallisticSolution ranger = Ballistics.Solve(610.0, 4.0, GameRules.Weapons[0]);
            BallisticSolution vektor = Ballistics.Solve(610.0, 4.0, GameRules.Weapons[1]);
            BallisticSolution titan = Ballistics.Solve(610.0, 4.0, GameRules.Weapons[2]);

            Assert.That(vektor.TimeSeconds, Is.LessThan(ranger.TimeSeconds));
            Assert.That(titan.TimeSeconds, Is.LessThan(vektor.TimeSeconds));
            Assert.That(vektor.WindDriftMetres, Is.LessThan(ranger.WindDriftMetres));
            Assert.That(titan.WindDriftMetres, Is.LessThan(vektor.WindDriftMetres));
            Assert.That(GameRules.Weapons[2].RagdollImpulse, Is.GreaterThan(GameRules.Weapons[0].RagdollImpulse));
            Assert.That(Ballistics.Solve(610.0, 4.0).TimeSeconds, Is.EqualTo(ranger.TimeSeconds).Within(1e-9));
        }

        [Test]
        public void WindDriftChangesSideAndRecommendedDialCancelsIt()
        {
            const double range = 500.0;
            const double opticalX = 1.75;
            BallisticSolution rightWind = Ballistics.Solve(range, 4.0);
            BallisticSolution leftWind = Ballistics.Solve(range, -4.0);

            Assert.That(rightWind.WindDriftMetres, Is.GreaterThan(0.0));
            Assert.That(leftWind.WindDriftMetres, Is.LessThan(0.0));
            Assert.That(leftWind.WindDriftMetres, Is.EqualTo(-rightWind.WindDriftMetres).Within(1e-9));
            Assert.That(Ballistics.HorizontalImpact(opticalX, range, 0.0, 0.0), Is.EqualTo(opticalX).Within(1e-9));
            Assert.That(
                Ballistics.HorizontalImpact(opticalX, range, 4.0, -rightWind.WindMil),
                Is.EqualTo(opticalX).Within(1e-9));
            Assert.That(
                Ballistics.HorizontalImpact(opticalX, range, -4.0, -leftWind.WindMil),
                Is.EqualTo(opticalX).Within(1e-9));
        }

        [Test]
        public void EveryCinematicEndsBesideTheImpactWithATightLens()
        {
            ShotRecord shot = new ShotRecord
            {
                Start = new Vector3(0f, BallisticGame.CameraHeight, -0.55f),
                Impact = new Vector3(0.35f, 1.72f, 500f),
                RangeMetres = 500f
            };
            Vector3 approach = (shot.Impact - shot.Start).normalized;

            for (int variant = 0; variant < GameRules.CinematicNames.Length; variant++)
            {
                KillCamDirector.CalculateImpactCloseUp(
                    shot, variant, out Vector3 position, out Vector3 lookAt, out float fieldOfView);

                Assert.That(Mathf.Abs(position.y - shot.Impact.y), Is.LessThan(0.30f),
                    "Variant " + variant + " still ends above the target");
                Assert.That(Vector3.Distance(position, shot.Impact), Is.InRange(2.50f, 2.80f));
                Assert.That(Vector3.Dot(shot.Impact - position, approach), Is.GreaterThan(2.45f));
                Assert.That(Vector3.Distance(lookAt, shot.Impact), Is.LessThan(0.03f));
                Assert.That(fieldOfView, Is.LessThanOrEqualTo(18f));
            }
        }
    }
}
#endif
