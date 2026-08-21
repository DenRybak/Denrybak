using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
                Button start = hud.StartButtonForTests;
                Assert.That(start.gameObject.activeInHierarchy, Is.True);
                Assert.That(start.interactable, Is.True);
                start.onClick.Invoke();
                yield return null;

                Assert.That(
                    game.CurrentScreen,
                    Is.EqualTo(GameScreen.Playing),
                    "START did not enter gameplay for " + difficulties[index]);
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
            yield return new WaitForEndOfFrame();

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
            int darkPixels = 0;
            int sampled = 0;
            for (int i = 0; i < pixels.Length; i += 8)
            {
                Color32 pixel = pixels[i];
                double luminance = (pixel.r * 0.2126 + pixel.g * 0.7152 + pixel.b * 0.0722) / 255.0;
                sum += luminance;
                sumSquares += luminance * luminance;
                if (luminance < 0.055) darkPixels++;
                sampled++;
            }

            double average = sum / sampled;
            double variance = sumSquares / sampled - average * average;
            double deviation = System.Math.Sqrt(System.Math.Max(0.0, variance));
            double darkRatio = darkPixels / (double)sampled;

            string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../TestResults"));
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllBytes(Path.Combine(outputDirectory, "runtime-world-v3.1.0.png"), capture.EncodeToPNG());

            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Object.Destroy(target);
            Object.Destroy(capture);

            Assert.That(average, Is.GreaterThan(0.12), "Rendered range is too dark");
            Assert.That(average, Is.LessThan(0.88), "Rendered range is overexposed");
            Assert.That(deviation, Is.GreaterThan(0.035), "Rendered range lacks visible texture/detail");
            Assert.That(darkRatio, Is.LessThan(0.72), "Most of the range rendered nearly black");
        }
    }
}
