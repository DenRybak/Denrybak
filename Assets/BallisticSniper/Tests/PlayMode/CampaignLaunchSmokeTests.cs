using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BallisticSniper.Tests
{
    public sealed class CampaignLaunchSmokeTests
    {
        [UnityTest]
        public IEnumerator StartButtonEntersTheScopeForEveryDifficultyAndRendersTheRange()
        {
            yield return null;

            BallisticGame game = Object.FindObjectOfType<BallisticGame>();
            Assert.That(game, Is.Not.Null, "Runtime bootstrap did not create the game");
            MobileHud hud = Object.FindObjectOfType<MobileHud>();
            Assert.That(hud, Is.Not.Null, "Runtime HUD was not created");
            Assert.That(hud.StartButtonForTests, Is.Not.Null, "START button is missing");

            Difficulty[] difficulties =
            {
                Difficulty.Cadet,
                Difficulty.Shooter,
                Difficulty.Expert
            };

            for (int index = 0; index < difficulties.Length; index++)
            {
                if (game.CurrentScreen != GameScreen.Menu)
                {
                    game.OpenMenu();
                    yield return new WaitForSecondsRealtime(0.36f);
                }

                game.SetDifficulty(difficulties[index]);
                Assert.That(hud.StartButtonForTests.gameObject.activeInHierarchy, Is.True);
                Assert.That(hud.StartButtonForTests.interactable, Is.True);
                if (index == 0) hud.TapStartThroughAndroidFallbackForTests();
                else if (index == 1) hud.TapStartThroughPointerDownForTests();
                else hud.TapStartThroughStandardClickForTests();
                yield return null;

                Assert.That(
                    game.CurrentScreen,
                    Is.EqualTo(GameScreen.Playing),
                    "START did not enter gameplay for " + difficulties[index]);
                Assert.That(hud.IsMenuVisible, Is.False, "Main menu still covers gameplay");
                Assert.That(hud.IsGameplayVisible, Is.True, "Gameplay HUD is hidden");
                Assert.That(hud.IsScopeVisible, Is.True, "Scope is hidden");
                Assert.That(hud.IsBriefingVisible, Is.False, "Briefing blocked gameplay");

                RangeWorld world = Object.FindObjectOfType<RangeWorld>();
                Assert.That(world, Is.Not.Null);
                Assert.That(world.Targets.Count, Is.EqualTo(GameRules.TargetsPerStage));

                if (difficulties[index] == Difficulty.Shooter)
                {
                    yield return CaptureAndValidateWorldFrame();
                }

                game.OpenMenu();
                yield return new WaitForSecondsRealtime(0.36f);
            }
        }

        private static IEnumerator CaptureAndValidateWorldFrame()
        {
            // WaitForEndOfFrame is never resumed by the Unity Editor in
            // command-line batch mode. One regular frame is enough because
            // the camera is rendered explicitly below.
            yield return null;

            Camera camera = Object.FindObjectOfType<Camera>();
            Assert.That(camera, Is.Not.Null, "Sniper camera is missing");

            const int width = 960;
            const int height = 540;
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D capture = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;

            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            capture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            capture.Apply(false, false);

            Color32[] pixels = capture.GetPixels32();
            double sum = 0.0;
            double sumSquares = 0.0;
            double skyRed = 0.0;
            double skyGreen = 0.0;
            double skyBlue = 0.0;
            double groundRed = 0.0;
            double groundGreen = 0.0;
            double groundBlue = 0.0;
            int darkPixels = 0;
            int sampled = 0;
            int skySamples = 0;
            int groundSamples = 0;
            for (int i = 0; i < pixels.Length; i += 8)
            {
                Color32 pixel = pixels[i];
                double luminance = (pixel.r * 0.2126 + pixel.g * 0.7152 + pixel.b * 0.0722) / 255.0;
                sum += luminance;
                sumSquares += luminance * luminance;
                if (luminance < 0.055) darkPixels++;
                sampled++;

                int y = i / width;
                if (y >= height * 0.62f)
                {
                    skyRed += pixel.r / 255.0;
                    skyGreen += pixel.g / 255.0;
                    skyBlue += pixel.b / 255.0;
                    skySamples++;
                }
                else if (y < height * 0.34f)
                {
                    groundRed += pixel.r / 255.0;
                    groundGreen += pixel.g / 255.0;
                    groundBlue += pixel.b / 255.0;
                    groundSamples++;
                }
            }

            double average = sum / sampled;
            double variance = sumSquares / sampled - average * average;
            double deviation = System.Math.Sqrt(System.Math.Max(0.0, variance));
            double darkRatio = darkPixels / (double)sampled;
            skyRed /= skySamples;
            skyGreen /= skySamples;
            skyBlue /= skySamples;
            groundRed /= groundSamples;
            groundGreen /= groundSamples;
            groundBlue /= groundSamples;

            string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults"));
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllBytes(Path.Combine(outputDirectory, "runtime-world-v3.2.0.png"), capture.EncodeToPNG());

            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Object.Destroy(target);
            Object.Destroy(capture);

            Assert.That(average, Is.GreaterThan(0.12), "Rendered range is too dark");
            Assert.That(average, Is.LessThan(0.88), "Rendered range is overexposed");
            Assert.That(deviation, Is.GreaterThan(0.035), "Rendered range lacks visible texture/detail");
            Assert.That(darkRatio, Is.LessThan(0.72), "Most of the range rendered nearly black");
            Assert.That(skyBlue, Is.GreaterThan(skyRed * 1.08), "Sky has a yellow/red colour cast");
            Assert.That(skyGreen, Is.GreaterThan(skyRed * 1.04), "Sky lacks a natural blue/cyan balance");
            Assert.That(groundGreen, Is.GreaterThan(groundRed * 0.55), "Terrain is oversaturated orange");
            Assert.That(groundBlue, Is.GreaterThan(groundRed * 0.20), "Terrain has lost neutral brown detail");
        }
    }
}
