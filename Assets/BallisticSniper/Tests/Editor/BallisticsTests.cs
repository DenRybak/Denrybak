#if UNITY_EDITOR
using NUnit.Framework;

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
            Assert.That(GameRules.CinematicNames.Length, Is.EqualTo(14));
            foreach (StageDefinition stage in GameRules.StageDefinitions)
            {
                Assert.That(stage.Targets.Length, Is.EqualTo(5));
                Assert.That(stage.HeightMil.Length, Is.EqualTo(5));
                Assert.That(stage.Motions.Length, Is.EqualTo(5));
            }
        }
    }
}
#endif
