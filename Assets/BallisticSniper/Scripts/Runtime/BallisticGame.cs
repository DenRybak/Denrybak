using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BallisticSniper
{
    public sealed class BallisticGame : MonoBehaviour
    {
        public const float CameraHeight = 1.65f;
        private const float MilToDegrees = 0.05729578f;
        private const float BaseScopeFov = 52f;

        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly System.Random random = new System.Random();

        private RangeWorld world;
        private MobileHud hud;
        private Camera playerCamera;
        private AudioSource audioSource;
        private KillCamDirector killCam;
        private ProjectileTracer activeProjectile;
        private GameObject impactMarker;

        private GameScreen screen = GameScreen.Menu;
        private GameScreen resumeScreen = GameScreen.Playing;
        private Difficulty difficulty;
        private int stage;
        private int worldStageIndex = -1;
        private int zoomIndex;
        private int shotInStage;
        private int totalShots;
        private int score;
        private int highScore;
        private int hitCount;
        private int successfulShots;
        private int destroyedCount;
        private int targetsCleared;
        private int roundDestructiblesTotal;
        private int roundDestructiblesDestroyed;
        private int destructionStreak;
        private bool demolitionBonusAwarded;
        private bool bonusMode;
        private bool campaignStarting;

        private float range;
        private float baseWind;
        private float currentWind;
        private float elevationDialMil;
        private float windageDialMil;
        private float aimYawDegrees;
        private float aimPitchDegrees;
        private float swayMilX;
        private float swayMilY;
        private float recoilMilX;
        private float recoilMilY;
        private float recoilVelocityX;
        private float recoilVelocityY;
        private float breath = 1f;
        private bool holdingBreath;
        private float sceneClock;
        private BallisticSolution displayedSolution;

        private ShotRecord currentShot;
        private float shotSceneClock;
        private float flightElapsed;
        private Quaternion firingCameraRotation;
        private Vector3 firingCameraPosition;
        private float firingCameraFov;

        private TargetActor lastReviewedTarget;
        private TargetActor deferredSteelHide;
        private bool deferredBonusImpact;
        private Vector2 lastError;
        private int lastShotScore;
        private bool lastBullseye;
        private bool lastHitDestructible;
        private bool lastDemolitionBonus;
        private bool lastBonusShot;
        private int lastChainReaction;
        private int previousCinematicVariant = -1;
        private float reviewZoom = 1f;
        private Quaternion reviewStartRotation;
        private Quaternion reviewTargetRotation;
        private float reviewStartFov;
        private float reviewTargetFov;
        private float reviewStartedAt;
        private float resultRevealAt;
        private bool resultShown;

        public static BallisticGame Instance { get; private set; }
        public float CurrentWind => currentWind;
        public GameScreen CurrentScreen => screen;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            ConfigureApplication();

            difficulty = (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt("difficulty", 1), 0, 2);
            zoomIndex = Mathf.Clamp(PlayerPrefs.GetInt("zoom_index", 0), 0, GameRules.ZoomLevels.Length - 1);
            highScore = PlayerPrefs.GetInt("high_score", 0);

            CreateCamera();
            CreateWorld();
            CreateAudio();
            CreateHud();
            CreateKillCam();

            world.BuildStage(0, difficulty);
            worldStageIndex = 0;
            ResetCameraForMenu();
            hud.ShowMenu(highScore, difficulty);
        }

        private void Update()
        {
            float dt = Mathf.Min(0.05f, Time.deltaTime);
            HandleKeyboardInput(dt);

            switch (screen)
            {
                case GameScreen.Menu:
                case GameScreen.Help:
                    UpdateMenuCamera();
                    break;
                case GameScreen.Briefing:
                    UpdateBriefingCamera();
                    break;
                case GameScreen.Playing:
                    sceneClock += dt;
                    world.TickTargets(sceneClock);
                    UpdateBreath(dt);
                    UpdateWind();
                    UpdateSway();
                    UpdateRecoil(dt);
                    UpdatePlayerCamera();
                    RefreshGameplayHud(true);
                    break;
                case GameScreen.Flight:
                    flightElapsed += dt;
                    float physicalProgress = Mathf.Clamp01(flightElapsed / Mathf.Max(0.01f, currentShot.VisualDuration));
                    world.TickTargets(shotSceneClock + (float)currentShot.Solution.TimeSeconds * physicalProgress);
                    UpdateBreath(dt);
                    UpdateRecoil(dt);
                    UpdatePlayerCamera();
                    RefreshGameplayHud(false);
                    break;
                case GameScreen.Result:
                    UpdateReviewCamera();
                    if (!resultShown && Time.unscaledTime >= resultRevealAt)
                    {
                        resultShown = true;
                        hud.ShowResult(BuildResultSnapshot());
                    }
                    break;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetDifficulty(Difficulty selected)
        {
            if (screen != GameScreen.Menu) return;
            difficulty = selected;
            PlayerPrefs.SetInt("difficulty", (int)difficulty);
            PlayerPrefs.Save();
            hud.ShowMenu(highScore, difficulty);
        }

        public void StartCampaign()
        {
            if (campaignStarting) return;

            Time.timeScale = 1f;
            StopTransientShot();
            stage = 0;
            shotInStage = 0;
            totalShots = 0;
            score = 0;
            hitCount = 0;
            successfulShots = 0;
            destroyedCount = 0;
            campaignStarting = true;

            ConfigureStage(false);

            // The first range is already created in Awake. A restart from the
            // summary may still need to restore stage zero.
            if (worldStageIndex != stage)
            {
                world.BuildStage(stage, difficulty);
                worldStageIndex = stage;
                ResetCameraForBriefing();
            }

            campaignStarting = false;

            // START now means start: enter the scope in the same invocation.
            // There is no asynchronous preparation state that Android can
            // leave disabled. Briefings remain between later campaign stages.
            screen = GameScreen.Briefing;
            EnterRange();
        }

        public void RestartCampaign() => StartCampaign();

        public void OpenHelp()
        {
            if (screen != GameScreen.Menu) return;
            screen = GameScreen.Help;
            hud.ShowHelp();
        }

        public void CloseHelp()
        {
            if (screen != GameScreen.Help) return;
            screen = GameScreen.Menu;
            ResetCameraForMenu();
            hud.ShowMenu(highScore, difficulty);
        }

        public void OpenMenu()
        {
            if (screen == GameScreen.Help)
            {
                CloseHelp();
                return;
            }

            Time.timeScale = 1f;
            campaignStarting = false;
            StopTransientShot();
            screen = GameScreen.Menu;
            holdingBreath = false;
            stage = 0;
            ResetCameraForMenu();
            hud.ShowMenu(highScore, difficulty);
        }

        public void EnterRange()
        {
            if (screen != GameScreen.Briefing) return;
            screen = GameScreen.Playing;
            ResetAim();
            UpdateWind();
            UpdatePlayerCamera();
            RefreshGameplayHud(true);
            hud.ShowGameplay(BuildHudSnapshot(true), false);
        }

        public void AdjustElevation(float deltaMil)
        {
            if (screen != GameScreen.Playing) return;
            elevationDialMil = Mathf.Clamp(elevationDialMil + deltaMil, 0f, 15f);
            RefreshGameplayHud(true);
        }

        public void AdjustWindage(float deltaMil)
        {
            if (screen != GameScreen.Playing) return;
            windageDialMil = Mathf.Clamp(windageDialMil + deltaMil, -10f, 10f);
            RefreshGameplayHud(true);
        }

        public void AdjustZoom(int direction)
        {
            if (screen != GameScreen.Playing) return;
            zoomIndex = Mathf.Clamp(zoomIndex + direction, 0, GameRules.ZoomLevels.Length - 1);
            PlayerPrefs.SetInt("zoom_index", zoomIndex);
            ApplyScopeFov();
            RefreshGameplayHud(true);
        }

        public void SetBreath(bool held)
        {
            if (screen != GameScreen.Playing && screen != GameScreen.Flight)
            {
                holdingBreath = false;
                return;
            }
            holdingBreath = held && breath > 0.01f;
        }

        public void DragAim(Vector2 screenDelta)
        {
            if (screen != GameScreen.Playing && screen != GameScreen.Flight) return;
            float fov = BaseScopeFov / GameRules.ZoomLevels[zoomIndex];
            float degreesPerPixel = fov / Mathf.Max(360f, Screen.height);
            aimYawDegrees += screenDelta.x * degreesPerPixel * 0.92f;
            aimPitchDegrees += screenDelta.y * degreesPerPixel * 0.92f;
            float maxAngle = 13f * MilToDegrees;
            aimYawDegrees = Mathf.Clamp(aimYawDegrees, -maxAngle, maxAngle);
            aimPitchDegrees = Mathf.Clamp(aimPitchDegrees, -maxAngle, maxAngle);
        }

        public void Fire()
        {
            if (screen != GameScreen.Playing || shotInStage >= GameRules.ShotsPerStage) return;

            BallisticSolution solution = Ballistics.Solve(range, currentWind);
            Vector2 aimPoint = EffectiveAimPointAtRange();
            float metresPerMil = range / 1000f;
            Vector3 impact = new Vector3(
                aimPoint.x + (float)solution.WindDriftMetres + windageDialMil * metresPerMil,
                aimPoint.y - (float)solution.DropMetres + elevationDialMil * metresPerMil,
                range);

            firingCameraPosition = playerCamera.transform.position;
            firingCameraRotation = playerCamera.transform.rotation;
            firingCameraFov = playerCamera.fieldOfView;
            currentShot = new ShotRecord
            {
                Start = playerCamera.transform.position + playerCamera.transform.forward * 0.62f + Vector3.down * 0.035f,
                Impact = impact,
                TargetCentre = new Vector3(0f, CameraHeight, range),
                Solution = solution,
                VisualDuration = Ballistics.VisualFlightSeconds(range),
                WindMetresPerSecond = currentWind,
                RangeMetres = range
            };
            shotSceneClock = sceneClock;
            flightElapsed = 0f;
            screen = GameScreen.Flight;

            recoilMilX = 0.06f + Random.value * 0.08f;
            recoilMilY = 0.18f + Random.value * 0.09f;
            recoilVelocityX = -2f + Random.value * 8f;
            recoilVelocityY = 28f + Random.value * 6f;
            PlaySound("shot", 0.88f, 1f);
            SpawnMuzzleFlash(currentShot.Start);
            LaunchTracer();
            hud.ShowGameplay(BuildHudSnapshot(false), true);
        }

        public void ContinueAfterResult()
        {
            if (screen != GameScreen.Result || !resultShown) return;
            if (deferredSteelHide != null) deferredSteelHide.SetDestroyed(true);
            deferredSteelHide = null;
            RemoveImpactMarker();

            if (shotInStage < GameRules.ShotsPerStage)
            {
                if (targetsCleared >= GameRules.TargetsPerStage && !bonusMode)
                {
                    bonusMode = true;
                    destructionStreak = 0;
                    world.ShowBonusTarget();
                }
                ResetAim();
                screen = GameScreen.Playing;
                RefreshGameplayHud(true);
                hud.ShowGameplay(BuildHudSnapshot(true), false);
                return;
            }

            if (stage < GameRules.Stages - 1)
            {
                stage++;
                ConfigureStage();
                screen = GameScreen.Briefing;
                ShowBriefing();
                return;
            }

            if (score > highScore)
            {
                highScore = score;
                PlayerPrefs.SetInt("high_score", highScore);
                PlayerPrefs.Save();
            }
            screen = GameScreen.Summary;
            ResetCameraForMenu();
            hud.ShowSummary(new SummarySnapshot
            {
                Score = score,
                HighScore = highScore,
                HitCount = hitCount,
                DestroyedCount = destroyedCount,
                TotalShots = totalShots,
                SuccessfulShots = successfulShots
            });
        }

        public void TogglePause()
        {
            if (screen == GameScreen.Paused)
            {
                ResumeGame();
                return;
            }
            if (screen != GameScreen.Playing && screen != GameScreen.Flight) return;
            resumeScreen = screen;
            screen = GameScreen.Paused;
            holdingBreath = false;
            Time.timeScale = 0f;
            hud.ShowPause();
        }

        public void ResumeGame()
        {
            if (screen != GameScreen.Paused) return;
            Time.timeScale = 1f;
            screen = resumeScreen;
            hud.ShowGameplay(BuildHudSnapshot(screen == GameScreen.Playing), screen == GameScreen.Flight);
        }

        private void ConfigureApplication()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = Mathf.Max(QualitySettings.antiAliasing, 4);
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.shadowDistance = 1200f;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.pixelLightCount = 6;
            QualitySettings.lodBias = Mathf.Max(QualitySettings.lodBias, 1.65f);
            QualitySettings.maximumLODLevel = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Input.multiTouchEnabled = true;
            if (Application.isMobilePlatform)
            {
                Screen.autorotateToPortrait = false;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = true;
                Screen.orientation = ScreenOrientation.AutoRotation;
            }
        }

        private void CreateCamera()
        {
            GameObject cameraObject = new GameObject("Sniper Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(transform, false);
            playerCamera = cameraObject.GetComponent<Camera>();
            playerCamera.clearFlags = CameraClearFlags.Skybox;
            playerCamera.nearClipPlane = 0.03f;
            playerCamera.farClipPlane = 1800f;
            playerCamera.allowHDR = true;
            playerCamera.allowMSAA = true;
            playerCamera.depthTextureMode = DepthTextureMode.Depth;
            playerCamera.transform.position = new Vector3(0f, CameraHeight, -0.55f);
            playerCamera.gameObject.AddComponent<SceneToneMapper>();
        }

        private void CreateWorld()
        {
            GameObject worldObject = new GameObject("3D Range World");
            worldObject.transform.SetParent(transform, false);
            world = worldObject.AddComponent<RangeWorld>();
            world.Initialize();
        }

        private void CreateAudio()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            string[] names =
            {
                "shot", "hit", "glass_break", "clay_break", "cans_crash",
                "wood_break", "melon_splat", "explosion", "bullseye"
            };
            for (int i = 0; i < names.Length; i++)
            {
                AudioClip clip = Resources.Load<AudioClip>("BallisticSniper/Audio/" + names[i]);
                if (clip != null) clips[names[i]] = clip;
            }
        }

        private void CreateHud()
        {
            GameObject hudObject = new GameObject("Interface");
            hudObject.transform.SetParent(transform, false);
            hud = hudObject.AddComponent<MobileHud>();
            hud.Initialize(this);
        }

        private void CreateKillCam()
        {
            killCam = gameObject.AddComponent<KillCamDirector>();
            Material bulletMaterial = world.Materials.Solid(new Color(1f, 0.67f, 0.12f), true, "_Tracer");
            killCam.Initialize(playerCamera, bulletMaterial);
        }

        private void ConfigureStage(bool rebuildWorld = true)
        {
            StageDefinition definition = GameRules.StageDefinitions[stage];
            range = definition.RangeMetres;
            shotInStage = 0;
            targetsCleared = 0;
            elevationDialMil = 0f;
            windageDialMil = 0f;
            bonusMode = false;
            destructionStreak = 0;
            roundDestructiblesTotal = 0;
            roundDestructiblesDestroyed = 0;
            demolitionBonusAwarded = false;
            lastChainReaction = 0;
            lastBullseye = false;
            sceneClock = 0f;

            for (int i = 0; i < definition.Targets.Length; i++)
            {
                if (definition.Targets[i] != TargetKind.Steel) roundDestructiblesTotal++;
            }

            float maxWind = 1.3f + stage * 1.25f;
            baseWind = Mathf.Lerp(-maxWind, maxWind, (float)random.NextDouble());
            if (stage > 0 && Mathf.Abs(baseWind) < 0.75f)
            {
                float sign = baseWind < 0f ? -1f : 1f;
                baseWind = sign * Mathf.Lerp(0.75f, 1.20f, (float)random.NextDouble());
            }
            currentWind = baseWind;
            displayedSolution = Ballistics.Solve(range, currentWind);

            if (rebuildWorld)
            {
                world.BuildStage(stage, difficulty);
                worldStageIndex = stage;
            }
            ResetAim();
            ResetCameraForBriefing();
        }

        private void ShowBriefing()
        {
            StageDefinition definition = GameRules.StageDefinitions[stage];
            BallisticSolution solution = Ballistics.Solve(range, baseWind);
            hud.ShowBriefing(stage, definition, baseWind, solution);
        }

        private void ResetAim()
        {
            aimYawDegrees = 0f;
            aimPitchDegrees = 0f;
            swayMilX = 0f;
            swayMilY = 0f;
            recoilMilX = 0f;
            recoilMilY = 0f;
            recoilVelocityX = 0f;
            recoilVelocityY = 0f;
            breath = Mathf.Max(breath, 0.72f);
            holdingBreath = false;
            lastShotScore = 0;
            lastHitDestructible = false;
            lastDemolitionBonus = false;
            lastChainReaction = 0;
            lastBullseye = false;
            deferredSteelHide = null;
            deferredBonusImpact = false;
            resultShown = false;
            RemoveImpactMarker();
            playerCamera.transform.position = new Vector3(0f, CameraHeight, -0.55f);
            ApplyScopeFov();
        }

        private void UpdateWind()
        {
            float gust = difficulty == Difficulty.Cadet ? 0f :
                Mathf.Sin(sceneClock * 0.81f + stage * 1.7f) * (difficulty == Difficulty.Shooter ? 0.22f : 0.62f);
            currentWind = baseWind + gust;
            displayedSolution = Ballistics.Solve(range, currentWind);
        }

        private void UpdateSway()
        {
            float scale = difficulty == Difficulty.Cadet ? 0f : difficulty == Difficulty.Shooter ? 0.16f : 0.28f;
            if (holdingBreath && breath > 0.02f) scale *= 0.22f;
            swayMilX = (Mathf.Sin(sceneClock * 1.43f) + 0.32f * Mathf.Sin(sceneClock * 3.11f)) * scale;
            swayMilY = (Mathf.Cos(sceneClock * 1.09f) + 0.28f * Mathf.Sin(sceneClock * 2.57f)) * scale;
        }

        private void UpdateBreath(float dt)
        {
            if (holdingBreath && breath > 0f)
            {
                breath = Mathf.Max(0f, breath - dt / (difficulty == Difficulty.Expert ? 3.8f : 5f));
                if (breath <= 0f) holdingBreath = false;
            }
            else
            {
                breath = Mathf.Min(1f, breath + dt / 4f);
            }
        }

        private void UpdateRecoil(float dt)
        {
            const float spring = 175f;
            const float damping = 19f;
            recoilVelocityX += (-spring * recoilMilX - damping * recoilVelocityX) * dt;
            recoilVelocityY += (-spring * recoilMilY - damping * recoilVelocityY) * dt;
            recoilMilX += recoilVelocityX * dt;
            recoilMilY += recoilVelocityY * dt;
            if (Mathf.Abs(recoilMilX) < 0.001f && Mathf.Abs(recoilVelocityX) < 0.01f)
            {
                recoilMilX = 0f;
                recoilVelocityX = 0f;
            }
            if (Mathf.Abs(recoilMilY) < 0.001f && Mathf.Abs(recoilVelocityY) < 0.01f)
            {
                recoilMilY = 0f;
                recoilVelocityY = 0f;
            }
        }

        private void UpdatePlayerCamera()
        {
            float yaw = aimYawDegrees + (swayMilX + recoilMilX) * MilToDegrees;
            float pitch = aimPitchDegrees + (swayMilY + recoilMilY) * MilToDegrees;
            playerCamera.transform.position = new Vector3(0f, CameraHeight, -0.55f);
            playerCamera.transform.rotation = Quaternion.Euler(-pitch, yaw, 0f);
            ApplyScopeFov();
        }

        private void ApplyScopeFov()
        {
            float fov = BaseScopeFov / GameRules.ZoomLevels[zoomIndex];
            playerCamera.fieldOfView = fov;
            float canvasHeight = hud != null ? hud.CanvasHeight : 1080f;
            float focalPixels = canvasHeight / (2f * Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f));
            float pixelsPerMil = focalPixels * 0.001f;
            hud?.SetReticleScale(pixelsPerMil, GameRules.ZoomLevels[zoomIndex]);
        }

        private Vector2 EffectiveAimPointAtRange()
        {
            float yaw = aimYawDegrees + (swayMilX + recoilMilX) * MilToDegrees;
            float pitch = aimPitchDegrees + (swayMilY + recoilMilY) * MilToDegrees;
            return new Vector2(
                Mathf.Tan(yaw * Mathf.Deg2Rad) * range,
                CameraHeight + Mathf.Tan(pitch * Mathf.Deg2Rad) * range);
        }

        private void LaunchTracer()
        {
            GameObject bullet = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bullet.name = "Visible .308 Tracer";
            bullet.transform.SetParent(transform, true);
            activeProjectile = bullet.AddComponent<ProjectileTracer>();
            activeProjectile.Begin(
                currentShot,
                world.Materials.Solid(new Color(1f, 0.67f, 0.12f), true, "_Tracer"),
                ResolveShot);
        }

        private void ResolveShot()
        {
            if (screen != GameScreen.Flight) return;
            sceneClock = shotSceneClock + (float)currentShot.Solution.TimeSeconds;
            world.TickTargets(sceneClock);
            activeProjectile = null;

            lastShotScore = 0;
            lastBullseye = false;
            lastHitDestructible = false;
            lastDemolitionBonus = false;
            lastBonusShot = bonusMode;
            lastChainReaction = 0;

            TargetActor nearest = world.FindBestTarget(currentShot.Impact, out float normalizedDistance);
            lastReviewedTarget = nearest;
            if (nearest != null)
            {
                currentShot.TargetCentre = nearest.Centre;
                lastError = nearest.ErrorFromCentre(currentShot.Impact);
            }
            else
            {
                currentShot.TargetCentre = new Vector3(0f, CameraHeight, range);
                lastError = new Vector2(currentShot.Impact.x, currentShot.Impact.y - CameraHeight);
            }

            bool hit = nearest != null && nearest.ContainsImpact(currentShot.Impact);
            if (bonusMode)
            {
                int ringScore = hit ? GameRules.SteelScore(lastError.x, lastError.y) : 0;
                if (ringScore > 0)
                {
                    lastShotScore = ringScore * 2;
                    lastBullseye = ringScore == 10;
                    if (lastBullseye) deferredBonusImpact = true;
                    else world.AddBonusImpact(lastError);
                    PlaySound("hit", 0.84f, lastShotScore >= 20 ? 1.25f : 1f);
                }
            }
            else if (hit)
            {
                targetsCleared++;
                hitCount++;
                if (nearest.Kind == TargetKind.Steel)
                {
                    destructionStreak = 0;
                    lastShotScore = GameRules.SteelScore(lastError.x, lastError.y);
                    lastBullseye = lastShotScore == 10;
                    deferredSteelHide = nearest;
                    PlaySound("hit", 0.84f, lastBullseye ? 1.25f : 1f);
                }
                else
                {
                    lastHitDestructible = true;
                    destructionStreak++;
                    lastShotScore = GameRules.DestructionScore(destructionStreak);
                    roundDestructiblesDestroyed++;
                    destroyedCount++;
                    bool explosive = nearest.Kind == TargetKind.ExplosiveBarrel;
                    world.DestroyTargetVisual(nearest, explosive);
                    PlayTargetSound(nearest.Kind);
                    if (explosive) TriggerChainReaction(nearest);
                    if (roundDestructiblesDestroyed >= roundDestructiblesTotal && !demolitionBonusAwarded)
                    {
                        demolitionBonusAwarded = true;
                        lastDemolitionBonus = true;
                        lastShotScore += 20;
                    }
                }
            }
            else
            {
                destructionStreak = 0;
            }

            if (lastShotScore > 0) successfulShots++;
            score += lastShotScore;
            totalShots++;
            shotInStage++;
            CalculateReview();
            if (!lastBullseye) SpawnImpactMarker();

            if (lastBullseye)
            {
                BeginKillCam();
            }
            else
            {
                float delay = lastHitDestructible ? (lastReviewedTarget != null && lastReviewedTarget.Kind == TargetKind.ExplosiveBarrel ? 1.55f : 1.25f) : 0.30f;
                EnterResult(delay);
            }
        }

        private void TriggerChainReaction(TargetActor source)
        {
            float chainRadius = range / 1000f * 3.45f;
            IReadOnlyList<TargetActor> actors = world.Targets;
            for (int i = 0; i < actors.Count; i++)
            {
                TargetActor target = actors[i];
                if (target == source || target.Destroyed || target.Kind == TargetKind.Steel) continue;
                if (Vector2.Distance(new Vector2(target.Centre.x, target.Centre.y), new Vector2(source.Centre.x, source.Centre.y)) > chainRadius) continue;
                targetsCleared++;
                hitCount++;
                destroyedCount++;
                roundDestructiblesDestroyed++;
                destructionStreak++;
                lastShotScore += GameRules.DestructionScore(destructionStreak);
                lastChainReaction++;
                world.DestroyTargetVisual(target, target.Kind == TargetKind.ExplosiveBarrel);
            }
        }

        private void CalculateReview()
        {
            float metresPerMil = range / 1000f;
            float separationMil = lastError.magnitude / metresPerMil;
            float targetRadius = lastReviewedTarget == null ? 0.58f :
                lastReviewedTarget.Kind == TargetKind.Steel ? 0.58f : Mathf.Max(GameRules.TargetSize(lastReviewedTarget.Kind).x, GameRules.TargetSize(lastReviewedTarget.Kind).y) * 0.62f;
            float targetRadiusMil = targetRadius / metresPerMil;
            float fov = BaseScopeFov / GameRules.ZoomLevels[zoomIndex];
            float focalPixels = hud.CanvasHeight / (2f * Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f));
            float pixelsPerMil = focalPixels * 0.001f;
            float visibleRadiusMil = hud.ScopeRadius * 0.56f / Mathf.Max(0.1f, pixelsPerMil);
            reviewZoom = Mathf.Clamp(visibleRadiusMil / Mathf.Max(0.55f, targetRadiusMil + separationMil * 0.55f), 1f, 3f);

            reviewStartRotation = firingCameraRotation;
            Vector3 reviewCentre = (currentShot.Impact + currentShot.TargetCentre) * 0.5f;
            Vector3 direction = reviewCentre - firingCameraPosition;
            reviewTargetRotation = direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction.normalized, Vector3.up) : firingCameraRotation;
            reviewStartFov = firingCameraFov;
            reviewTargetFov = firingCameraFov / reviewZoom;
        }

        private void BeginKillCam()
        {
            int variant = random.Next(GameRules.CinematicNames.Length);
            if (variant == previousCinematicVariant)
            {
                variant = (variant + 1 + random.Next(GameRules.CinematicNames.Length - 1)) % GameRules.CinematicNames.Length;
            }
            previousCinematicVariant = variant;
            screen = GameScreen.Cinematic;
            holdingBreath = false;
            hud.ShowCinematic(variant);
            killCam.Begin(currentShot, variant, OnKillCamComplete);
        }

        private void OnKillCamComplete()
        {
            PlaySound("bullseye", 0.80f, 1f);
            if (deferredBonusImpact)
            {
                world.AddBonusImpact(lastError);
                deferredBonusImpact = false;
            }
            SpawnImpactMarker();
            playerCamera.transform.position = firingCameraPosition;
            playerCamera.transform.rotation = reviewStartRotation;
            playerCamera.fieldOfView = reviewStartFov;
            EnterResult(0f);
        }

        private void EnterResult(float revealDelay)
        {
            screen = GameScreen.Result;
            holdingBreath = false;
            reviewStartedAt = Time.unscaledTime;
            resultRevealAt = Time.unscaledTime + revealDelay;
            resultShown = revealDelay <= 0f;
            if (resultShown) hud.ShowResult(BuildResultSnapshot());
            else hud.ShowImpactWait(BuildHudSnapshot(false));
        }

        private void UpdateReviewCamera()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - reviewStartedAt) / 0.65f);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            playerCamera.transform.position = firingCameraPosition;
            playerCamera.transform.rotation = Quaternion.Slerp(reviewStartRotation, reviewTargetRotation, eased);
            playerCamera.fieldOfView = Mathf.Lerp(reviewStartFov, reviewTargetFov, eased);
        }

        private ResultSnapshot BuildResultSnapshot()
        {
            string headline;
            if (lastBullseye) headline = "ЯБЛОЧКО";
            else if (lastChainReaction > 0) headline = "ЦЕПЬ ×" + (lastChainReaction + 1);
            else if (lastDemolitionBonus) headline = "РАЗРУШИТЕЛЬ";
            else if (lastHitDestructible && destructionStreak > 1) headline = "КОМБО ×" + destructionStreak;
            else if (lastHitDestructible) headline = "РАЗНЕСЕНО";
            else if (lastBonusShot && lastShotScore > 0) headline = "БОНУС ×2";
            else headline = lastShotScore >= 10 ? "ДЕСЯТКА" : lastShotScore == 7 ? "ТОЧНО" : lastShotScore == 4 ? "ЗАЧЁТ" : "ПРОМАХ";

            float metresPerMil = range / 1000f;
            string targetNote = lastChainReaction > 0 && lastReviewedTarget != null
                ? GameRules.TargetName(lastReviewedTarget.Kind) + " • +" + lastChainReaction + " ЦЕЛЬ"
                : lastHitDestructible && lastReviewedTarget != null
                    ? GameRules.TargetName(lastReviewedTarget.Kind)
                    : lastBonusShot ? "БОНУСНАЯ СТАЛЬ • ОЧКИ ×2"
                    : lastShotScore > 0 ? "СТАЛЬНАЯ МИШЕНЬ" : "МЕТКА — МЕСТО ПУЛИ";

            bool stageFinished = shotInStage >= GameRules.ShotsPerStage;
            string actionLabel = stageFinished
                ? stage == GameRules.Stages - 1 ? "РЕЗУЛЬТАТ" : "СЛЕДУЮЩИЕ " + GameRules.StageDefinitions[stage + 1].RangeMetres + " м"
                : targetsCleared >= GameRules.TargetsPerStage && !bonusMode ? "БОНУСНАЯ МИШЕНЬ"
                : bonusMode ? "ЕЩЁ БОНУСНЫЙ" : "К ЦЕЛЯМ";

            return new ResultSnapshot
            {
                Headline = headline,
                Points = lastShotScore,
                ErrorCentimetres = lastError.magnitude * 100f,
                ErrorMilX = lastError.x / metresPerMil,
                ErrorMilY = lastError.y / metresPerMil,
                TargetNote = targetNote,
                ReviewZoom = reviewZoom,
                ActionLabel = actionLabel
            };
        }

        private HudSnapshot BuildHudSnapshot(bool canFire)
        {
            return new HudSnapshot
            {
                Stage = stage,
                Range = Mathf.RoundToInt(range),
                Wind = currentWind,
                Solution = displayedSolution,
                ElevationDial = elevationDialMil,
                WindageDial = windageDialMil,
                Zoom = GameRules.ZoomLevels[zoomIndex],
                Breath = breath,
                HoldingBreath = holdingBreath,
                TargetsCleared = targetsCleared,
                ShotsRemaining = GameRules.ShotsPerStage - shotInStage,
                Score = score,
                BonusMode = bonusMode,
                CanFire = canFire && screen == GameScreen.Playing,
                Difficulty = difficulty
            };
        }

        private void RefreshGameplayHud(bool canFire)
        {
            if (screen != GameScreen.Playing && screen != GameScreen.Flight) return;
            hud.UpdateGameplay(BuildHudSnapshot(canFire));
        }

        private void SpawnImpactMarker()
        {
            RemoveImpactMarker();
            impactMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impactMarker.name = "Impact Review Marker";
            impactMarker.transform.SetParent(transform, true);
            impactMarker.transform.position = currentShot.Impact + Vector3.back * 0.10f;
            impactMarker.transform.localScale = Vector3.one * Mathf.Clamp(range / 900f * 0.08f, 0.035f, 0.08f);
            Renderer renderer = impactMarker.GetComponent<Renderer>();
            renderer.sharedMaterial = world.Materials.Solid(new Color(1f, 0.055f, 0.025f), true, "_ImpactMarker");
            Collider collider = impactMarker.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }

        private void RemoveImpactMarker()
        {
            if (impactMarker != null) Destroy(impactMarker);
            impactMarker = null;
        }

        private void SpawnMuzzleFlash(Vector3 position)
        {
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "Muzzle Flash";
            flash.transform.SetParent(transform, true);
            flash.transform.position = position;
            Renderer renderer = flash.GetComponent<Renderer>();
            renderer.sharedMaterial = world.Materials.Solid(new Color(1f, 0.61f, 0.08f), true, "_Muzzle");
            Collider collider = flash.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            PointFlash point = flash.AddComponent<PointFlash>();
            point.StartScale = 0.08f;
            point.EndScale = 0.9f;
            point.Lifetime = 0.11f;
        }

        private void PlayTargetSound(TargetKind kind)
        {
            switch (kind)
            {
                case TargetKind.GlassBottle: PlaySound("glass_break", 0.95f, 1f); break;
                case TargetKind.ClayJug: PlaySound("clay_break", 0.95f, 1f); break;
                case TargetKind.Cans: PlaySound("cans_crash", 0.95f, 1f); break;
                case TargetKind.WoodenCrate: PlaySound("wood_break", 0.95f, 1f); break;
                case TargetKind.Watermelon: PlaySound("melon_splat", 0.95f, 1f); break;
                case TargetKind.ExplosiveBarrel: PlaySound("explosion", 1f, 1f); break;
            }
        }

        private void PlaySound(string name, float volume, float pitch)
        {
            if (!clips.TryGetValue(name, out AudioClip clip) || clip == null) return;
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, volume);
        }

        private void ResetCameraForMenu()
        {
            playerCamera.transform.position = new Vector3(0f, 4.2f, -10f);
            playerCamera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.8f, 135f) - playerCamera.transform.position, Vector3.up);
            playerCamera.fieldOfView = 43f;
        }

        private void ResetCameraForBriefing()
        {
            playerCamera.transform.position = new Vector3(0f, CameraHeight, -0.55f);
            playerCamera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, CameraHeight, range) - playerCamera.transform.position, Vector3.up);
            playerCamera.fieldOfView = Mathf.Clamp(18f + stage * 1.4f, 18f, 24f);
        }

        private void UpdateMenuCamera()
        {
            float time = Time.unscaledTime;
            Vector3 position = new Vector3(Mathf.Sin(time * 0.13f) * 3.2f, 4.1f + Mathf.Sin(time * 0.21f) * 0.35f, -9.5f);
            playerCamera.transform.position = position;
            Vector3 lookAt = new Vector3(Mathf.Sin(time * 0.11f) * 4f, 2.1f, 125f);
            playerCamera.transform.rotation = Quaternion.LookRotation(lookAt - position, Vector3.up);
            playerCamera.fieldOfView = 43f;
        }

        private void UpdateBriefingCamera()
        {
            Vector3 position = new Vector3(Mathf.Sin(Time.unscaledTime * 0.18f) * 0.35f, CameraHeight + 0.12f, -0.55f);
            playerCamera.transform.position = position;
            playerCamera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, CameraHeight, range) - position, Vector3.up);
        }

        private void HandleKeyboardInput(float dt)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (screen == GameScreen.Menu) return;
                if (screen == GameScreen.Help) CloseHelp();
                else if (screen == GameScreen.Briefing || screen == GameScreen.Summary) OpenMenu();
                else TogglePause();
            }

            if (screen != GameScreen.Playing && screen != GameScreen.Flight) return;

            float keyboardAim = 420f * dt;
            Vector2 delta = new Vector2(
                (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? keyboardAim : 0f) -
                (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? keyboardAim : 0f),
                (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? keyboardAim : 0f) -
                (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? keyboardAim : 0f));
            if (delta.sqrMagnitude > 0f) DragAim(delta);
            if (Input.GetKeyDown(KeyCode.Space)) Fire();
            if (Input.GetKeyDown(KeyCode.Q)) AdjustElevation(-0.5f);
            if (Input.GetKeyDown(KeyCode.E)) AdjustElevation(0.5f);
            if (Input.GetKeyDown(KeyCode.Z)) AdjustWindage(-0.5f);
            if (Input.GetKeyDown(KeyCode.X)) AdjustWindage(0.5f);
            if (Input.mouseScrollDelta.y > 0f) AdjustZoom(1);
            if (Input.mouseScrollDelta.y < 0f) AdjustZoom(-1);
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)) SetBreath(true);
            if (Input.GetKeyUp(KeyCode.LeftShift) || Input.GetKeyUp(KeyCode.RightShift)) SetBreath(false);
        }

        private void StopTransientShot()
        {
            if (activeProjectile != null) Destroy(activeProjectile.gameObject);
            activeProjectile = null;
            if (killCam != null && killCam.Active) killCam.StopImmediately();
            RemoveImpactMarker();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && (screen == GameScreen.Playing || screen == GameScreen.Flight)) TogglePause();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
        }
    }
}
