using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BallisticSniper.Tests
{
    public sealed class CampaignLaunchSmokeTests
    {
        [UnitySetUp]
        public IEnumerator AllowInputDebounceToSettleBetweenScenarios()
        {
            // Each test is a new interaction, not a duplicate tap from the
            // preceding test. Keep the production button debounce enabled.
            yield return new WaitForSecondsRealtime(0.5f);
        }

        [UnityTest]
        public IEnumerator AimSurfaceReceivesRaycastsAfterHidingAndRestoringGameplay()
        {
            yield return null;
            BallisticGame game = Object.FindObjectOfType<BallisticGame>();
            MobileHud hud = Object.FindObjectOfType<MobileHud>();
            game.OpenMenu();
            hud.TapTrainingThroughStandardClickForTests();
            yield return null;
            AimDragSurface surface = Object.FindObjectOfType<AimDragSurface>();
            Assert.That(surface, Is.Not.Null);
            surface.gameObject.SetActive(false);
            yield return null;
            surface.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            yield return null;
            PointerEventData pointer = new PointerEventData(EventSystem.current)
            {
                position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                delta = new Vector2(30f, 10f),
                button = PointerEventData.InputButton.Left
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0), "Centre aim surface does not receive touches");
            Assert.That(hits[0].gameObject.GetComponent<AimDragSurface>(), Is.SameAs(surface),
                "Another UI element intercepts aiming at the centre");
            bool received = false;
            var previousHandler = surface.Dragged;
            surface.Dragged = delta => { received = delta.sqrMagnitude > 0; previousHandler?.Invoke(delta); };
            ExecuteEvents.Execute<IDragHandler>(hits[0].gameObject, pointer, ExecuteEvents.dragHandler);
            surface.Dragged = previousHandler;
            Assert.That(received, Is.True, "Gesture did not reach the aiming handler");
            game.OpenMenu();
        }

        [UnityTest]
        public IEnumerator StartButtonEntersTheScopeForEveryDifficultyAndRendersTheRange()
        {
            yield return null;

            BallisticGame game = Object.FindObjectOfType<BallisticGame>();
            Assert.That(game, Is.Not.Null, "Runtime bootstrap did not create the game");
            MobileHud hud = Object.FindObjectOfType<MobileHud>();
            Assert.That(hud, Is.Not.Null, "Runtime HUD was not created");
            Assert.That(hud.StartButtonForTests, Is.Not.Null, "MISSIONS button is missing");
            Assert.That(hud.TrainingButtonForTests, Is.Not.Null, "TRAINING button is missing");

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
                Assert.That(hud.TrainingButtonForTests.gameObject.activeInHierarchy, Is.True);
                Assert.That(hud.TrainingButtonForTests.interactable, Is.True);
                hud.TapTrainingThroughStandardClickForTests();
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
                    yield return CaptureAndValidateWorldFrame("runtime-world-v5.0.0.png");
                }

                game.OpenMenu();
                yield return new WaitForSecondsRealtime(0.36f);
            }
        }

        [UnityTest]
        public IEnumerator OperationsBuildCharactersAndReleaseARealJointedRagdoll()
        {
            yield return null;

            BallisticGame game = Object.FindObjectOfType<BallisticGame>();
            MobileHud hud = Object.FindObjectOfType<MobileHud>();
            Assert.That(game, Is.Not.Null);
            Assert.That(hud, Is.Not.Null);

            if (game.CurrentScreen != GameScreen.Menu)
            {
                game.OpenMenu();
                yield return new WaitForSecondsRealtime(0.36f);
            }

            WeaponDefinition before = game.SelectedWeaponForTests;
            game.CycleWeapon();
            Assert.That(game.SelectedWeaponForTests.Kind, Is.Not.EqualTo(before.Kind));
            game.SetDifficulty(Difficulty.Cadet);
            hud.TapStartThroughAndroidFallbackForTests();
            yield return null;

            Assert.That(game.CurrentScreen, Is.EqualTo(GameScreen.Briefing), "Missions must start with a briefing");
            Assert.That(hud.IsBriefingVisible, Is.True);
            hud.TapBriefingEnterThroughStandardClickForTests();
            yield return null;

            Assert.That(game.CurrentScreen, Is.EqualTo(GameScreen.Playing));
            Assert.That(game.CampaignModeForTests, Is.EqualTo(CampaignMode.Operations));
            RangeWorld world = Object.FindObjectOfType<RangeWorld>();
            Assert.That(world, Is.Not.Null);
            Assert.That(world.Targets.Count, Is.EqualTo(0));
            Assert.That(world.Humans.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(world.PrimaryHuman, Is.Not.Null);
            Assert.That(world.PrimaryHuman.Bodies.Count, Is.GreaterThanOrEqualTo(10));
            Assert.That(world.PrimaryHuman.GetComponent<Animator>(), Is.Not.Null, "Mission character has no skeleton animator host");
            MeshFilter[] humanMeshes = world.PrimaryHuman.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(humanMeshes.Length, Is.GreaterThanOrEqualTo(12), "Mission character is not a volumetric multi-part mesh");
            Assert.That(System.Array.Exists(humanMeshes, m => m.sharedMesh != null && m.sharedMesh.name.Contains("Torso Mesh")), Is.True,
                "Procedural torso mesh is missing");
            foreach (Rigidbody body in world.PrimaryHuman.Bodies)
                Assert.That(body.isKinematic, Is.True, "Observation pose is not stable before impact");

            yield return CaptureAndValidateWorldFrame("runtime-operation-v5.0.0.png");

            HumanMissionActor target = world.PrimaryHuman;
            Vector3 initialCentre = target.AimCentre;
            world.ApplyHumanImpact(target, initialCentre, Vector3.forward, GameRules.Weapons[2].RagdollImpulse);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(target.IsRagdolled, Is.True);
            bool releasedBody = false;
            bool hasJoint = false;
            foreach (Rigidbody body in target.Bodies)
            {
                releasedBody |= !body.isKinematic;
                hasJoint |= body.GetComponent<CharacterJoint>() != null;
            }
            Assert.That(releasedBody, Is.True, "Impact did not release the rigidbodies");
            Assert.That(hasJoint, Is.True, "Character is not connected by physics joints");

            game.OpenMenu();
            yield return new WaitForSecondsRealtime(0.36f);
            game.SetCampaignMode(CampaignMode.Range);
            for (int i = 0; i < GameRules.Weapons.Length && game.SelectedWeaponForTests.Kind != WeaponKind.Ranger308; i++)
                game.CycleWeapon();
        }

        [UnityTest]
        public IEnumerator EscapeOperationHasTwoTargetsAndMovingVehiclePassenger()
        {
            yield return null;
            RangeWorld world = Object.FindObjectOfType<RangeWorld>();
            Assert.That(world, Is.Not.Null);
            Assert.That(GameRules.OperationDefinitions.Length, Is.EqualTo(4));
            Assert.That(GameRules.OperationTargetCount(3), Is.EqualTo(2));

            world.BuildStage(3, Difficulty.Cadet, CampaignMode.Operations);
            yield return null;
            HumanMissionActor first = null;
            HumanMissionActor second = null;
            for (int i = 0; i < world.Humans.Count; i++)
            {
                HumanMissionActor actor = world.Humans[i];
                if (!actor.IsPrimary) continue;
                if (first == null) first = actor; else if (second == null) second = actor;
            }
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Vector3 before = second.transform.position;
            Assert.That(world.BeginEscapeAfterFirstTarget(first, 0f), Is.True);

            world.TickTargets(0.75f);
            Assert.That(Vector3.Distance(second.transform.position, before), Is.LessThan(0.08f),
                "The survivor moved before the one-second reaction delay elapsed");

            world.TickTargets(1.70f);
            Assert.That(Vector3.Distance(second.transform.position, before), Is.GreaterThan(0.35f),
                "The survivor did not run toward the car");
            Assert.That(second.IsSeatedInVehicle, Is.False);

            world.TickTargets(4.15f);
            Assert.That(second.IsSeatedInVehicle, Is.True,
                "The smooth boarding sequence did not place the survivor in the vehicle");

            Vector3 seated = second.transform.position;
            world.TickTargets(5.60f);
            Assert.That(Vector3.Distance(second.transform.position, seated), Is.GreaterThan(1.0f),
                "The target-in-car did not move through the firing sector");

            world.TickTargets(8.10f);
            Assert.That(world.EscapeTargetLost, Is.True);

            world.BuildStage(1, Difficulty.Cadet, CampaignMode.Operations);
            yield return null;
            Assert.That(world.TryShatterOperationGlass(new Vector3(0f, 1.56f, 420f)), Is.True,
                "Hotel window glass did not shatter on impact");

            world.BuildStage(0, Difficulty.Cadet, CampaignMode.Operations);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ShotReviewReturnsToAimWithoutFiringAndKeepsOpticsCentred()
        {
            yield return null;

            BallisticGame game = Object.FindObjectOfType<BallisticGame>();
            MobileHud hud = Object.FindObjectOfType<MobileHud>();
            Camera camera = Object.FindObjectOfType<Camera>();
            Assert.That(game, Is.Not.Null);
            Assert.That(hud, Is.Not.Null);
            Assert.That(camera, Is.Not.Null);

            if (game.CurrentScreen != GameScreen.Menu)
            {
                game.OpenMenu();
                yield return new WaitForSecondsRealtime(0.36f);
            }

            for (int i = 0; i < GameRules.Weapons.Length && game.SelectedWeaponForTests.Kind != WeaponKind.Ranger308; i++)
                game.CycleWeapon();
            game.SetDifficulty(Difficulty.Cadet);
            hud.TapTrainingThroughStandardClickForTests();
            yield return null;
            Assert.That(game.CurrentScreen, Is.EqualTo(GameScreen.Playing));

            Vector3 opticalPoint = game.OpticalAxisPointForTests();
            Vector3 opticalScreen = camera.WorldToScreenPoint(opticalPoint);
            Vector2 reticleScreen = hud.ReticleScreenCentreForTests();
            Assert.That(Vector2.Distance(new Vector2(opticalScreen.x, opticalScreen.y), reticleScreen),
                Is.LessThan(1.5f), "The reticle is offset from the camera optical axis");

            // Stage one centre steel is +0.15 MIL high; +1.0 MIL elevation
            // compensates the 0.83 MIL drop closely enough for a bullseye.
            game.AdjustElevation(1.0f);
            int acceptedBeforeFire = game.AcceptedShotCountForTests;
            game.Fire();
            Assert.That(game.AcceptedShotCountForTests, Is.EqualTo(acceptedBeforeFire + 1));
            Assert.That(game.CurrentScreen, Is.EqualTo(GameScreen.Flight));

            float deadline = Time.realtimeSinceStartup + 8f;
            bool sawCinematic = false;
            while (!game.IsResultReadyForTests && Time.realtimeSinceStartup < deadline)
            {
                if (game.CurrentScreen == GameScreen.Cinematic) sawCinematic = true;
                yield return null;
            }
            Assert.That(game.IsResultReadyForTests, Is.True, "The shot never reached its review result");
            Assert.That(sawCinematic, Is.True, "The corrected centre shot did not enter the impact cinematic");
            Assert.That(hud.IsResultVisible, Is.True);
            Assert.That(hud.IsGameplayVisible, Is.False, "Gameplay HUD remained under the result action");
            Assert.That(hud.FireButtonForTests.gameObject.activeInHierarchy, Is.False,
                "FIRE is still active beneath К ЦЕЛЯМ");
            Assert.That(hud.ResultActionOverlapsFireForTests(), Is.False,
                "К ЦЕЛЯМ still geometrically overlaps FIRE");

            int acceptedAtResult = game.AcceptedShotCountForTests;
            hud.TapResultActionThroughAndroidFallbackForTests();
            yield return null;
            Assert.That(game.CurrentScreen, Is.EqualTo(GameScreen.Playing));
            Assert.That(game.AcceptedShotCountForTests, Is.EqualTo(acceptedAtResult),
                "Returning to targets fired another shot");

            // Even a synthetic immediate tap on the newly revealed FIRE button
            // is rejected while the return touch is being released.
            hud.TapFireThroughAndroidFallbackForTests();
            game.DragAim(new Vector2(24f, 9f));
            yield return new WaitForSecondsRealtime(0.45f);
            Assert.That(game.CurrentScreen, Is.EqualTo(GameScreen.Playing),
                "The player did not get an aiming window after К ЦЕЛЯМ");
            Assert.That(game.AcceptedShotCountForTests, Is.EqualTo(acceptedAtResult));
            Assert.That(hud.FireButtonForTests.interactable, Is.True);

            game.OpenMenu();
            yield return new WaitForSecondsRealtime(0.36f);
        }

        private static IEnumerator CaptureAndValidateWorldFrame(string fileName)
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
            File.WriteAllBytes(Path.Combine(outputDirectory, fileName), capture.EncodeToPNG());

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
