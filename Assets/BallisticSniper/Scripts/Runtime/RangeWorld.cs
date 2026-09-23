using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BallisticSniper
{
    public sealed class RangeWorld : MonoBehaviour
    {
        private readonly List<TargetActor> targets = new List<TargetActor>();
        private readonly List<HumanMissionActor> humans = new List<HumanMissionActor>();
        private readonly List<GameObject> transientObjects = new List<GameObject>();
        private MaterialLibrary materials;
        private Transform stageRoot;
        private Light sun;
        private Light skyFill;
        private Material skyboxMaterial;
        private Material missionSkyboxMaterial;
        private int currentStage;
        private float currentRange;
        private CampaignMode currentMode;
        private Transform escapeVehicle;
        private Transform escapeDoorPivot;
        private Quaternion escapeDoorClosedRotation;
        private readonly List<GameObject> escapeGlassPanels = new List<GameObject>();
        private GameObject hotelWindowGlass;
        private HumanMissionActor escapeSurvivor;
        private Vector3 escapeVehicleStart;
        private float escapeStartClock;
        private bool escapeActive;
        private bool escapeTargetLost;

        private const float EscapeReactionSeconds = 1.0f;
        private const float EscapeRunSeconds = 1.85f;
        private const float EscapeBoardSeconds = 1.10f;
        private const float EscapeDriveDelay = 4.35f;

        public IReadOnlyList<TargetActor> Targets => targets;
        public IReadOnlyList<HumanMissionActor> Humans => humans;
        public HumanMissionActor PrimaryHuman { get; private set; }
        public TargetActor BonusTarget { get; private set; }
        public MaterialLibrary Materials => materials;
        public bool EscapeTargetLost => escapeTargetLost;
        public bool EscapeSequenceActive => escapeActive;

        public void Initialize()
        {
            materials = new MaterialLibrary();
            stageRoot = new GameObject("Stage Geometry").transform;
            stageRoot.SetParent(transform, false);
            CreateLighting();
        }

        public void BuildStage(int stageIndex, Difficulty difficulty)
        {
            BuildStage(stageIndex, difficulty, CampaignMode.Range);
        }

        public void BuildStage(int stageIndex, Difficulty difficulty, CampaignMode mode)
        {
            ClearStage();
            currentMode = mode;
            if (mode == CampaignMode.Operations)
            {
                currentStage = Mathf.Clamp(stageIndex, 0, GameRules.OperationDefinitions.Length - 1);
                OperationDefinition operation = GameRules.OperationDefinitions[currentStage];
                currentRange = operation.RangeMetres;
                int environmentStage = currentStage == 0 ? 1 : currentStage == 1 ? 3 : 4;
                ConfigureAtmosphere(environmentStage);
                CreateMissionEnvironment(operation, currentStage);
                CreateOperationSetpiece(operation, currentStage);
            }
            else
            {
                currentStage = Mathf.Clamp(stageIndex, 0, GameRules.StageDefinitions.Length - 1);
                StageDefinition definition = GameRules.StageDefinitions[currentStage];
                currentRange = definition.RangeMetres;
                ConfigureAtmosphere(currentStage);
                CreateTerrain(currentStage, currentRange);
                CreateEnvironment(currentStage, currentRange);
                CreateGroundScatter(currentStage, currentRange);
                CreateRangeFurniture(currentStage, currentRange);
                CreateTargets(definition, difficulty);
            }
        }

        public void TickTargets(float clock)
        {
            if (escapeActive && escapeVehicle != null)
            {
                float elapsed = Mathf.Max(0f, clock - escapeStartClock);

                if (escapeDoorPivot != null)
                {
                    float doorOpen;
                    if (elapsed < 2.18f) doorOpen = 0f;
                    else if (elapsed < 2.62f)
                        doorOpen = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.18f, 2.62f, elapsed));
                    else if (elapsed < 3.95f) doorOpen = 1f;
                    else if (elapsed < EscapeDriveDelay)
                        doorOpen = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3.95f, EscapeDriveDelay, elapsed));
                    else doorOpen = 0f;

                    escapeDoorPivot.localRotation =
                        escapeDoorClosedRotation * Quaternion.Euler(0f, -72f * doorOpen, 0f);
                }

                if (elapsed >= EscapeDriveDelay)
                {
                    float driveTime = elapsed - EscapeDriveDelay;
                    float distance = Mathf.Min(15.5f, driveTime * 4.25f);
                    Vector3 position = escapeVehicleStart;
                    position.x -= distance;
                    position.z += Mathf.Sin(driveTime * 1.25f) * 0.16f;
                    escapeVehicle.position = position;
                    if (distance >= 14.0f) escapeTargetLost = true;
                }
            }

            for (int i = 0; i < targets.Count; i++) targets[i].Tick(clock);
            if (BonusTarget != null && BonusTarget.gameObject.activeSelf) BonusTarget.Tick(clock);
            for (int i = 0; i < humans.Count; i++) humans[i].Tick(clock);
        }

        public bool BeginEscapeAfterFirstTarget(HumanMissionActor hitActor, float clock)
        {
            if (currentMode != CampaignMode.Operations ||
                currentStage < 0 ||
                currentStage >= GameRules.OperationDefinitions.Length ||
                GameRules.OperationDefinitions[currentStage].Kind != OperationKind.EscapeVehicle ||
                escapeVehicle == null)
                return false;

            HumanMissionActor survivor = null;
            for (int i = 0; i < humans.Count; i++)
            {
                HumanMissionActor candidate = humans[i];
                if (candidate != null && candidate != hitActor && candidate.IsPrimary && !candidate.IsRagdolled)
                {
                    survivor = candidate;
                    break;
                }
            }

            if (survivor == null) return false;
            escapeStartClock = clock;
            escapeActive = true;
            escapeTargetLost = false;
            escapeSurvivor = survivor;

            Vector3 doorEntry = escapeVehicle.TransformPoint(new Vector3(1.24f, -0.44f, 0.18f));
            Vector3 seatLocal = new Vector3(0.34f, -0.80f, 0.08f);
            survivor.BeginVehicleEscape(escapeVehicle, doorEntry, seatLocal, clock);
            Debug.Log("BALLISTIC_ESCAPE_STARTED survivor=" + survivor.name +
                      " reaction=1.00 run=" + EscapeRunSeconds.ToString("0.00") +
                      " clock=" + clock.ToString("0.00"));
            return true;
        }

        public bool TryShatterOperationGlass(Vector3 impactPoint)
        {
            if (currentMode != CampaignMode.Operations) return false;

            if (currentStage == 1 && hotelWindowGlass != null && hotelWindowGlass.activeSelf)
            {
                Renderer hotelRenderer = hotelWindowGlass.GetComponent<Renderer>();
                if (hotelRenderer != null)
                {
                    Bounds bounds = hotelRenderer.bounds;
                    float marginX = 0.055f;
                    float marginY = 0.055f;
                    bool inside =
                        Mathf.Abs(impactPoint.x - bounds.center.x) <= bounds.extents.x + marginX &&
                        Mathf.Abs(impactPoint.y - bounds.center.y) <= bounds.extents.y + marginY;
                    if (inside)
                    {
                        ShatterGlassPanel(hotelWindowGlass, impactPoint, "Hotel Window Shard");
                        return true;
                    }
                }
            }

            // All operation cars now register their glass.  This includes the
            // escape sedan as well as parked street cars, so a visible pane
            // always reacts when the shot lands on it.
            GameObject best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < escapeGlassPanels.Count; i++)
            {
                GameObject panel = escapeGlassPanels[i];
                if (panel == null || !panel.activeSelf) continue;
                Renderer renderer = panel.GetComponent<Renderer>();
                if (renderer == null) continue;

                Bounds bounds = renderer.bounds;
                float extentX = Mathf.Max(0.035f, bounds.extents.x) + 0.11f;
                float extentY = Mathf.Max(0.035f, bounds.extents.y) + 0.09f;
                float nx = Mathf.Abs(impactPoint.x - bounds.center.x) / extentX;
                float ny = Mathf.Abs(impactPoint.y - bounds.center.y) / extentY;
                if (nx > 1f || ny > 1f) continue;

                float score = nx * nx + ny * ny;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = panel;
                }
            }

            if (best != null)
            {
                ShatterGlassPanel(best, impactPoint, "Vehicle Safety Glass Shard");
                return true;
            }

            return false;
        }

        private void ShatterGlassPanel(GameObject panel, Vector3 impactPoint, string shardName)
        {
            if (panel == null || !panel.activeSelf) return;
            Renderer renderer = panel.GetComponent<Renderer>();
            if (renderer == null)
            {
                panel.SetActive(false);
                return;
            }

            Bounds bounds = renderer.bounds;
            panel.SetActive(false);

            // Use a brighter, separately cached shard material.  The previous
            // version reused the almost invisible intact-glass alpha, so the
            // pane disappeared but the player could not read the break.
            Material shardMaterial = materials.TransparentGlass(
                new Color(0.72f, 0.90f, 1.00f, 0.52f));

            Random.State oldState = Random.state;
            Random.InitState(
                currentStage * 2039 +
                Mathf.RoundToInt(impactPoint.x * 97f) +
                Mathf.RoundToInt(impactPoint.y * 131f));

            int shardCount = Mathf.Clamp(
                Mathf.RoundToInt((bounds.size.x + bounds.size.y) * 18f),
                34,
                54);

            for (int i = 0; i < shardCount; i++)
            {
                Vector3 offset = new Vector3(
                    Random.Range(-bounds.extents.x, bounds.extents.x),
                    Random.Range(-bounds.extents.y, bounds.extents.y),
                    Random.Range(-0.024f, 0.024f));
                float size = Random.Range(0.040f, 0.105f);
                GameObject shard = CreatePrimitive(
                    PrimitiveType.Cube, shardName, stageRoot,
                    bounds.center + offset,
                    new Vector3(size, size * Random.Range(0.42f, 1.55f), Random.Range(0.008f, 0.018f)),
                    shardMaterial, Random.rotation, false);
                Rigidbody body = shard.AddComponent<Rigidbody>();
                body.mass = Random.Range(0.010f, 0.032f);
                body.useGravity = true;

                Vector3 away = (bounds.center + offset - impactPoint);
                away.z = -Mathf.Abs(away.z) - 0.35f;
                if (away.sqrMagnitude < 0.01f) away = new Vector3(Random.Range(-0.3f, 0.3f), 0.2f, -1f);
                away.Normalize();
                body.velocity =
                    away * Random.Range(2.2f, 5.4f) +
                    new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(0.9f, 3.4f), Random.Range(-2.2f, -0.6f));
                body.angularVelocity = Random.insideUnitSphere * 18f;

                TimedDestroy timed = shard.AddComponent<TimedDestroy>();
                timed.Lifetime = Random.Range(2.0f, 3.6f);
                transientObjects.Add(shard);
            }

            GameObject flash = CreatePrimitive(
                PrimitiveType.Cylinder, "Glass Impact Spark", stageRoot,
                impactPoint + new Vector3(0f, 0f, -0.035f),
                new Vector3(0.15f, 0.006f, 0.15f),
                materials.Solid(new Color(0.78f, 0.94f, 1f), true, "_GlassImpactFlashV57"),
                Quaternion.Euler(90f, 0f, 0f), false);
            TimedDestroy flashDestroy = flash.AddComponent<TimedDestroy>();
            flashDestroy.Lifetime = 0.14f;
            transientObjects.Add(flash);

            Random.state = oldState;
        }

        public void ShowBonusTarget()
        {
            if (BonusTarget == null) return;
            BonusTarget.ResetTarget(true);
            BonusTarget.gameObject.SetActive(true);
        }

        public TargetActor FindBestTarget(Vector3 impactPoint, out float normalizedDistance)
        {
            TargetActor best = null;
            normalizedDistance = float.MaxValue;
            if (BonusTarget != null && BonusTarget.gameObject.activeSelf)
            {
                normalizedDistance = BonusTarget.NormalizedDistance(impactPoint);
                return BonusTarget;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                TargetActor target = targets[i];
                if (target.Destroyed) continue;
                float distance = target.NormalizedDistance(impactPoint);
                if (distance < normalizedDistance)
                {
                    normalizedDistance = distance;
                    best = target;
                }
            }
            return best;
        }

        public HumanMissionActor FindHumanImpact(Vector3 impactPoint, out float normalizedDistance)
        {
            HumanMissionActor nearest = null;
            HumanMissionActor firstPhysicalHit = null;
            normalizedDistance = float.MaxValue;
            float closestDepth = float.MaxValue;
            for (int i = 0; i < humans.Count; i++)
            {
                HumanMissionActor actor = humans[i];
                if (actor == null || actor.IsRagdolled) continue;
                float distance = actor.NormalizedDistance(impactPoint);
                if (distance < normalizedDistance)
                {
                    normalizedDistance = distance;
                    nearest = actor;
                }
                if (actor.ContainsImpact(impactPoint) && actor.Depth < closestDepth)
                {
                    closestDepth = actor.Depth;
                    firstPhysicalHit = actor;
                }
            }
            return firstPhysicalHit != null ? firstPhysicalHit : nearest;
        }

        public bool IsOperationImpactBlocked(Vector3 impactPoint)
        {
            if (currentMode != CampaignMode.Operations) return false;
            if (currentStage == 1)
            {
                // Only the actual hotel window opening is a valid line of fire.
                return impactPoint.x < -0.72f || impactPoint.x > 0.72f ||
                       impactPoint.y < 1.02f || impactPoint.y > 2.10f;
            }
            if (currentStage == 2)
            {
                if (impactPoint.y < 6.48f) return true;
                if (impactPoint.x > 0.62f && impactPoint.x < 1.52f && impactPoint.y < 7.42f) return true;
            }
            if (currentStage == 3 &&
                escapeActive &&
                escapeSurvivor != null &&
                escapeSurvivor.IsSeatedInVehicle &&
                escapeVehicle != null)
            {
                float lateral = Mathf.Abs(impactPoint.x - escapeVehicle.position.x);
                if (lateral <= 2.35f && (impactPoint.y < 0.72f || impactPoint.y > 1.62f))
                    return true;
            }
            return false;
        }

        public void ApplyHumanImpact(
            HumanMissionActor actor,
            Vector3 impactPoint,
            Vector3 shotDirection,
            float impulse)
        {
            if (actor == null) return;
            Vector3 direction = shotDirection.sqrMagnitude > 0.0001f ? shotDirection.normalized : Vector3.forward;
            SpawnHumanImpactParticles(impactPoint, actor.IsPrimary, direction);
            SpawnHumanBloodMist(impactPoint, direction, actor.IsPrimary);
            actor.ActivateRagdoll(impactPoint, direction, impulse);
        }

        public void DestroyTargetVisual(TargetActor target, bool explosive)
        {
            if (target == null) return;
            Vector3 origin = target.transform.position;
            Material fragmentMaterial = target.PrimaryMaterial;
            int count = explosive ? 26 : 14;
            Random.State oldState = Random.state;
            Random.InitState(currentStage * 7919 + target.Index * 1049 + Mathf.RoundToInt(Time.time * 100f));
            for (int i = 0; i < count; i++)
            {
                PrimitiveType primitive = i % 3 == 0 ? PrimitiveType.Capsule : PrimitiveType.Cube;
                GameObject fragment = GameObject.CreatePrimitive(primitive);
                fragment.name = explosive ? "Barrel Fragment" : "Material Fragment";
                fragment.transform.SetParent(stageRoot, true);
                fragment.transform.position = origin + Random.insideUnitSphere * 0.18f;
                float size = Random.Range(0.035f, explosive ? 0.12f : 0.085f);
                fragment.transform.localScale = new Vector3(size, size * Random.Range(0.35f, 1.4f), size * 0.45f);
                Renderer renderer = fragment.GetComponent<Renderer>();
                renderer.sharedMaterial = fragmentMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                Rigidbody body = fragment.AddComponent<Rigidbody>();
                body.mass = Random.Range(0.03f, 0.22f);
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                Vector3 radial = (Random.insideUnitSphere + Vector3.up * 0.85f).normalized;
                body.velocity = radial * Random.Range(explosive ? 6f : 2f, explosive ? 16f : 7f);
                body.angularVelocity = Random.insideUnitSphere * 18f;
                fragment.AddComponent<TimedDestroy>().Lifetime = Random.Range(2.3f, 4.2f);
                transientObjects.Add(fragment);
            }
            Random.state = oldState;

            SpawnImpactParticles(origin, target.Kind, explosive);
            if (explosive) SpawnExplosionFlash(origin);
            target.SetDestroyed(true);
        }

        public void AddBonusImpact(Vector2 errorMetres)
        {
            if (BonusTarget != null) BonusTarget.AddImpactMark(errorMetres);
        }

        private void ClearStage()
        {
            for (int i = stageRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = stageRoot.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            targets.Clear();
            humans.Clear();
            PrimaryHuman = null;
            BonusTarget = null;
            escapeVehicle = null;
            escapeDoorPivot = null;
            escapeGlassPanels.Clear();
            hotelWindowGlass = null;
            escapeSurvivor = null;
            escapeActive = false;
            escapeTargetLost = false;
            escapeStartClock = 0f;
            transientObjects.Clear();
        }

        private void CreateLighting()
        {
            GameObject sunObject = new GameObject("Directional Sun");
            sunObject.transform.SetParent(transform, false);
            sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.82f;
            sun.shadowBias = 0.080f;
            sun.shadowNormalBias = 0.55f;
            sun.intensity = 1.18f;
            RenderSettings.sun = sun;

            GameObject fillObject = new GameObject("Sky Fill Light");
            fillObject.transform.SetParent(transform, false);
            skyFill = fillObject.AddComponent<Light>();
            skyFill.type = LightType.Directional;
            skyFill.shadows = LightShadows.None;
            skyFill.intensity = 0.20f;

            Shader skyShader = Resources.Load<Shader>("BallisticSniper/Shaders/PanoramaSky") ??
                               Shader.Find("BallisticSniper/PanoramaSky") ??
                               Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                skyboxMaterial = new Material(skyShader) { name = "Runtime Training Panorama Sky" };
                Texture2D panorama = Resources.Load<Texture2D>("BallisticSniper/Textures/range_panorama_v4");
                if (panorama != null && skyboxMaterial.HasProperty("_PanoramaTex"))
                    skyboxMaterial.SetTexture("_PanoramaTex", panorama);
            }

            Shader missionSkyShader = Resources.Load<Shader>("BallisticSniper/Shaders/GradientSky") ??
                                      Shader.Find("BallisticSniper/GradientSky") ??
                                      Shader.Find("Skybox/Procedural");
            if (missionSkyShader != null)
                missionSkyboxMaterial = new Material(missionSkyShader) { name = "Runtime Mission Gradient Sky" };

            RenderSettings.skybox = skyboxMaterial != null ? skyboxMaterial : missionSkyboxMaterial;
        }

        private void ConfigureAtmosphere(int stage)
        {
            Color[] zenithColors =
            {
                new Color(0.055f, 0.14f, 0.30f),
                new Color(0.045f, 0.17f, 0.21f),
                new Color(0.075f, 0.15f, 0.28f),
                new Color(0.035f, 0.08f, 0.14f),
                new Color(0.075f, 0.18f, 0.36f)
            };
            Color[] horizonColors =
            {
                new Color(0.34f, 0.44f, 0.52f),
                new Color(0.28f, 0.40f, 0.38f),
                new Color(0.46f, 0.30f, 0.18f),
                new Color(0.20f, 0.27f, 0.30f),
                new Color(0.40f, 0.49f, 0.58f)
            };
            Color[] fogColors =
            {
                new Color(0.26f, 0.31f, 0.35f),
                new Color(0.24f, 0.34f, 0.31f),
                new Color(0.40f, 0.27f, 0.17f),
                new Color(0.18f, 0.23f, 0.26f),
                new Color(0.39f, 0.47f, 0.56f)
            };
            Color[] sunColors =
            {
                new Color(1.00f, 0.88f, 0.72f),
                new Color(1.00f, 0.94f, 0.84f),
                new Color(1.00f, 0.84f, 0.66f),
                new Color(0.82f, 0.90f, 1.00f),
                new Color(0.91f, 0.96f, 1.00f)
            };

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 0.76f;
            RenderSettings.ambientSkyColor = Color.Lerp(zenithColors[stage], horizonColors[stage], 0.42f);
            RenderSettings.ambientEquatorColor = horizonColors[stage] * 0.46f;
            RenderSettings.ambientGroundColor = fogColors[stage] * 0.27f;
            RenderSettings.reflectionIntensity = 0.48f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColors[stage];
            RenderSettings.fogStartDistance = currentRange * 0.72f;
            RenderSettings.fogEndDistance = currentRange + 520f;

            sun.color = sunColors[stage];
            sun.intensity = stage == 0 ? 1.30f : 1.24f;
            float sunYaw = -38f + stage * 19f;
            sun.transform.rotation = Quaternion.Euler(28f + stage * 4f, sunYaw, 0f);
            skyFill.color = Color.Lerp(horizonColors[stage], Color.white, 0.12f);
            skyFill.intensity = stage == 3 ? 0.10f : 0.13f;
            skyFill.transform.rotation = Quaternion.Euler(48f, sunYaw + 165f, 0f);

            Material activeSky = currentMode == CampaignMode.Operations && missionSkyboxMaterial != null
                ? missionSkyboxMaterial
                : skyboxMaterial != null ? skyboxMaterial : missionSkyboxMaterial;
            RenderSettings.skybox = activeSky;
            if (activeSky != null)
            {
                if (activeSky.HasProperty("_PanoramaTex"))
                {
                    Color tint = Color.Lerp(Color.white, horizonColors[stage], 0.06f);
                    activeSky.SetColor("_Tint", tint);
                    activeSky.SetFloat("_Exposure", stage == 3 ? 0.92f : 1.04f);
                    activeSky.SetFloat("_Rotation", stage * 34f);
                    activeSky.SetFloat("_HorizonBoost", stage == 3 ? 0.04f : 0.10f);
                }
                else if (activeSky.HasProperty("_HorizonColor"))
                {
                    activeSky.SetColor("_HorizonColor", horizonColors[stage]);
                    activeSky.SetColor("_ZenithColor", zenithColors[stage]);
                    activeSky.SetColor("_GroundColor", fogColors[stage] * 0.52f);
                    activeSky.SetColor("_SunColor", sunColors[stage]);
                    activeSky.SetVector("_SunDirection", -sun.transform.forward);
                    activeSky.SetFloat("_SunIntensity", stage == 2 ? 0.34f : 0.42f);
                }
                else
                {
                    activeSky.SetColor("_SkyTint", zenithColors[stage]);
                    activeSky.SetColor("_GroundColor", fogColors[stage] * 0.52f);
                    activeSky.SetFloat("_AtmosphereThickness", 0.68f);
                    activeSky.SetFloat("_SunSize", 0.025f);
                    activeSky.SetFloat("_SunSizeConvergence", 5.0f);
                    activeSky.SetFloat("_Exposure", 0.82f);
                }
            }
        }

        private void CreateTerrain(int stage, float range)
        {
            const int xSegments = 34;
            const int zSegments = 76;
            float width = 520f;
            float length = range + 330f;
            Vector3[] vertices = new Vector3[(xSegments + 1) * (zSegments + 1)];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[xSegments * zSegments * 6];
            float amplitude = stage == 1 ? 15f : stage == 2 ? 22f : stage == 4 ? 18f : 5f;

            int vertex = 0;
            for (int z = 0; z <= zSegments; z++)
            {
                float z01 = z / (float)zSegments;
                float worldZ = -24f + z01 * length;
                for (int x = 0; x <= xSegments; x++)
                {
                    float x01 = x / (float)xSegments;
                    float worldX = (x01 - 0.5f) * width;
                    float noise = (Mathf.PerlinNoise(x01 * 7.5f + stage * 2.7f, z01 * 11.5f + stage) - 0.5f) * 2f;
                    float laneFlatten = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(worldX) - 17f) / 90f));
                    float stageShape = stage == 1 || stage == 2 || stage == 4
                        ? Mathf.Pow(Mathf.Clamp01(Mathf.Abs(worldX) / (width * 0.5f)), 1.35f) * amplitude * 1.8f
                        : 0f;
                    float height = noise * amplitude * 0.25f * laneFlatten + stageShape;
                    if (stage == 3) height *= 0.16f;
                    vertices[vertex] = new Vector3(worldX, height, worldZ);
                    uv[vertex] = new Vector2(x01 * 22f, z01 * Mathf.Max(12f, range / 34f));
                    vertex++;
                }
            }

            int triangle = 0;
            for (int z = 0; z < zSegments; z++)
            {
                for (int x = 0; x < xSegments; x++)
                {
                    int a = z * (xSegments + 1) + x;
                    int b = a + 1;
                    int c = a + xSegments + 1;
                    int d = c + 1;
                    triangles[triangle++] = a;
                    triangles[triangle++] = c;
                    triangles[triangle++] = b;
                    triangles[triangle++] = b;
                    triangles[triangle++] = c;
                    triangles[triangle++] = d;
                }
            }

            Mesh mesh = new Mesh { name = "Range Terrain Mesh" };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GameObject ground = new GameObject("Detailed Range Terrain");
            ground.transform.SetParent(stageRoot, false);
            MeshFilter filter = ground.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = ground.AddComponent<MeshRenderer>();
            MaterialLibrary.Surface surface = stage == 1 ? MaterialLibrary.Surface.Grass :
                stage == 2 ? MaterialLibrary.Surface.Sandstone :
                stage == 3 ? MaterialLibrary.Surface.Concrete :
                stage == 4 ? MaterialLibrary.Surface.Snow : MaterialLibrary.Surface.Dirt;
            renderer.sharedMaterial = materials.Get(surface, Color.white, 0f, stage == 4 ? 0.42f : 0.16f, "_Terrain");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        private void CreateEnvironment(int stage, float range)
        {
            Random.State oldState = Random.state;
            Random.InitState(4603 + stage * 997);
            switch (stage)
            {
                case 0:
                    CreateDistantRocks(range, 42, MaterialLibrary.Surface.Granite, new Color(0.74f, 0.70f, 0.62f), 40f, 1.4f, 7.5f);
                    CreateTreeLine(range, 30, false);
                    break;
                case 1:
                    CreateDistantRocks(range, 42, MaterialLibrary.Surface.Grass, new Color(0.63f, 0.74f, 0.50f), 35f, 5f, 20f);
                    CreateTreeLine(range, 42, false);
                    break;
                case 2:
                    CreateDistantRocks(range, 48, MaterialLibrary.Surface.Sandstone, new Color(0.90f, 0.67f, 0.42f), 31f, 8f, 32f);
                    break;
                case 3:
                    CreateIndustrialYard(range);
                    break;
                default:
                    CreateDistantRocks(range, 45, MaterialLibrary.Surface.Snow, new Color(0.93f, 0.97f, 1f), 38f, 9f, 34f);
                    CreateTreeLine(range, 55, true);
                    break;
            }
            Random.state = oldState;
        }

        private void CreateGroundScatter(int stage, float range)
        {
            Random.State oldState = Random.state;
            Random.InitState(8171 + stage * 1301);
            Material stone = materials.Get(
                stage == 2 ? MaterialLibrary.Surface.Sandstone : MaterialLibrary.Surface.Granite,
                stage == 4 ? new Color(0.86f, 0.91f, 0.94f) : new Color(0.62f, 0.59f, 0.52f),
                0f,
                0.10f,
                "_GroundScatter");
            Material scrub = materials.Get(
                stage == 4 ? MaterialLibrary.Surface.Snow : MaterialLibrary.Surface.Grass,
                stage == 2 ? new Color(0.48f, 0.38f, 0.20f) : new Color(0.42f, 0.52f, 0.28f),
                0f,
                0.14f,
                "_GroundScatter");

            int stoneCount = stage == 3 ? 28 : 62;
            float depth = Mathf.Min(180f, range * 0.72f);
            for (int i = 0; i < stoneCount; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float z = Random.Range(7f, depth);
                float x = side * Random.Range(6.5f, 72f);
                float size = Random.Range(0.10f, 0.52f) * Mathf.Lerp(0.75f, 1.35f, z / depth);
                float groundY = SampleTerrainHeight(stage, range, x, z);
                GameObject pebble = CreatePrimitive(
                    PrimitiveType.Sphere,
                    "Ground Stone",
                    stageRoot,
                    new Vector3(x, groundY + size * 0.20f, z),
                    new Vector3(size * Random.Range(0.7f, 1.45f), size * Random.Range(0.28f, 0.65f), size),
                    stone,
                    Random.rotation,
                    false);
                Renderer pebbleRenderer = pebble.GetComponent<Renderer>();
                if (pebbleRenderer != null) pebbleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            if (stage != 3)
            {
                int scrubCount = stage == 4 ? 22 : 38;
                for (int i = 0; i < scrubCount; i++)
                {
                    float side = i % 2 == 0 ? -1f : 1f;
                    float z = Random.Range(12f, depth);
                    float x = side * Random.Range(10f, 86f);
                    float size = Random.Range(0.24f, 0.75f);
                    float groundY = SampleTerrainHeight(stage, range, x, z);
                    GameObject tuft = CreatePrimitive(
                        PrimitiveType.Sphere,
                        "Range Scrub",
                        stageRoot,
                        new Vector3(x, groundY + size * 0.32f, z),
                        new Vector3(size, size * 0.52f, size * Random.Range(0.70f, 1.25f)),
                        scrub,
                        Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
                        false);
                    Renderer tuftRenderer = tuft.GetComponent<Renderer>();
                    if (tuftRenderer != null) tuftRenderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            Random.state = oldState;
        }

        private static float SampleTerrainHeight(int stage, float range, float worldX, float worldZ)
        {
            const float width = 520f;
            float length = range + 330f;
            float x01 = Mathf.Clamp01(worldX / width + 0.5f);
            float z01 = Mathf.Clamp01((worldZ + 24f) / length);
            float amplitude = stage == 1 ? 15f : stage == 2 ? 22f : stage == 4 ? 18f : 5f;
            float noise = (Mathf.PerlinNoise(x01 * 7.5f + stage * 2.7f, z01 * 11.5f + stage) - 0.5f) * 2f;
            float laneFlatten = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(worldX) - 17f) / 90f));
            float stageShape = stage == 1 || stage == 2 || stage == 4
                ? Mathf.Pow(Mathf.Clamp01(Mathf.Abs(worldX) / (width * 0.5f)), 1.35f) * amplitude * 1.8f
                : 0f;
            float height = noise * amplitude * 0.25f * laneFlatten + stageShape;
            return stage == 3 ? height * 0.16f : height;
        }

        private void CreateDistantRocks(float range, int count, MaterialLibrary.Surface surface, Color tint, float sideOffset, float minScale, float maxScale)
        {
            Material material = materials.Get(surface, tint, 0f, 0.12f, "_Environment");
            for (int i = 0; i < count; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float z = Random.Range(35f, range + 210f);
                float x = side * Random.Range(sideOffset, 235f);
                GameObject rock = CreatePrimitive(PrimitiveType.Sphere, "Weathered Rock", stageRoot,
                    new Vector3(x, Random.Range(-2f, 4f), z),
                    new Vector3(Random.Range(minScale, maxScale), Random.Range(minScale, maxScale) * Random.Range(0.55f, 1.35f), Random.Range(minScale, maxScale)),
                    material, Random.rotation, false);
                rock.transform.Rotate(Random.Range(-18f, 18f), Random.Range(0f, 360f), Random.Range(-14f, 14f), Space.Self);
            }
        }

        private void CreateTreeLine(float range, int count, bool alpine)
        {
            Material trunk = materials.Get(MaterialLibrary.Surface.SplinteredWood, new Color(0.55f, 0.42f, 0.28f), 0f, 0.10f, "_Tree");
            Material foliage = materials.Get(alpine ? MaterialLibrary.Surface.Grass : MaterialLibrary.Surface.Grass,
                alpine ? new Color(0.24f, 0.37f, 0.27f) : new Color(0.34f, 0.48f, 0.25f), 0f, 0.18f, "_Foliage");
            for (int i = 0; i < count; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float z = Random.Range(42f, range + 120f);
                float x = side * Random.Range(38f, 210f);
                float height = Random.Range(alpine ? 8f : 5f, alpine ? 18f : 13f);
                Transform tree = new GameObject("Pine Tree").transform;
                tree.SetParent(stageRoot, false);
                tree.position = new Vector3(x, height * 0.5f, z);
                CreatePrimitive(PrimitiveType.Cylinder, "Trunk", tree, Vector3.zero,
                    new Vector3(height * 0.045f, height * 0.5f, height * 0.045f), trunk, Quaternion.identity, false);
                for (int tier = 0; tier < 3; tier++)
                {
                    float radius = height * (0.24f - tier * 0.045f);
                    float y = height * (-0.13f + tier * 0.20f);
                    CreatePrimitive(PrimitiveType.Sphere, "Needles", tree, new Vector3(0f, y, 0f),
                        new Vector3(radius, height * 0.24f, radius), foliage, Quaternion.identity, false);
                }
            }
        }

        private void CreateIndustrialYard(float range)
        {
            Material steel = materials.Get(MaterialLibrary.Surface.CorrugatedSteel, new Color(0.75f, 0.78f, 0.76f), 0.55f, 0.32f, "_Industry");
            Material rust = materials.Get(MaterialLibrary.Surface.RustedRedSteel, Color.white, 0.65f, 0.24f, "_Industry");
            Material concrete = materials.Get(MaterialLibrary.Surface.Concrete, new Color(0.78f, 0.79f, 0.76f), 0f, 0.12f, "_Industry");
            for (int i = 0; i < 18; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float z = 70f + i * range / 21f;
                float x = side * Random.Range(42f, 95f);
                CreatePrimitive(PrimitiveType.Cube, "Shipping Container", stageRoot,
                    new Vector3(x, 2.6f, z), new Vector3(12f, 5.2f, 4.8f), i % 3 == 0 ? rust : steel,
                    Quaternion.Euler(0f, Random.Range(-8f, 8f), 0f), false);
            }
            for (int i = 0; i < 6; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float z = range * (0.22f + i * 0.12f);
                float x = side * Random.Range(65f, 130f);
                CreatePrimitive(PrimitiveType.Cube, "Warehouse", stageRoot,
                    new Vector3(x, 9f, z), new Vector3(32f, 18f, 24f), concrete, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cylinder, "Industrial Tank", stageRoot,
                    new Vector3(x + side * 21f, 8f, z + 7f), new Vector3(5f, 8f, 5f), steel, Quaternion.identity, false);
            }
        }

        private void CreateMissionEnvironment(OperationDefinition operation, int operationStage)
        {
            Material asphalt = materials.Get(MaterialLibrary.Surface.Concrete, new Color(0.15f, 0.17f, 0.19f), 0f, 0.22f, "_MissionAsphalt");
            Material sidewalk = materials.Get(MaterialLibrary.Surface.Concrete, new Color(0.48f, 0.50f, 0.48f), 0f, 0.34f, "_MissionSidewalk");
            Material facadeA = materials.Get(MaterialLibrary.Surface.Concrete, new Color(0.43f, 0.43f, 0.40f), 0f, 0.38f, "_MissionFacadeA");
            Material facadeB = materials.Get(MaterialLibrary.Surface.Concrete, new Color(0.19f, 0.21f, 0.24f), 0f, 0.32f, "_MissionFacadeB");
            Material frame = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel, new Color(0.16f, 0.19f, 0.20f), 0.46f, 0.36f, "_MissionFrames");
            Material glass = materials.Solid(new Color(0.10f, 0.17f, 0.24f), false, "_MissionWindowDark");
            Material warmWindow = materials.Solid(new Color(0.92f, 0.55f, 0.24f), true, "_MissionWindowGlow");
            Material vegetation = materials.Get(MaterialLibrary.Surface.Grass, new Color(0.20f, 0.34f, 0.22f), 0f, 0.28f, "_MissionVegetation");
            Material roadPaint = materials.Solid(new Color(0.76f, 0.76f, 0.70f), false, "_MissionRoadPaintV57");
            Material streetWood = materials.Get(MaterialLibrary.Surface.Planks, new Color(0.29f, 0.20f, 0.13f), 0f, 0.30f, "_MissionStreetWoodV57");

            CreatePrimitive(PrimitiveType.Cube, "Mission Road", stageRoot,
                new Vector3(0f, -0.22f, currentRange * 0.50f),
                new Vector3(30f, 0.28f, currentRange + 65f), asphalt, Quaternion.identity, true);
            CreatePrimitive(PrimitiveType.Cube, "Left Sidewalk", stageRoot,
                new Vector3(-18f, -0.06f, currentRange * 0.50f),
                new Vector3(6f, 0.18f, currentRange + 65f), sidewalk, Quaternion.identity, true);
            CreatePrimitive(PrimitiveType.Cube, "Right Sidewalk", stageRoot,
                new Vector3(18f, -0.06f, currentRange * 0.50f),
                new Vector3(6f, 0.18f, currentRange + 65f), sidewalk, Quaternion.identity, true);

            CreatePrimitive(PrimitiveType.Cube, "Left Granite Curb", stageRoot,
                new Vector3(-14.92f, 0.02f, currentRange * 0.50f),
                new Vector3(0.24f, 0.28f, currentRange + 65f), sidewalk, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Right Granite Curb", stageRoot,
                new Vector3(14.92f, 0.02f, currentRange * 0.50f),
                new Vector3(0.24f, 0.28f, currentRange + 65f), sidewalk, Quaternion.identity, false);

            for (int i = 0; i < 13; i++)
            {
                float z = 20f + i * Mathf.Max(22f, (currentRange - 30f) / 12f);
                CreatePrimitive(PrimitiveType.Cube, "Lane Dash L", stageRoot,
                    new Vector3(-5.0f, -0.065f, z), new Vector3(0.16f, 0.015f, 5.4f),
                    roadPaint, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Lane Dash R", stageRoot,
                    new Vector3(5.0f, -0.065f, z + 8f), new Vector3(0.16f, 0.015f, 5.4f),
                    roadPaint, Quaternion.identity, false);
            }

            float crossingZ = Mathf.Max(34f, currentRange - 18f);
            for (int i = 0; i < 8; i++)
            {
                float x = -10.5f + i * 3.0f;
                CreatePrimitive(PrimitiveType.Cube, "Crosswalk Stripe", stageRoot,
                    new Vector3(x, -0.06f, crossingZ), new Vector3(1.55f, 0.018f, 0.72f),
                    roadPaint, Quaternion.identity, false);
            }

            int buildingPairs = operationStage == 2 ? 4 : 5;
            for (int i = 0; i < buildingPairs; i++)
            {
                float z = 32f + i * Mathf.Max(28f, (currentRange - 72f) / Mathf.Max(1, buildingPairs - 1));
                float heightL = 12f + (i % 3) * 4f;
                float heightR = 14f + ((i + 1) % 3) * 3f;
                CreateCityBuilding(new Vector3(-30f - (i % 2) * 5f, heightL * 0.5f - 0.05f, z), new Vector3(18f, heightL, 18f),
                    i % 2 == 0 ? facadeA : facadeB, frame, glass, warmWindow, i + operationStage * 11);
                CreateCityBuilding(new Vector3(30f + ((i + 1) % 2) * 5f, heightR * 0.5f - 0.05f, z + 7f), new Vector3(18f, heightR, 20f),
                    i % 2 == 0 ? facadeB : facadeA, frame, glass, warmWindow, i + operationStage * 17 + 3);
            }

            for (int i = 0; i < 3; i++)
            {
                float z = 40f + i * Mathf.Max(48f, (currentRange - 85f) / 3f);
                CreateStreetLamp(-13.7f, z, frame, warmWindow);
                CreateStreetLamp(13.7f, z + 8f, frame, warmWindow);
            }

            for (int i = 0; i < 3; i++)
            {
                float z = 58f + i * Mathf.Max(44f, (currentRange - 110f) / 3f);
                float x = i % 2 == 0 ? -8.5f : 8.5f;
                CreateParkedCar(new Vector3(x, 0.48f, z), i % 2 == 0 ? new Color(0.18f, 0.24f, 0.30f) : new Color(0.43f, 0.17f, 0.12f), frame);
            }

            for (int i = 0; i < 6; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float z = 42f + (i / 2) * Mathf.Max(38f, (currentRange - 90f) / 4f);
                Transform tree = new GameObject("Mission Street Tree").transform;
                tree.SetParent(stageRoot, false);
                tree.position = new Vector3(side * 20.5f, 0f, z);
                CreatePrimitive(PrimitiveType.Cylinder, "Tree Trunk", tree, new Vector3(0f, 1.6f, 0f),
                    new Vector3(0.16f, 1.6f, 0.16f), frame, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Sphere, "Tree Crown Lower", tree, new Vector3(0f, 3.65f, 0f),
                    new Vector3(1.45f, 1.45f, 1.35f), vegetation, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Sphere, "Tree Crown Upper", tree, new Vector3(0.18f, 4.65f, 0.08f),
                    new Vector3(1.18f, 1.35f, 1.10f), vegetation, Quaternion.identity, false);

                float furnitureX = side * 17.45f;
                CreatePrimitive(PrimitiveType.Cube, "Street Bench Seat", stageRoot,
                    new Vector3(furnitureX, 0.48f, z + 7.0f), new Vector3(2.1f, 0.16f, 0.48f),
                    streetWood, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Street Bench Back", stageRoot,
                    new Vector3(furnitureX + side * 0.18f, 0.92f, z + 7.0f), new Vector3(0.12f, 0.82f, 2.1f),
                    streetWood, Quaternion.Euler(0f, 90f, 0f), false);
                CreatePrimitive(PrimitiveType.Cube, "Stone Planter", stageRoot,
                    new Vector3(side * 19.35f, 0.34f, z - 6.2f), new Vector3(1.45f, 0.68f, 1.45f),
                    sidewalk, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Sphere, "Planter Shrub", stageRoot,
                    new Vector3(side * 19.35f, 1.25f, z - 6.2f), new Vector3(1.10f, 1.05f, 1.10f),
                    vegetation, Quaternion.identity, false);
            }

            // A nearby foreground structure keeps the firing position physical.
            // The escape mission puts the shooter on a high rooftop instead of ground level.
            if (operation.Kind == OperationKind.EscapeVehicle)
            {
                CreatePrimitive(PrimitiveType.Cube, "High Shooter Tower", stageRoot,
                    new Vector3(0f, 10.6f, 4.2f), new Vector3(13.5f, 21.2f, 12.0f),
                    facadeB, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "High Shooter Roof", stageRoot,
                    new Vector3(0f, 21.35f, 4.2f), new Vector3(14.2f, 0.34f, 12.7f),
                    sidewalk, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "High Shooter Parapet", stageRoot,
                    new Vector3(0f, 22.05f, 9.9f), new Vector3(11.0f, 1.05f, 0.42f),
                    sidewalk, Quaternion.identity, true);
            }
            else
            {
                CreatePrimitive(PrimitiveType.Cube, "Shooter Rooftop Ledge", stageRoot,
                    new Vector3(0f, 0.48f, 4.4f), new Vector3(5.8f, 0.82f, 0.55f),
                    sidewalk, Quaternion.identity, true);
            }

            RenderSettings.fogStartDistance = currentRange * 0.70f;
            RenderSettings.fogEndDistance = currentRange + 280f;
        }

        private void CreateCityBuilding(
            Vector3 centre,
            Vector3 size,
            Material wall,
            Material frame,
            Material glass,
            Material warmWindow,
            int seed)
        {
            CreatePrimitive(PrimitiveType.Cube, "Mission Building", stageRoot, centre, size, wall, Quaternion.identity, true);

            float bottom = centre.y - size.y * 0.5f;
            float faceZ = centre.z - size.z * 0.5f - 0.19f;
            CreatePrimitive(PrimitiveType.Cube, "Building Stone Plinth", stageRoot,
                new Vector3(centre.x, bottom + 0.35f, centre.z),
                new Vector3(size.x + 0.35f, 0.70f, size.z + 0.35f), frame, Quaternion.identity, false);

            int floors = Mathf.Clamp(Mathf.RoundToInt(size.y / 3.55f), 3, 5);
            int columns = size.x >= 18f ? 4 : 3;
            float floorStep = (size.y - 2.5f) / Mathf.Max(1, floors);

            for (int floor = 0; floor < floors; floor++)
            {
                float y = bottom + 2.05f + floor * floorStep;
                if (y > centre.y + size.y * 0.42f) continue;

                for (int column = 0; column < columns; column++)
                {
                    float lerp = columns == 1 ? 0.5f : column / (float)(columns - 1);
                    float x = centre.x + Mathf.Lerp(-size.x * 0.36f, size.x * 0.36f, lerp);
                    bool lit = ((seed + floor * 5 + column * 3) % 8) < 3;
                    Material pane = lit ? warmWindow : glass;

                    CreatePrimitive(PrimitiveType.Cube, "Window Recess", stageRoot,
                        new Vector3(x, y, faceZ), new Vector3(2.20f, 1.55f, 0.075f),
                        pane, Quaternion.identity, false);
                    CreatePrimitive(PrimitiveType.Cube, "Window Sill", stageRoot,
                        new Vector3(x, y - 0.87f, faceZ - 0.035f), new Vector3(2.45f, 0.10f, 0.16f),
                        frame, Quaternion.identity, false);
                    CreatePrimitive(PrimitiveType.Cube, "Window Lintel", stageRoot,
                        new Vector3(x, y + 0.87f, faceZ - 0.035f), new Vector3(2.45f, 0.10f, 0.16f),
                        frame, Quaternion.identity, false);
                }

                if (floor > 0)
                {
                    CreatePrimitive(PrimitiveType.Cube, "Facade Floor Band", stageRoot,
                        new Vector3(centre.x, y - floorStep * 0.51f, faceZ - 0.03f),
                        new Vector3(size.x * 0.94f, 0.11f, 0.18f), frame, Quaternion.identity, false);
                }
            }

            for (int side = -1; side <= 1; side += 2)
            {
                CreatePrimitive(PrimitiveType.Cube, "Facade Corner Pier", stageRoot,
                    new Vector3(centre.x + side * size.x * 0.45f, centre.y, faceZ + 0.01f),
                    new Vector3(0.42f, size.y * 0.93f, 0.20f), frame, Quaternion.identity, false);
            }

            float entryX = centre.x + ((seed & 1) == 0 ? -size.x * 0.24f : size.x * 0.24f);
            CreatePrimitive(PrimitiveType.Cube, "Building Entrance", stageRoot,
                new Vector3(entryX, bottom + 1.25f, faceZ - 0.04f),
                new Vector3(1.55f, 2.50f, 0.11f), glass, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Entrance Canopy", stageRoot,
                new Vector3(entryX, bottom + 2.62f, faceZ - 0.50f),
                new Vector3(2.65f, 0.14f, 0.95f), frame, Quaternion.identity, false);

            CreatePrimitive(PrimitiveType.Cube, "Building Roof Cap", stageRoot,
                new Vector3(centre.x, centre.y + size.y * 0.5f + 0.20f, centre.z),
                new Vector3(size.x + 0.7f, 0.32f, size.z + 0.7f), frame, Quaternion.identity, true);
            CreatePrimitive(PrimitiveType.Cube, "Rooftop Utility Box", stageRoot,
                new Vector3(centre.x + (((seed % 3) - 1) * 2.1f), centre.y + size.y * 0.5f + 0.78f, centre.z + 1.1f),
                new Vector3(2.4f, 1.15f, 2.0f), frame, Quaternion.identity, false);
        }

        private void CreateStreetLamp(float x, float z, Material metal, Material glow)
        {
            CreatePrimitive(PrimitiveType.Cylinder, "Street Lamp Pole", stageRoot,
                new Vector3(x, 2.5f, z), new Vector3(0.08f, 2.5f, 0.08f), metal, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Sphere, "Street Lamp Head", stageRoot,
                new Vector3(x, 5.05f, z), new Vector3(0.22f, 0.16f, 0.22f), glow, Quaternion.identity, false);
        }

        private void CreateParkedCar(Vector3 centre, Color bodyTint, Material dark)
        {
            Transform root = new GameObject("Detailed Parked Sedan").transform;
            root.SetParent(stageRoot, false);
            root.position = centre;
            root.rotation = Quaternion.Euler(0f, centre.x < 0f ? 7f : -7f, 0f);

            Material body = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel, bodyTint, 0.58f, 0.48f, "_MissionCarV57");
            Material glass = materials.TransparentGlass(new Color(0.16f, 0.30f, 0.40f, 0.32f));
            Material tire = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel, new Color(0.018f, 0.020f, 0.022f), 0.08f, 0.20f, "_MissionTireV57");
            Material rim = materials.MetallicSolid(new Color(0.52f, 0.55f, 0.58f), 0.88f, 0.74f, "_MissionRimV57");
            Material lamp = materials.Solid(new Color(0.78f, 0.88f, 1f), true, "_MissionHeadlampV57");
            Material tail = materials.Solid(new Color(0.72f, 0.035f, 0.018f), true, "_MissionTailLampV57");

            CreatePrimitive(PrimitiveType.Cube, "Sedan Lower Body", root, new Vector3(0f, 0.02f, 0f),
                new Vector3(1.90f, 0.48f, 4.28f), body, Quaternion.identity, true);
            CreatePrimitive(PrimitiveType.Sphere, "Sedan Rounded Nose", root, new Vector3(0f, 0.28f, 1.78f),
                new Vector3(1.86f, 0.50f, 1.02f), body, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Sphere, "Sedan Rounded Rear", root, new Vector3(0f, 0.30f, -1.75f),
                new Vector3(1.82f, 0.54f, 0.96f), body, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Sedan Hood", root, new Vector3(0f, 0.49f, 1.38f),
                new Vector3(1.76f, 0.11f, 1.18f), body, Quaternion.Euler(-3f, 0f, 0f), false);
            CreatePrimitive(PrimitiveType.Cube, "Sedan Trunk", root, new Vector3(0f, 0.49f, -1.48f),
                new Vector3(1.72f, 0.14f, 0.92f), body, Quaternion.Euler(3f, 0f, 0f), false);
            CreatePrimitive(PrimitiveType.Cube, "Sedan Roof", root, new Vector3(0f, 1.16f, -0.10f),
                new Vector3(1.58f, 0.12f, 1.80f), body, Quaternion.identity, false);

            GameObject windshield = CreatePrimitive(PrimitiveType.Cube, "Parked Windshield", root,
                new Vector3(0f, 0.86f, 0.72f), new Vector3(1.58f, 0.62f, 0.030f),
                glass, Quaternion.Euler(-28f, 0f, 0f), false);
            escapeGlassPanels.Add(windshield);
            GameObject rearGlass = CreatePrimitive(PrimitiveType.Cube, "Parked Rear Glass", root,
                new Vector3(0f, 0.88f, -0.84f), new Vector3(1.56f, 0.58f, 0.030f),
                glass, Quaternion.Euler(27f, 0f, 0f), false);
            escapeGlassPanels.Add(rearGlass);

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject frontSide = CreatePrimitive(PrimitiveType.Cube, "Parked Front Side Glass", root,
                    new Vector3(side * 0.965f, 0.92f, 0.26f), new Vector3(0.028f, 0.44f, 0.96f),
                    glass, Quaternion.identity, false);
                escapeGlassPanels.Add(frontSide);
                GameObject rearSide = CreatePrimitive(PrimitiveType.Cube, "Parked Rear Side Glass", root,
                    new Vector3(side * 0.965f, 0.92f, -0.66f), new Vector3(0.028f, 0.42f, 0.78f),
                    glass, Quaternion.identity, false);
                escapeGlassPanels.Add(rearSide);

                CreatePrimitive(PrimitiveType.Cube, "Side Mirror", root,
                    new Vector3(side * 1.03f, 0.84f, 0.68f), new Vector3(0.18f, 0.12f, 0.28f),
                    body, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Headlamp", root,
                    new Vector3(side * 0.58f, 0.34f, 2.16f), new Vector3(0.34f, 0.15f, 0.035f),
                    lamp, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Tail Lamp", root,
                    new Vector3(side * 0.61f, 0.36f, -2.16f), new Vector3(0.31f, 0.16f, 0.035f),
                    tail, Quaternion.identity, false);

                for (int end = -1; end <= 1; end += 2)
                {
                    Vector3 wheelPosition = new Vector3(side * 0.99f, -0.17f, end * 1.38f);
                    CreatePrimitive(PrimitiveType.Cylinder, "Parked Tire", root, wheelPosition,
                        new Vector3(0.33f, 0.17f, 0.33f), tire, Quaternion.Euler(0f, 0f, 90f), false);
                    CreatePrimitive(PrimitiveType.Cylinder, "Parked Alloy Wheel", root,
                        wheelPosition + new Vector3(side * 0.010f, 0f, 0f),
                        new Vector3(0.21f, 0.175f, 0.21f), rim, Quaternion.Euler(0f, 0f, 90f), false);
                }
            }

            CreatePrimitive(PrimitiveType.Cube, "Front Grille", root, new Vector3(0f, 0.18f, 2.18f),
                new Vector3(0.76f, 0.18f, 0.035f), dark, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Front Bumper", root, new Vector3(0f, 0.05f, 2.17f),
                new Vector3(1.82f, 0.16f, 0.10f), dark, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Rear Bumper", root, new Vector3(0f, 0.06f, -2.17f),
                new Vector3(1.82f, 0.16f, 0.10f), dark, Quaternion.identity, false);
        }

        private void CreateOperationSetpiece(OperationDefinition operation, int operationStage)
        {
            Material concrete = materials.Get(MaterialLibrary.Surface.Concrete, new Color(0.72f, 0.75f, 0.73f), 0f, 0.34f, "_OperationArchitecture");
            Material darkConcrete = materials.Get(MaterialLibrary.Surface.Concrete, new Color(0.27f, 0.31f, 0.32f), 0f, 0.25f, "_OperationArchitectureDark");
            Material steel = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel, new Color(0.47f, 0.53f, 0.53f), 0.72f, 0.48f, "_OperationSteel");
            Material wood = materials.Get(MaterialLibrary.Surface.Planks, new Color(0.82f, 0.65f, 0.43f), 0f, 0.30f, "_OperationWood");
            Material warm = materials.Solid(new Color(1f, 0.54f, 0.20f), true, "_OperationWarmLight");

            if (operation.Kind == OperationKind.Conversation)
            {
                float floorY = 0.30f;
                CreatePrimitive(PrimitiveType.Cube, "Terrace Floor", stageRoot,
                    new Vector3(0f, 0.15f, currentRange), new Vector3(12f, 0.30f, 8f), concrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Terrace Back Wall", stageRoot,
                    new Vector3(0f, 2.30f, currentRange + 3.65f), new Vector3(12f, 4.30f, 0.30f), darkConcrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Canopy", stageRoot,
                    new Vector3(0f, 3.25f, currentRange + 0.50f), new Vector3(9.2f, 0.20f, 6.1f), wood, Quaternion.identity, true);
                for (int side = -1; side <= 1; side += 2)
                {
                    CreatePrimitive(PrimitiveType.Cylinder, "Canopy Column", stageRoot,
                        new Vector3(side * 4.10f, 1.75f, currentRange - 1.80f), new Vector3(0.12f, 1.60f, 0.12f), steel, Quaternion.identity, true);
                    CreatePrimitive(PrimitiveType.Cylinder, "Terrace Lamp", stageRoot,
                        new Vector3(side * 2.55f, 2.83f, currentRange + 1.90f), new Vector3(0.14f, 0.12f, 0.14f), warm, Quaternion.identity, false);
                }
                CreatePrimitive(PrimitiveType.Cylinder, "Conversation Table", stageRoot,
                    new Vector3(0.02f, 0.91f, currentRange - 0.32f), new Vector3(0.62f, 0.055f, 0.62f), wood, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cylinder, "Table Pedestal", stageRoot,
                    new Vector3(0.02f, 0.59f, currentRange - 0.32f), new Vector3(0.09f, 0.30f, 0.09f), steel, Quaternion.identity, true);

                AddHuman("VOLKOV", true, new Vector3(-0.22f, floorY, currentRange + 0.05f),
                    HumanMotionStyle.TargetPatrol, 0.20f, new Color(0.56f, 0.075f, 0.065f), new Color(0.10f, 0.11f, 0.13f));
                AddHuman("SPEAKER", false, new Vector3(0.24f, floorY, currentRange - 0.34f),
                    HumanMotionStyle.CrossingSpeaker, 2.10f, new Color(0.12f, 0.28f, 0.46f), new Color(0.13f, 0.16f, 0.20f));
                AddHuman("SECURITY", false, new Vector3(2.15f, floorY, currentRange + 0.35f),
                    HumanMotionStyle.Guard, 4.30f, new Color(0.12f, 0.14f, 0.16f), new Color(0.08f, 0.09f, 0.10f));
                AddHuman("CIVILIAN A", false, new Vector3(-2.05f, floorY, currentRange - 0.48f),
                    HumanMotionStyle.CivilianWalk, 1.20f, new Color(0.36f, 0.46f, 0.56f), new Color(0.18f, 0.20f, 0.23f));
                AddHuman("CIVILIAN B", false, new Vector3(1.22f, floorY, currentRange + 0.58f),
                    HumanMotionStyle.Conversation, 5.20f, new Color(0.44f, 0.30f, 0.18f), new Color(0.12f, 0.14f, 0.18f));
            }
            else if (operation.Kind == OperationKind.HotelWindow)
            {
                Material hotelTrim = materials.Get(MaterialLibrary.Surface.Concrete,
                    new Color(0.58f, 0.55f, 0.50f), 0f, 0.42f, "_HotelTrimV57");
                Material roomWall = materials.Get(MaterialLibrary.Surface.Concrete,
                    new Color(0.48f, 0.38f, 0.28f), 0f, 0.34f, "_HotelRoomWallV57");
                Material curtain = materials.Solid(new Color(0.31f, 0.075f, 0.060f), false, "_HotelCurtainV57");
                Material sideGlass = materials.TransparentGlass(new Color(0.22f, 0.34f, 0.42f, 0.28f));

                CreatePrimitive(PrimitiveType.Cube, "Hotel Foundation", stageRoot,
                    new Vector3(0f, -0.18f, currentRange + 1.1f), new Vector3(12f, 0.35f, 7f), concrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Facade Left", stageRoot,
                    new Vector3(-3.38f, 2.35f, currentRange), new Vector3(5.30f, 4.70f, 0.42f), darkConcrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Facade Right", stageRoot,
                    new Vector3(3.38f, 2.35f, currentRange), new Vector3(5.30f, 4.70f, 0.42f), darkConcrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Facade Sill", stageRoot,
                    new Vector3(0f, 0.50f, currentRange), new Vector3(1.46f, 1.0f, 0.42f), concrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Facade Header", stageRoot,
                    new Vector3(0f, 3.38f, currentRange), new Vector3(1.46f, 2.36f, 0.42f), concrete, Quaternion.identity, true);

                CreatePrimitive(PrimitiveType.Cube, "Hotel Base Band", stageRoot,
                    new Vector3(0f, 0.32f, currentRange - 0.24f), new Vector3(11.5f, 0.42f, 0.20f),
                    hotelTrim, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Hotel Cornice", stageRoot,
                    new Vector3(0f, 4.73f, currentRange - 0.20f), new Vector3(11.8f, 0.34f, 0.48f),
                    hotelTrim, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Hotel Upper Band", stageRoot,
                    new Vector3(0f, 3.78f, currentRange - 0.22f), new Vector3(11.2f, 0.16f, 0.24f),
                    hotelTrim, Quaternion.identity, false);

                for (int side = -1; side <= 1; side += 2)
                {
                    float x1 = side * 2.25f;
                    float x2 = side * 4.25f;
                    CreatePrimitive(PrimitiveType.Cube, "Hotel Side Window A", stageRoot,
                        new Vector3(x1, 1.55f, currentRange - 0.24f), new Vector3(1.25f, 1.48f, 0.06f),
                        sideGlass, Quaternion.identity, false);
                    CreatePrimitive(PrimitiveType.Cube, "Hotel Side Window B", stageRoot,
                        new Vector3(x2, 1.55f, currentRange - 0.24f), new Vector3(1.25f, 1.48f, 0.06f),
                        ((side + 1) / 2 == 0) ? sideGlass : warm, Quaternion.identity, false);
                    CreatePrimitive(PrimitiveType.Cube, "Hotel Side Window Upper", stageRoot,
                        new Vector3(x1, 3.10f, currentRange - 0.24f), new Vector3(1.25f, 0.92f, 0.06f),
                        sideGlass, Quaternion.identity, false);
                    CreatePrimitive(PrimitiveType.Cube, "Hotel Pilaster", stageRoot,
                        new Vector3(side * 5.72f, 2.35f, currentRange - 0.23f), new Vector3(0.26f, 4.45f, 0.28f),
                        hotelTrim, Quaternion.identity, false);
                }

                CreatePrimitive(PrimitiveType.Cube, "Window Frame L", stageRoot,
                    new Vector3(-0.71f, 1.56f, currentRange - 0.22f), new Vector3(0.10f, 1.18f, 0.12f), steel, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Window Frame R", stageRoot,
                    new Vector3(0.71f, 1.56f, currentRange - 0.22f), new Vector3(0.10f, 1.18f, 0.12f), steel, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Window Frame Top", stageRoot,
                    new Vector3(0f, 2.15f, currentRange - 0.22f), new Vector3(1.52f, 0.10f, 0.12f), steel, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Window Stone Sill", stageRoot,
                    new Vector3(0f, 0.96f, currentRange - 0.28f), new Vector3(1.68f, 0.16f, 0.38f), hotelTrim, Quaternion.identity, false);

                hotelWindowGlass = CreatePrimitive(PrimitiveType.Cube, "Window Glass", stageRoot,
                    new Vector3(0f, 1.56f, currentRange - 0.28f), new Vector3(1.32f, 1.04f, 0.010f),
                    materials.TransparentGlass(new Color(0.40f, 0.66f, 0.78f, 0.22f)), Quaternion.identity, false);

                CreatePrimitive(PrimitiveType.Cube, "Room Floor", stageRoot,
                    new Vector3(0f, -0.08f, currentRange + 2.10f), new Vector3(5.2f, 0.18f, 4f), wood, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Room Back Wall", stageRoot,
                    new Vector3(0f, 2.10f, currentRange + 3.25f), new Vector3(5.15f, 4.20f, 0.18f),
                    roomWall, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Curtain L", stageRoot,
                    new Vector3(-0.58f, 1.62f, currentRange + 0.04f), new Vector3(0.16f, 1.62f, 0.10f),
                    curtain, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Curtain R", stageRoot,
                    new Vector3(0.58f, 1.62f, currentRange + 0.04f), new Vector3(0.16f, 1.62f, 0.10f),
                    curtain, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Interior Console", stageRoot,
                    new Vector3(-1.68f, 0.54f, currentRange + 2.62f), new Vector3(1.10f, 1.00f, 0.48f),
                    wood, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Interior Lamp", stageRoot,
                    new Vector3(-1.70f, 2.42f, currentRange + 1.30f), new Vector3(0.18f, 0.18f, 0.18f), warm, Quaternion.identity, false);

                AddHuman("MOROZOV", true, new Vector3(0f, 0.02f, currentRange + 0.52f),
                    HumanMotionStyle.WindowPatrol, 0.35f, new Color(0.075f, 0.30f, 0.21f), new Color(0.09f, 0.11f, 0.12f));
                AddHuman("ROOM ATTENDANT", false, new Vector3(0.12f, 0.02f, currentRange + 0.28f),
                    HumanMotionStyle.CrossingSpeaker, 3.25f, new Color(0.58f, 0.62f, 0.66f), new Color(0.16f, 0.19f, 0.22f));
                AddHuman("HOTEL GUEST", false, new Vector3(-1.45f, 0.02f, currentRange + 1.15f),
                    HumanMotionStyle.CivilianWalk, 1.55f, new Color(0.38f, 0.20f, 0.42f), new Color(0.18f, 0.20f, 0.22f));
                AddHuman("SECURITY", false, new Vector3(1.62f, 0.02f, currentRange + 1.30f),
                    HumanMotionStyle.Guard, 4.70f, new Color(0.10f, 0.13f, 0.16f), new Color(0.07f, 0.08f, 0.10f));
            }

        else if (operation.Kind == OperationKind.Rooftop)
            {
                const float roofY = 5.82f;
                CreatePrimitive(PrimitiveType.Cube, "Terminal Building", stageRoot,
                    new Vector3(0f, 2.75f, currentRange + 3.6f), new Vector3(20f, 5.50f, 8.2f), darkConcrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Terminal Roof", stageRoot,
                    new Vector3(0f, 5.64f, currentRange + 0.45f), new Vector3(20f, 0.36f, 8.0f), concrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Roof Parapet", stageRoot,
                    new Vector3(0f, 6.14f, currentRange - 0.48f), new Vector3(20f, 0.66f, 0.30f), concrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Ventilation Block", stageRoot,
                    new Vector3(1.07f, 6.58f, currentRange + 0.06f), new Vector3(0.90f, 1.50f, 1.25f), steel, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cylinder, "Roof Antenna", stageRoot,
                    new Vector3(-4.2f, 8.40f, currentRange + 1.8f), new Vector3(0.11f, 2.65f, 0.11f), steel, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Aviation Light", stageRoot,
                    new Vector3(-4.2f, 11.05f, currentRange + 1.8f), Vector3.one * 0.22f,
                    materials.Solid(new Color(1f, 0.08f, 0.035f), true, "_AviationLight"), Quaternion.identity, false);

                AddHuman("ORLOV", true, new Vector3(-0.18f, roofY, currentRange + 0.18f),
                    HumanMotionStyle.RooftopPatrol, 0.40f, new Color(0.67f, 0.53f, 0.31f), new Color(0.12f, 0.13f, 0.14f));
                AddHuman("GUARD ALPHA", false, new Vector3(-0.82f, roofY, currentRange - 0.20f),
                    HumanMotionStyle.Guard, 2.40f, new Color(0.12f, 0.16f, 0.20f), new Color(0.08f, 0.10f, 0.12f));
                AddHuman("GUARD BRAVO", false, new Vector3(2.08f, roofY, currentRange + 0.25f),
                    HumanMotionStyle.Guard, 5.10f, new Color(0.17f, 0.19f, 0.21f), new Color(0.08f, 0.10f, 0.12f));
                AddHuman("TECHNICIAN", false, new Vector3(-2.25f, roofY, currentRange + 0.70f),
                    HumanMotionStyle.CivilianWalk, 1.75f, new Color(0.30f, 0.36f, 0.40f), new Color(0.10f, 0.12f, 0.14f));
                AddHuman("GROUND CREW", false, new Vector3(3.15f, roofY, currentRange + 1.15f),
                    HumanMotionStyle.Guard, 3.65f, new Color(0.25f, 0.29f, 0.33f), new Color(0.09f, 0.10f, 0.12f));
            }
            else if (operation.Kind == OperationKind.EscapeVehicle)
            {
                Material road = materials.Get(MaterialLibrary.Surface.Concrete, new Color(0.10f, 0.12f, 0.14f), 0f, 0.22f, "_EscapeRoad");
                CreatePrimitive(PrimitiveType.Cube, "Escape Cross Street", stageRoot,
                    new Vector3(7f, -0.10f, currentRange + 0.10f), new Vector3(66f, 0.16f, 9.2f),
                    road, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Escape Median", stageRoot,
                    new Vector3(7f, 0.12f, currentRange + 4.45f), new Vector3(66f, 0.28f, 0.52f),
                    concrete, Quaternion.identity, true);

                escapeVehicle = CreateEscapeVehicle(new Vector3(4.8f, 0.48f, currentRange + 0.05f), steel);
                escapeVehicleStart = escapeVehicle.position;
                escapeActive = false;

                AddHuman("TARGET ALPHA", true, new Vector3(-2.20f, 0.04f, currentRange - 0.10f),
                    HumanMotionStyle.TargetPatrol, 0.35f, new Color(0.62f, 0.10f, 0.08f), new Color(0.10f, 0.11f, 0.13f));
                AddHuman("TARGET BRAVO", true, new Vector3(1.10f, 0.04f, currentRange + 0.18f),
                    HumanMotionStyle.Conversation, 2.10f, new Color(0.12f, 0.32f, 0.62f), new Color(0.09f, 0.10f, 0.12f));
            }
            else
            {
                Material militaryCanvas = materials.Get(MaterialLibrary.Surface.Grass,
                    new Color(0.24f, 0.30f, 0.18f), 0f, 0.30f, "_OfficerMissionCanvasV57");
                Material militaryDark = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel,
                    new Color(0.10f, 0.13f, 0.09f), 0.35f, 0.34f, "_OfficerMissionDarkV57");
                Material sandbag = materials.Get(MaterialLibrary.Surface.Sandstone,
                    new Color(0.55f, 0.48f, 0.34f), 0f, 0.30f, "_OfficerMissionSandbagV57");
                Material insignia = materials.MetallicSolid(
                    new Color(0.76f, 0.61f, 0.18f), 0.76f, 0.70f, "_OfficerInsigniaV57");

                CreatePrimitive(PrimitiveType.Cube, "Command Yard", stageRoot,
                    new Vector3(0f, 0.02f, currentRange + 0.70f), new Vector3(24f, 0.18f, 15f),
                    concrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Command Post Rear", stageRoot,
                    new Vector3(0f, 2.05f, currentRange + 6.55f), new Vector3(16f, 4.1f, 0.40f),
                    militaryCanvas, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Command Post Roof", stageRoot,
                    new Vector3(0f, 4.15f, currentRange + 4.35f), new Vector3(16.8f, 0.24f, 4.8f),
                    militaryCanvas, Quaternion.Euler(-2f, 0f, 0f), false);
                for (int side = -1; side <= 1; side += 2)
                {
                    CreatePrimitive(PrimitiveType.Cylinder, "Command Post Pole", stageRoot,
                        new Vector3(side * 7.55f, 2.10f, currentRange + 2.25f),
                        new Vector3(0.11f, 2.05f, 0.11f), militaryDark, Quaternion.identity, true);
                    CreatePrimitive(PrimitiveType.Cube, "Concrete Barrier", stageRoot,
                        new Vector3(side * 7.0f, 0.52f, currentRange - 2.65f),
                        new Vector3(4.3f, 1.0f, 0.62f), concrete, Quaternion.identity, true);
                }

                for (int i = 0; i < 8; i++)
                {
                    float x = -5.2f + i * 1.48f;
                    CreatePrimitive(PrimitiveType.Capsule, "Sandbag", stageRoot,
                        new Vector3(x, 0.34f, currentRange + 5.95f),
                        new Vector3(0.66f, 0.26f, 0.42f), sandbag, Quaternion.Euler(90f, 0f, 0f), false);
                }

                CreatePrimitive(PrimitiveType.Cube, "Briefing Table", stageRoot,
                    new Vector3(0.10f, 0.82f, currentRange + 0.82f),
                    new Vector3(2.25f, 0.10f, 1.20f), wood, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Map Case", stageRoot,
                    new Vector3(0.10f, 0.93f, currentRange + 0.82f),
                    new Vector3(1.52f, 0.06f, 0.76f),
                    materials.Solid(new Color(0.58f, 0.52f, 0.35f), false, "_OfficerMapV57"),
                    Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cylinder, "Radio Mast", stageRoot,
                    new Vector3(6.6f, 5.25f, currentRange + 5.8f),
                    new Vector3(0.09f, 5.2f, 0.09f), militaryDark, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Radio Crossbar", stageRoot,
                    new Vector3(6.6f, 8.85f, currentRange + 5.8f),
                    new Vector3(2.2f, 0.08f, 0.08f), militaryDark, Quaternion.identity, false);

                HumanMissionActor officer = AddHuman("OFFICER SOKOLOV", true,
                    new Vector3(0.0f, 0.04f, currentRange + 0.10f),
                    HumanMotionStyle.Conversation, 0.35f,
                    new Color(0.20f, 0.28f, 0.13f), new Color(0.10f, 0.12f, 0.08f));
                DecorateOfficer(officer, militaryDark, insignia);

                AddHuman("SOLDIER ALPHA", false, new Vector3(-1.15f, 0.04f, currentRange - 0.15f),
                    HumanMotionStyle.Conversation, 1.20f, new Color(0.28f, 0.35f, 0.20f), new Color(0.12f, 0.15f, 0.10f));
                AddHuman("SOLDIER BRAVO", false, new Vector3(1.25f, 0.04f, currentRange - 0.05f),
                    HumanMotionStyle.Conversation, 2.40f, new Color(0.26f, 0.34f, 0.19f), new Color(0.11f, 0.14f, 0.09f));
                AddHuman("SOLDIER CHARLIE", false, new Vector3(-2.20f, 0.04f, currentRange + 0.65f),
                    HumanMotionStyle.Guard, 3.30f, new Color(0.23f, 0.31f, 0.17f), new Color(0.10f, 0.13f, 0.09f));
                AddHuman("SOLDIER DELTA", false, new Vector3(2.25f, 0.04f, currentRange + 0.72f),
                    HumanMotionStyle.Guard, 4.45f, new Color(0.29f, 0.36f, 0.20f), new Color(0.12f, 0.14f, 0.10f));
                AddHuman("SOLDIER ECHO", false, new Vector3(0.78f, 0.04f, currentRange + 1.55f),
                    HumanMotionStyle.CrossingSpeaker, 5.25f, new Color(0.25f, 0.33f, 0.18f), new Color(0.10f, 0.13f, 0.09f));
            }

            GameObject keyObject = new GameObject("Operation Key Light");
            keyObject.transform.SetParent(stageRoot, false);
            keyObject.transform.position = new Vector3(
                -2.2f,
                operation.Kind == OperationKind.EscapeVehicle ? 7.5f : operationStage == 2 ? 10f : 5f,
                currentRange - 3f);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Point;
            key.color = operationStage == 1 ? new Color(1f, 0.70f, 0.42f) : new Color(0.72f, 0.84f, 1f);
            key.intensity = 1.45f;
            key.range = 16f;
            key.shadows = LightShadows.None;
        }

        private void DecorateOfficer(HumanMissionActor officer, Material capMaterial, Material insignia)
        {
            if (officer == null) return;
            Transform root = officer.transform;

            CreatePrimitive(PrimitiveType.Cylinder, "Officer Peaked Cap Crown", root,
                new Vector3(0f, 1.98f, 0.00f), new Vector3(0.27f, 0.035f, 0.25f),
                capMaterial, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Officer Cap Visor", root,
                new Vector3(0f, 1.93f, -0.20f), new Vector3(0.34f, 0.035f, 0.18f),
                capMaterial, Quaternion.Euler(-6f, 0f, 0f), false);
            CreatePrimitive(PrimitiveType.Cube, "Officer Left Epaulette", root,
                new Vector3(-0.29f, 1.53f, -0.02f), new Vector3(0.20f, 0.055f, 0.26f),
                insignia, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Officer Right Epaulette", root,
                new Vector3(0.29f, 1.53f, -0.02f), new Vector3(0.20f, 0.055f, 0.26f),
                insignia, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Officer Chest Insignia", root,
                new Vector3(0.16f, 1.48f, -0.245f), new Vector3(0.20f, 0.055f, 0.025f),
                insignia, Quaternion.identity, false);
        }

        private Transform CreateEscapeVehicle(Vector3 worldPosition, Material dark)
        {
            Transform root = new GameObject("ESCAPE VEHICLE — PREMIUM DARK SEDAN").transform;
            root.SetParent(stageRoot, false);
            root.position = worldPosition;
            root.rotation = Quaternion.Euler(0f, 90f, 0f);
            Material body = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel,
                new Color(0.075f, 0.095f, 0.125f), 0.88f, 0.74f, "_EscapeVehicleBodyV56");
            Material trim = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel,
                new Color(0.025f, 0.030f, 0.038f), 0.72f, 0.58f, "_EscapeVehicleTrimV56");
            Material tire = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel,
                new Color(0.018f, 0.020f, 0.022f), 0.10f, 0.22f, "_EscapeVehicleTireV56");
            Material rim = materials.MetallicSolid(new Color(0.48f, 0.51f, 0.54f), 0.92f, 0.82f, "_EscapeVehicleRimV56");
            Material glass = materials.TransparentGlass(new Color(0.22f, 0.42f, 0.55f, 0.26f));
            Material headlight = materials.Solid(new Color(0.82f, 0.91f, 1.00f), true, "_EscapeHeadlampV56");
            Material tailLight = materials.Solid(new Color(0.90f, 0.055f, 0.025f), true, "_EscapeTailLampV56");

            CreatePrimitive(PrimitiveType.Cube, "Sedan Lower Body", root, new Vector3(0f, 0.02f, 0f),
                new Vector3(1.92f, 0.46f, 4.62f), body, Quaternion.identity, true);
            CreatePrimitive(PrimitiveType.Cube, "Sedan Beltline", root, new Vector3(0f, 0.37f, -0.02f),
                new Vector3(1.86f, 0.24f, 3.68f), body, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Sedan Hood", root, new Vector3(0f, 0.39f, 1.63f),
                new Vector3(1.82f, 0.12f, 1.22f), body, Quaternion.Euler(-2f, 0f, 0f), false);
            CreatePrimitive(PrimitiveType.Cube, "Sedan Trunk", root, new Vector3(0f, 0.43f, -1.63f),
                new Vector3(1.80f, 0.18f, 1.10f), body, Quaternion.Euler(2f, 0f, 0f), false);
            CreatePrimitive(PrimitiveType.Cube, "Sedan Roof", root, new Vector3(0f, 1.16f, -0.08f),
                new Vector3(1.64f, 0.10f, 2.10f), body, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Sphere, "Sedan Sculpted Nose", root, new Vector3(0f, 0.30f, 1.92f),
                new Vector3(1.86f, 0.46f, 0.92f), body, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Sphere, "Sedan Sculpted Tail", root, new Vector3(0f, 0.32f, -1.92f),
                new Vector3(1.82f, 0.50f, 0.84f), body, Quaternion.identity, false);
            for (int side = -1; side <= 1; side += 2)
            {
                CreatePrimitive(PrimitiveType.Cube, "Side Rocker", root,
                    new Vector3(side * 0.94f, 0.03f, -0.05f), new Vector3(0.08f, 0.13f, 3.15f),
                    trim, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Door Belt Trim", root,
                    new Vector3(side * 0.985f, 0.67f, -0.22f), new Vector3(0.035f, 0.055f, 2.45f),
                    rim, Quaternion.identity, false);
            }

            GameObject windshield = CreatePrimitive(PrimitiveType.Cube, "Laminated Windshield", root,
                new Vector3(0f, 0.86f, 0.88f), new Vector3(1.64f, 0.66f, 0.035f),
                glass, Quaternion.Euler(-27f, 0f, 0f), false);
            escapeGlassPanels.Add(windshield);
            GameObject rearGlass = CreatePrimitive(PrimitiveType.Cube, "Rear Safety Glass", root,
                new Vector3(0f, 0.87f, -0.92f), new Vector3(1.62f, 0.62f, 0.035f),
                glass, Quaternion.Euler(27f, 0f, 0f), false);
            escapeGlassPanels.Add(rearGlass);

            CreatePrimitive(PrimitiveType.Cube, "A Pillar L", root, new Vector3(-0.82f, 0.88f, 0.86f),
                new Vector3(0.08f, 0.78f, 0.10f), trim, Quaternion.Euler(-24f, 0f, 0f), false);
            CreatePrimitive(PrimitiveType.Cube, "A Pillar R", root, new Vector3(0.82f, 0.88f, 0.86f),
                new Vector3(0.08f, 0.78f, 0.10f), trim, Quaternion.Euler(-24f, 0f, 0f), false);
            CreatePrimitive(PrimitiveType.Cube, "B Pillar L", root, new Vector3(-0.86f, 0.86f, 0.02f),
                new Vector3(0.07f, 0.76f, 0.09f), trim, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "B Pillar R", root, new Vector3(0.86f, 0.86f, 0.02f),
                new Vector3(0.07f, 0.76f, 0.09f), trim, Quaternion.identity, false);

            escapeDoorPivot = new GameObject("Driver Door Hinge").transform;
            escapeDoorPivot.SetParent(root, false);
            escapeDoorPivot.localPosition = new Vector3(0.985f, 0.14f, 0.82f);
            escapeDoorClosedRotation = Quaternion.identity;
            escapeDoorPivot.localRotation = escapeDoorClosedRotation;
            CreatePrimitive(PrimitiveType.Cube, "Driver Door Outer Skin", escapeDoorPivot,
                new Vector3(0f, 0.26f, -0.69f), new Vector3(0.055f, 0.58f, 1.38f), body, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Driver Door Handle", escapeDoorPivot,
                new Vector3(0.045f, 0.52f, -0.91f), new Vector3(0.035f, 0.045f, 0.22f), trim, Quaternion.identity, false);
            GameObject driverGlass = CreatePrimitive(PrimitiveType.Cube, "Driver Door Safety Glass", escapeDoorPivot,
                new Vector3(0f, 0.86f, -0.69f), new Vector3(0.030f, 0.43f, 1.24f), glass, Quaternion.identity, false);
            escapeGlassPanels.Add(driverGlass);

            CreatePrimitive(PrimitiveType.Cube, "Rear Door Outer Skin", root,
                new Vector3(0.985f, 0.40f, -0.93f), new Vector3(0.055f, 0.60f, 1.25f), body, Quaternion.identity, false);
            GameObject rearSideGlass = CreatePrimitive(PrimitiveType.Cube, "Rear Side Safety Glass", root,
                new Vector3(0.985f, 0.99f, -0.92f), new Vector3(0.030f, 0.42f, 1.05f), glass, Quaternion.identity, false);
            escapeGlassPanels.Add(rearSideGlass);
            GameObject oppositeFrontGlass = CreatePrimitive(PrimitiveType.Cube, "Passenger Front Safety Glass", root,
                new Vector3(-0.985f, 0.99f, 0.13f), new Vector3(0.030f, 0.42f, 1.24f), glass, Quaternion.identity, false);
            escapeGlassPanels.Add(oppositeFrontGlass);
            GameObject oppositeRearGlass = CreatePrimitive(PrimitiveType.Cube, "Passenger Rear Safety Glass", root,
                new Vector3(-0.985f, 0.99f, -0.92f), new Vector3(0.030f, 0.42f, 1.05f), glass, Quaternion.identity, false);
            escapeGlassPanels.Add(oppositeRearGlass);

            CreatePrimitive(PrimitiveType.Cube, "Passenger Front Door Skin", root,
                new Vector3(-0.985f, 0.40f, 0.13f), new Vector3(0.055f, 0.60f, 1.30f), body, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Passenger Rear Door Skin", root,
                new Vector3(-0.985f, 0.40f, -0.93f), new Vector3(0.055f, 0.60f, 1.25f), body, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Passenger Front Handle", root,
                new Vector3(-1.025f, 0.54f, -0.10f), new Vector3(0.035f, 0.045f, 0.22f), trim, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Passenger Rear Handle", root,
                new Vector3(-1.025f, 0.54f, -1.15f), new Vector3(0.035f, 0.045f, 0.22f), trim, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Driver Mirror", root,
                new Vector3(1.10f, 0.88f, 0.72f), new Vector3(0.18f, 0.13f, 0.30f), body, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Passenger Mirror", root,
                new Vector3(-1.10f, 0.88f, 0.72f), new Vector3(0.18f, 0.13f, 0.30f), body, Quaternion.identity, false);

            CreatePrimitive(PrimitiveType.Cube, "Front Bumper", root, new Vector3(0f, 0.06f, 2.34f),
                new Vector3(1.82f, 0.18f, 0.12f), trim, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Rear Bumper", root, new Vector3(0f, 0.08f, -2.34f),
                new Vector3(1.82f, 0.18f, 0.12f), trim, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cube, "Front Grille", root, new Vector3(0f, 0.20f, 2.405f),
                new Vector3(0.82f, 0.20f, 0.035f), trim, Quaternion.identity, false);

            for (int side = -1; side <= 1; side += 2)
            {
                CreatePrimitive(PrimitiveType.Cube, "LED Headlamp", root,
                    new Vector3(side * 0.62f, 0.33f, 2.405f), new Vector3(0.38f, 0.16f, 0.035f),
                    headlight, Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "LED Tail Lamp", root,
                    new Vector3(side * 0.64f, 0.35f, -2.405f), new Vector3(0.34f, 0.18f, 0.035f),
                    tailLight, Quaternion.identity, false);
            }

            for (int side = -1; side <= 1; side += 2)
            {
                for (int end = -1; end <= 1; end += 2)
                {
                    Vector3 wheelPosition = new Vector3(side * 1.00f, -0.18f, end * 1.48f);
                    CreatePrimitive(PrimitiveType.Cylinder, "Performance Tire", root, wheelPosition,
                        new Vector3(0.34f, 0.17f, 0.34f), tire, Quaternion.Euler(0f, 0f, 90f), false);
                    CreatePrimitive(PrimitiveType.Cylinder, "Alloy Wheel", root,
                        wheelPosition + new Vector3(side * 0.012f, 0f, 0f),
                        new Vector3(0.22f, 0.175f, 0.22f), rim, Quaternion.Euler(0f, 0f, 90f), false);
                }
            }
            return root;
        }

        private HumanMissionActor AddHuman(
            string characterName,
            bool primary,
            Vector3 position,
            HumanMotionStyle motion,
            float phase,
            Color jacket,
            Color trousers)
        {
            HumanMissionActor actor = HumanMissionActor.Create(
                stageRoot,
                materials,
                characterName,
                primary,
                position,
                motion,
                currentStage,
                phase,
                jacket,
                trousers);
            humans.Add(actor);
            if (primary)
            {
                if (PrimaryHuman == null) PrimaryHuman = actor;
                GameObject readabilityLight = new GameObject("Primary Wardrobe Fill");
                readabilityLight.transform.SetParent(actor.transform, false);
                readabilityLight.transform.localPosition = new Vector3(-0.65f, 1.55f, -1.15f);
                Light fill = readabilityLight.AddComponent<Light>();
                fill.type = LightType.Point;
                fill.color = new Color(1.0f, 0.90f, 0.78f);
                fill.intensity = 0.82f;
                fill.range = 3.6f;
                fill.shadows = LightShadows.None;
            }
            return actor;
        }

        private void CreateRangeFurniture(int stage, float range)
        {
            Material wood = materials.Get(MaterialLibrary.Surface.Planks, Color.white, 0f, 0.15f, "_Furniture");
            Material steel = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel, new Color(0.78f, 0.80f, 0.77f), 0.72f, 0.30f, "_Furniture");
            for (int i = 1; i <= 5; i++)
            {
                float z = range * i / 6f;
                float side = i % 2 == 0 ? -1f : 1f;
                CreatePrimitive(PrimitiveType.Cylinder, "Range Marker Pole", stageRoot,
                    new Vector3(side * 18f, 1.4f, z), new Vector3(0.055f, 1.4f, 0.055f), steel, Quaternion.identity, false);
                GameObject flag = CreatePrimitive(PrimitiveType.Cube, "Wind Flag", stageRoot,
                    new Vector3(side * 18f + side * 0.7f, 2.42f, z), new Vector3(1.35f, 0.36f, 0.025f),
                    materials.Get(MaterialLibrary.Surface.RustedRedSteel, new Color(0.92f, 0.63f, 0.26f), 0.1f, 0.2f, "_Flag"),
                    Quaternion.identity, false);
                WindFlagVisual visual = flag.AddComponent<WindFlagVisual>();
                visual.Side = side;
                visual.Phase = i * 0.83f;
            }

            // Firing bench and padded rest make the foreground feel like a physical shooting position.
            CreatePrimitive(PrimitiveType.Cube, "Firing Bench", stageRoot, new Vector3(0f, 0.78f, 2.3f),
                new Vector3(2.7f, 0.18f, 1.25f), wood, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cylinder, "Bench Leg L", stageRoot, new Vector3(-0.9f, 0.36f, 2.3f),
                new Vector3(0.08f, 0.36f, 0.08f), steel, Quaternion.identity, false);
            CreatePrimitive(PrimitiveType.Cylinder, "Bench Leg R", stageRoot, new Vector3(0.9f, 0.36f, 2.3f),
                new Vector3(0.08f, 0.36f, 0.08f), steel, Quaternion.identity, false);
        }

        private void CreateTargets(StageDefinition definition, Difficulty difficulty)
        {
            float metresPerMil = definition.RangeMetres / 1000f;
            for (int i = 0; i < GameRules.TargetsPerStage; i++)
            {
                float jitter = definition.Targets[i] == TargetKind.Steel ? 0f : Random.Range(-0.09f, 0.09f);
                Vector3 basePosition = new Vector3(
                    (GameRules.LanesMil[i] + jitter) * metresPerMil,
                    BallisticGame.CameraHeight + definition.HeightMil[i] * metresPerMil,
                    definition.RangeMetres);
                TargetMotion motion = EffectiveMotion(definition.Motions[i], currentStage, i, difficulty);
                TargetActor actor = TargetActor.Create(
                    stageRoot,
                    materials,
                    i,
                    definition.Targets[i],
                    motion,
                    basePosition,
                    definition.RangeMetres,
                    currentStage,
                    false);
                targets.Add(actor);
            }

            BonusTarget = TargetActor.Create(
                stageRoot,
                materials,
                99,
                TargetKind.Steel,
                TargetMotion.Static,
                new Vector3(0f, BallisticGame.CameraHeight, definition.RangeMetres),
                definition.RangeMetres,
                currentStage,
                true);
            BonusTarget.gameObject.SetActive(false);
        }

        private static TargetMotion EffectiveMotion(TargetMotion requested, int stage, int index, Difficulty difficulty)
        {
            if (stage == 0 || difficulty == Difficulty.Cadet) return TargetMotion.Static;
            if (difficulty == Difficulty.Shooter && stage < 2 && index != 0) return TargetMotion.Static;
            return requested;
        }

        private void SpawnImpactParticles(Vector3 position, TargetKind kind, bool explosive)
        {
            GameObject particleObject = new GameObject("Impact Particles — " + kind);
            particleObject.transform.SetParent(stageRoot, false);
            particleObject.transform.position = position;
            ParticleSystem system = particleObject.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = explosive ? 1.6f : 0.9f;
            main.startSpeed = explosive ? 12f : 4.5f;
            main.startSize = explosive ? 0.18f : 0.07f;
            main.gravityModifier = 0.75f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(explosive ? 72 : 34)) });
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = explosive ? 0.35f : 0.16f;
            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            Color start = kind == TargetKind.Watermelon ? new Color(0.95f, 0.12f, 0.08f) :
                kind == TargetKind.GlassBottle ? new Color(0.58f, 0.92f, 0.89f) :
                kind == TargetKind.ExplosiveBarrel ? new Color(1f, 0.48f, 0.05f) : new Color(0.68f, 0.55f, 0.38f);
            gradient.SetKeys(
                new[] { new GradientColorKey(start, 0f), new GradientColorKey(start * 0.4f, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = materials.Solid(start, true, "_Particles");
            system.Play();
            particleObject.AddComponent<TimedDestroy>().Lifetime = explosive ? 2.2f : 1.4f;
        }

        private void SpawnHumanImpactParticles(Vector3 position, bool primary, Vector3 shotDirection)
        {
            GameObject particleObject = new GameObject(primary ? "Target Impact Dust" : "Bystander Impact Dust");
            particleObject.transform.SetParent(stageRoot, false);
            particleObject.transform.position = position;
            if (shotDirection.sqrMagnitude > 0.001f)
                particleObject.transform.rotation = Quaternion.LookRotation(shotDirection.normalized, Vector3.up);

            ParticleSystem system = particleObject.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.duration = 0.16f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.58f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 2.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.010f, 0.030f);
            main.gravityModifier = 0.24f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = primary
                ? new Color(0.68f, 0.62f, 0.52f, 0.72f)
                : new Color(0.58f, 0.61f, 0.62f, 0.64f);

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)10, (short)15) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.018f;
            shape.length = 0.05f;

            ParticleSystem.ColorOverLifetimeModule colour = system.colorOverLifetime;
            colour.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.76f, 0.69f, 0.57f), 0f),
                    new GradientColorKey(new Color(0.38f, 0.35f, 0.31f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.72f, 0f),
                    new GradientAlphaKey(0.34f, 0.46f),
                    new GradientAlphaKey(0f, 1f)
                });
            colour.color = gradient;

            ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = materials.Solid(new Color(0.66f, 0.60f, 0.50f), false, "_ImpactDustV53");
            system.Play();
            particleObject.AddComponent<TimedDestroy>().Lifetime = 1.4f;
        }

        private void SpawnHumanBloodMist(Vector3 position, Vector3 shotDirection, bool primary)
        {
            GameObject particleObject = new GameObject(primary ? "Target Blood Mist" : "Bystander Blood Mist");
            particleObject.transform.SetParent(stageRoot, false);
            particleObject.transform.position = position + shotDirection.normalized * 0.025f;
            if (shotDirection.sqrMagnitude > 0.001f)
                particleObject.transform.rotation = Quaternion.LookRotation(shotDirection.normalized, Vector3.up);

            ParticleSystem system = particleObject.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.duration = 0.12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.52f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 2.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.024f);
            main.gravityModifier = 0.42f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.34f, 0.015f, 0.012f, 0.90f),
                new Color(0.62f, 0.025f, 0.018f, 0.82f));

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)8, (short)14) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 13f;
            shape.radius = 0.012f;
            shape.length = 0.08f;

            ParticleSystem.ColorOverLifetimeModule colour = system.colorOverLifetime;
            colour.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.50f, 0.018f, 0.012f), 0f),
                    new GradientColorKey(new Color(0.18f, 0.008f, 0.006f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.88f, 0f),
                    new GradientAlphaKey(0.42f, 0.50f),
                    new GradientAlphaKey(0f, 1f)
                });
            colour.color = gradient;

            ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.6f;
            renderer.velocityScale = 0.12f;
            renderer.sharedMaterial = materials.Solid(new Color(0.48f, 0.018f, 0.012f), false, "_BloodMistV53");
            system.Play();
            particleObject.AddComponent<TimedDestroy>().Lifetime = 1.3f;
        }

        private void SpawnExplosionFlash(Vector3 position)
        {
            GameObject flash = CreatePrimitive(PrimitiveType.Sphere, "Explosion Flash", stageRoot, position,
                Vector3.one * 0.55f, materials.Solid(new Color(1f, 0.27f, 0.03f), true, "_Explosion"), Quaternion.identity, false);
            PointFlash pointFlash = flash.AddComponent<PointFlash>();
            pointFlash.StartScale = 0.55f;
            pointFlash.EndScale = 8.5f;
            pointFlash.Lifetime = 0.52f;
            Light light = flash.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.34f, 0.08f);
            light.range = 28f;
            light.intensity = 6f;
        }

        internal static GameObject CreatePrimitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            Quaternion localRotation,
            bool collider)
        {
            GameObject gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localRotation = localRotation;
            gameObject.transform.localScale = localScale;
            Renderer renderer = gameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                if (material != null && material.HasProperty("_Tiling"))
                {
                    float width = Mathf.Max(Mathf.Abs(localScale.x), Mathf.Abs(localScale.z));
                    float height = Mathf.Max(Mathf.Abs(localScale.y), Mathf.Min(width, 4f));
                    float repeatX = Mathf.Max(1f, width * (type == PrimitiveType.Cube ? 0.30f : 0.16f));
                    float repeatY = Mathf.Max(1f, height * (type == PrimitiveType.Cube ? 0.30f : 0.16f));
                    MaterialPropertyBlock properties = new MaterialPropertyBlock();
                    properties.SetVector("_Tiling", new Vector4(repeatX, repeatY, 0f, 0f));
                    renderer.SetPropertyBlock(properties);
                }
            }
            Collider foundCollider = gameObject.GetComponent<Collider>();
            if (foundCollider == null)
            {
                throw new MissingComponentException(
                    "Primitive collider for " + type + " is unavailable in this Player build.");
            }
            foundCollider.enabled = collider;
            return gameObject;
        }
    }

    public sealed class TargetActor : MonoBehaviour
    {
        private Transform visualRoot;
        private Vector3 basePosition;
        private float range;
        private int stage;
        private bool bonus;
        private float phase;
        private MaterialLibrary materials;

        public int Index { get; private set; }
        public TargetKind Kind { get; private set; }
        public TargetMotion Motion { get; private set; }
        public bool Destroyed { get; private set; }
        public Material PrimaryMaterial { get; private set; }
        public Vector3 Centre => transform.position;

        public static TargetActor Create(
            Transform parent,
            MaterialLibrary materials,
            int index,
            TargetKind kind,
            TargetMotion motion,
            Vector3 basePosition,
            float range,
            int stage,
            bool bonus)
        {
            GameObject root = new GameObject((bonus ? "Bonus " : "") + kind + " Target " + index);
            root.transform.SetParent(parent, false);
            TargetActor actor = root.AddComponent<TargetActor>();
            actor.materials = materials;
            actor.Index = index;
            actor.Kind = kind;
            actor.Motion = motion;
            actor.basePosition = basePosition;
            actor.range = range;
            actor.stage = stage;
            actor.bonus = bonus;
            actor.phase = index * 1.47f + stage * 0.83f;
            actor.transform.position = basePosition;
            actor.BuildVisual();
            return actor;
        }

        public void Tick(float clock)
        {
            if (Destroyed) return;
            float metresPerMil = range / 1000f;
            float speed = 0.72f + stage * 0.10f + Mathf.Max(0, Index) * 0.035f;
            Vector3 position = basePosition;
            if (Motion == TargetMotion.Slide)
            {
                position.x += Mathf.Sin(clock * speed + phase) * metresPerMil * (0.75f + stage * 0.10f);
            }
            else if (Motion == TargetMotion.Pendulum)
            {
                position.x += Mathf.Sin(clock * (speed + 0.26f) + phase) * metresPerMil * (0.55f + stage * 0.07f);
                position.y -= Mathf.Abs(Mathf.Cos(clock * (speed + 0.26f) + phase)) * metresPerMil * 0.16f;
                visualRoot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(clock * (speed + 0.26f) + phase) * 7f);
            }
            else if (Motion == TargetMotion.Bob)
            {
                position.y += Mathf.Sin(clock * (speed + 0.42f) + phase) * metresPerMil * (0.40f + stage * 0.035f);
            }
            transform.position = position;
        }

        public bool ContainsImpact(Vector3 impactPoint)
        {
            Vector2 difference = new Vector2(impactPoint.x - Centre.x, impactPoint.y - Centre.y);
            if (Kind == TargetKind.Steel)
            {
                return difference.magnitude <= 0.48f;
            }
            Vector2 size = GameRules.TargetSize(Kind);
            float normalizedX = difference.x / (size.x * 0.5f);
            float normalizedY = difference.y / (size.y * 0.5f);
            return normalizedX * normalizedX + normalizedY * normalizedY <= 1f;
        }

        public float NormalizedDistance(Vector3 impactPoint)
        {
            Vector2 difference = new Vector2(impactPoint.x - Centre.x, impactPoint.y - Centre.y);
            if (Kind == TargetKind.Steel) return difference.magnitude / 0.48f;
            Vector2 size = GameRules.TargetSize(Kind);
            float normalizedX = difference.x / Mathf.Max(0.01f, size.x * 0.5f);
            float normalizedY = difference.y / Mathf.Max(0.01f, size.y * 0.5f);
            return Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
        }

        public Vector2 ErrorFromCentre(Vector3 impactPoint)
        {
            return new Vector2(impactPoint.x - Centre.x, impactPoint.y - Centre.y);
        }

        public void SetDestroyed(bool destroyed)
        {
            Destroyed = destroyed;
            if (visualRoot != null) visualRoot.gameObject.SetActive(!destroyed);
        }

        public void ResetTarget(bool clearMarks)
        {
            Destroyed = false;
            if (visualRoot != null) visualRoot.gameObject.SetActive(true);
            if (clearMarks)
            {
                Transform marks = transform.Find("Impact Marks");
                if (marks != null)
                {
                    for (int i = marks.childCount - 1; i >= 0; i--) Destroy(marks.GetChild(i).gameObject);
                }
            }
        }

        public void AddImpactMark(Vector2 errorMetres)
        {
            Transform marks = transform.Find("Impact Marks");
            if (marks == null)
            {
                marks = new GameObject("Impact Marks").transform;
                marks.SetParent(transform, false);
            }
            GameObject mark = RangeWorld.CreatePrimitive(PrimitiveType.Sphere, "Bullet Hole", marks,
                new Vector3(errorMetres.x, errorMetres.y, -0.075f), new Vector3(0.025f, 0.025f, 0.010f),
                materials.Solid(new Color(0.025f, 0.018f, 0.012f), false, "_BulletHole"), Quaternion.identity, false);
            if (marks.childCount > 12) Destroy(marks.GetChild(0).gameObject);
        }

        private void BuildVisual()
        {
            visualRoot = new GameObject("Detailed Target Model").transform;
            visualRoot.SetParent(transform, false);
            switch (Kind)
            {
                case TargetKind.Steel: BuildSteel(); break;
                case TargetKind.GlassBottle: BuildBottle(); break;
                case TargetKind.ClayJug: BuildJug(); break;
                case TargetKind.Cans: BuildCans(); break;
                case TargetKind.WoodenCrate: BuildCrate(); break;
                case TargetKind.Watermelon: BuildWatermelon(); break;
                case TargetKind.ExplosiveBarrel: BuildBarrel(); break;
            }
            if (Kind != TargetKind.Steel) BuildPedestal();
        }

        private void BuildReadabilityFrame()
        {
            Vector2 targetSize = GameRules.TargetSize(Kind);
            float width = Mathf.Max(0.78f, targetSize.x + 0.28f);
            float height = Mathf.Max(0.88f, targetSize.y + 0.26f);
            Color accent = Kind == TargetKind.GlassBottle ? new Color(0.10f, 0.88f, 1.00f) :
                Kind == TargetKind.ClayJug ? new Color(1.00f, 0.49f, 0.12f) :
                Kind == TargetKind.Cans ? new Color(0.18f, 0.58f, 1.00f) :
                Kind == TargetKind.WoodenCrate ? new Color(1.00f, 0.76f, 0.18f) :
                Kind == TargetKind.Watermelon ? new Color(0.25f, 1.00f, 0.30f) :
                Kind == TargetKind.ExplosiveBarrel ? new Color(1.00f, 0.12f, 0.055f) :
                new Color(1.00f, 0.94f, 0.78f);
            Material backdrop = materials.Solid(new Color(0.018f, 0.026f, 0.030f), false, "_TargetBackdrop");
            Material rim = materials.Solid(accent, true, "_TargetRim" + Kind);

            RangeWorld.CreatePrimitive(PrimitiveType.Cube, "Contrast Backplate", visualRoot,
                new Vector3(0f, 0f, 0.19f), new Vector3(width, height, 0.055f), backdrop, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cube, "Readability Rim Top", visualRoot,
                new Vector3(0f, height * 0.5f, -0.02f), new Vector3(width + 0.08f, 0.035f, 0.035f), rim, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cube, "Readability Rim Bottom", visualRoot,
                new Vector3(0f, -height * 0.5f, -0.02f), new Vector3(width + 0.08f, 0.035f, 0.035f), rim, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cube, "Readability Rim Left", visualRoot,
                new Vector3(-width * 0.5f, 0f, -0.02f), new Vector3(0.035f, height, 0.035f), rim, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cube, "Readability Rim Right", visualRoot,
                new Vector3(width * 0.5f, 0f, -0.02f), new Vector3(0.035f, height, 0.035f), rim, Quaternion.identity, false);

            GameObject lampObject = new GameObject("Target Accent Light");
            lampObject.transform.SetParent(visualRoot, false);
            lampObject.transform.localPosition = new Vector3(0f, height * 0.20f, -0.55f);
            Light lamp = lampObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = accent;
            lamp.intensity = 0.80f;
            lamp.range = 2.8f;
            lamp.shadows = LightShadows.None;
        }

        private void BuildPedestal()
        {
            Vector2 size = GameRules.TargetSize(Kind);
            float shelfY = -size.y * 0.5f - 0.055f;
            Material stand = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel,
                new Color(0.48f, 0.51f, 0.49f), 0.72f, 0.28f, "_TargetStand");
            RangeWorld.CreatePrimitive(PrimitiveType.Cube, "Target Shelf", visualRoot,
                new Vector3(0f, shelfY, 0.06f), new Vector3(Mathf.Max(0.54f, size.x + 0.16f), 0.055f, 0.42f),
                stand, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Target Stand", visualRoot,
                new Vector3(0f, shelfY - 0.48f, 0.16f), new Vector3(0.035f, 0.48f, 0.035f),
                stand, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cube, "Target Stand Foot", visualRoot,
                new Vector3(0f, shelfY - 0.96f, 0.18f), new Vector3(0.74f, 0.05f, 0.38f),
                stand, Quaternion.identity, false);
        }

        private void BuildSteel()
        {
            Material darkSteel = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel, new Color(0.72f, 0.76f, 0.73f), 0.75f, 0.35f, "_Target");
            Material paper = materials.Get(MaterialLibrary.Surface.PaperTarget, Color.white, 0f, 0.18f, "_Target");
            Material black = materials.Solid(new Color(0.055f, 0.065f, 0.062f), false, "_TargetBlack");
            Material red = materials.Get(MaterialLibrary.Surface.RustedRedSteel, new Color(0.95f, 0.25f, 0.18f), 0.45f, 0.26f, "_Bullseye");
            PrimaryMaterial = darkSteel;

            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Support Pole", visualRoot, new Vector3(0f, -0.72f, 0.11f),
                new Vector3(0.035f, 0.72f, 0.035f), darkSteel, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cube, "Stand Foot", visualRoot, new Vector3(0f, -1.42f, 0.16f),
                new Vector3(0.78f, 0.06f, 0.42f), darkSteel, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Steel Plate", visualRoot, Vector3.zero,
                new Vector3(0.50f, 0.042f, 0.50f), paper, Quaternion.Euler(90f, 0f, 0f), false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Middle Ring", visualRoot, new Vector3(0f, 0f, -0.055f),
                new Vector3(0.25f, 0.012f, 0.25f), black, Quaternion.Euler(90f, 0f, 0f), false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Bullseye", visualRoot, new Vector3(0f, 0f, -0.070f),
                new Vector3(0.105f, 0.009f, 0.105f), red, Quaternion.Euler(90f, 0f, 0f), false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Bullseye Highlight", visualRoot, new Vector3(0f, 0f, -0.080f),
                new Vector3(0.032f, 0.006f, 0.032f), materials.Solid(new Color(1f, 0.83f, 0.28f), true, "_BullseyeGlow"),
                Quaternion.Euler(90f, 0f, 0f), false);
        }

        private void BuildBottle()
        {
            PrimaryMaterial = materials.TransparentGlass(new Color(0.42f, 0.88f, 0.76f, 0.68f));
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Bottle Body", visualRoot, new Vector3(0f, -0.06f, 0f),
                new Vector3(0.15f, 0.23f, 0.15f), PrimaryMaterial, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Sphere, "Bottle Shoulder", visualRoot, new Vector3(0f, 0.19f, 0f),
                new Vector3(0.15f, 0.11f, 0.15f), PrimaryMaterial, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Bottle Neck", visualRoot, new Vector3(0f, 0.30f, 0f),
                new Vector3(0.055f, 0.10f, 0.055f), PrimaryMaterial, Quaternion.identity, false);
        }

        private void BuildJug()
        {
            PrimaryMaterial = materials.Get(MaterialLibrary.Surface.Clay, new Color(0.92f, 0.72f, 0.52f), 0f, 0.16f, "_Jug");
            RangeWorld.CreatePrimitive(PrimitiveType.Sphere, "Jug Body", visualRoot, new Vector3(0f, -0.05f, 0f),
                new Vector3(0.25f, 0.25f, 0.23f), PrimaryMaterial, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Jug Neck", visualRoot, new Vector3(0f, 0.23f, 0f),
                new Vector3(0.09f, 0.10f, 0.09f), PrimaryMaterial, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Jug Handle", visualRoot, new Vector3(0.20f, 0.06f, 0f),
                new Vector3(0.025f, 0.17f, 0.025f), PrimaryMaterial, Quaternion.Euler(0f, 0f, -25f), false);
        }

        private void BuildCans()
        {
            PrimaryMaterial = materials.Get(MaterialLibrary.Surface.CorrugatedSteel, new Color(0.86f, 0.88f, 0.84f), 0.72f, 0.36f, "_Can");
            float radius = 0.10f;
            Vector3[] positions =
            {
                new Vector3(-0.20f, -0.21f, 0f), new Vector3(0f, -0.21f, 0f), new Vector3(0.20f, -0.21f, 0f),
                new Vector3(-0.10f, 0.01f, 0f), new Vector3(0.10f, 0.01f, 0f), new Vector3(0f, 0.23f, 0f)
            };
            for (int i = 0; i < positions.Length; i++)
            {
                RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Can " + (i + 1), visualRoot, positions[i],
                    new Vector3(radius, 0.105f, radius), PrimaryMaterial, Quaternion.identity, false);
            }
        }

        private void BuildCrate()
        {
            PrimaryMaterial = materials.Get(MaterialLibrary.Surface.Planks, Color.white, 0f, 0.14f, "_Crate");
            Material brace = materials.Get(MaterialLibrary.Surface.SplinteredWood, new Color(0.82f, 0.70f, 0.52f), 0f, 0.10f, "_CrateBrace");
            RangeWorld.CreatePrimitive(PrimitiveType.Cube, "Crate Body", visualRoot, Vector3.zero,
                new Vector3(0.72f, 0.66f, 0.55f), PrimaryMaterial, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cube, "Crate Brace A", visualRoot, new Vector3(0f, 0f, -0.29f),
                new Vector3(0.07f, 0.74f, 0.035f), brace, Quaternion.Euler(0f, 0f, 47f), false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cube, "Crate Brace B", visualRoot, new Vector3(0f, 0f, -0.30f),
                new Vector3(0.07f, 0.74f, 0.035f), brace, Quaternion.Euler(0f, 0f, -47f), false);
        }

        private void BuildWatermelon()
        {
            PrimaryMaterial = materials.Get(MaterialLibrary.Surface.WatermelonSkin, Color.white, 0f, 0.25f, "_Watermelon");
            RangeWorld.CreatePrimitive(PrimitiveType.Sphere, "Watermelon", visualRoot, Vector3.zero,
                new Vector3(0.34f, 0.25f, 0.29f), PrimaryMaterial, Quaternion.Euler(0f, 18f, 0f), false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Watermelon Stem", visualRoot, new Vector3(0f, 0.27f, 0f),
                new Vector3(0.018f, 0.05f, 0.018f), materials.Get(MaterialLibrary.Surface.Grass, new Color(0.27f, 0.40f, 0.17f), 0f, 0.2f, "_Stem"),
                Quaternion.Euler(0f, 0f, 18f), false);
        }

        private void BuildBarrel()
        {
            PrimaryMaterial = materials.Get(MaterialLibrary.Surface.RustedRedSteel, Color.white, 0.80f, 0.28f, "_Barrel");
            Material bands = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel, new Color(0.44f, 0.46f, 0.43f), 0.85f, 0.34f, "_BarrelBands");
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Explosive Barrel", visualRoot, Vector3.zero,
                new Vector3(0.31f, 0.44f, 0.31f), PrimaryMaterial, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Top Band", visualRoot, new Vector3(0f, 0.31f, 0f),
                new Vector3(0.325f, 0.035f, 0.325f), bands, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cylinder, "Bottom Band", visualRoot, new Vector3(0f, -0.31f, 0f),
                new Vector3(0.325f, 0.035f, 0.325f), bands, Quaternion.identity, false);
            RangeWorld.CreatePrimitive(PrimitiveType.Cube, "Hazard Stripe", visualRoot, new Vector3(0f, 0f, -0.315f),
                new Vector3(0.43f, 0.12f, 0.012f), materials.Solid(new Color(0.98f, 0.73f, 0.08f), true, "_Hazard"),
                Quaternion.Euler(0f, 0f, 16f), false);
        }
    }

    public sealed class WindFlagVisual : MonoBehaviour
    {
        public float Side = 1f;
        public float Phase;

        private void Update()
        {
            float wind = BallisticGame.Instance != null ? BallisticGame.Instance.CurrentWind : 1f;
            float strength = Mathf.Clamp01(Mathf.Abs(wind) / 6f);
            transform.localRotation = Quaternion.Euler(
                Mathf.Sin(Time.time * (4f + strength * 4f) + Phase) * (3f + strength * 8f),
                wind >= 0f ? 0f : 180f,
                Mathf.Sin(Time.time * 3.1f + Phase) * 4f);
            Vector3 scale = transform.localScale;
            scale.x = Mathf.Max(0.45f, 1.35f * (0.45f + strength * 0.55f));
            transform.localScale = scale;
        }
    }

    public sealed class TimedDestroy : MonoBehaviour
    {
        public float Lifetime = 3f;
        private void Start() => Destroy(gameObject, Lifetime);
    }

    public sealed class PointFlash : MonoBehaviour
    {
        public float StartScale = 0.5f;
        public float EndScale = 8f;
        public float Lifetime = 0.5f;
        private float age;

        private void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / Lifetime);
            float eased = 1f - (1f - t) * (1f - t);
            transform.localScale = Vector3.one * Mathf.Lerp(StartScale, EndScale, eased);
            Light light = GetComponent<Light>();
            if (light != null) light.intensity = Mathf.Lerp(6f, 0f, t);
            if (age >= Lifetime) Destroy(gameObject);
        }
    }
}
