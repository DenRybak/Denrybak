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
        private int currentStage;
        private float currentRange;
        private CampaignMode currentMode;

        public IReadOnlyList<TargetActor> Targets => targets;
        public IReadOnlyList<HumanMissionActor> Humans => humans;
        public HumanMissionActor PrimaryHuman { get; private set; }
        public TargetActor BonusTarget { get; private set; }
        public MaterialLibrary Materials => materials;

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
                CreateTerrain(environmentStage, currentRange);
                CreateEnvironment(environmentStage, currentRange);
                CreateGroundScatter(environmentStage, currentRange);
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
            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].Tick(clock);
            }
            if (BonusTarget != null && BonusTarget.gameObject.activeSelf)
            {
                BonusTarget.Tick(clock);
            }
            for (int i = 0; i < humans.Count; i++)
            {
                humans[i].Tick(clock);
            }
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
                // The roof parapet hides the lower body. The ventilation box
                // adds a second hard obstruction near the right patrol point.
                if (impactPoint.y < 6.48f) return true;
                if (impactPoint.x > 0.62f && impactPoint.x < 1.52f && impactPoint.y < 7.42f) return true;
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
            SpawnHumanImpactParticles(impactPoint, actor.IsPrimary);
            actor.ActivateRagdoll(impactPoint, shotDirection, impulse);
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
            sun.shadowBias = 0.045f;
            sun.shadowNormalBias = 0.35f;
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
                               Resources.Load<Shader>("BallisticSniper/Shaders/GradientSky") ??
                               Shader.Find("BallisticSniper/GradientSky") ??
                               Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                skyboxMaterial = new Material(skyShader) { name = "Runtime Cinematic Panorama Sky" };
                Texture2D panorama = Resources.Load<Texture2D>("BallisticSniper/Textures/range_panorama_v4");
                if (panorama != null && skyboxMaterial.HasProperty("_PanoramaTex"))
                    skyboxMaterial.SetTexture("_PanoramaTex", panorama);
                RenderSettings.skybox = skyboxMaterial;
            }
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
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.ambientSkyColor = Color.Lerp(zenithColors[stage], horizonColors[stage], 0.42f);
            RenderSettings.ambientEquatorColor = horizonColors[stage] * 0.56f;
            RenderSettings.ambientGroundColor = fogColors[stage] * 0.36f;
            RenderSettings.reflectionIntensity = 0.48f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColors[stage];
            RenderSettings.fogStartDistance = currentRange * 0.72f;
            RenderSettings.fogEndDistance = currentRange + 520f;

            sun.color = sunColors[stage];
            sun.intensity = stage == 0 ? 1.18f : 1.12f;
            float sunYaw = -38f + stage * 19f;
            sun.transform.rotation = Quaternion.Euler(28f + stage * 4f, sunYaw, 0f);
            skyFill.color = Color.Lerp(horizonColors[stage], Color.white, 0.12f);
            skyFill.intensity = stage == 3 ? 0.14f : 0.20f;
            skyFill.transform.rotation = Quaternion.Euler(48f, sunYaw + 165f, 0f);

            if (skyboxMaterial != null)
            {
                if (skyboxMaterial.HasProperty("_PanoramaTex"))
                {
                    Color tint = Color.Lerp(Color.white, horizonColors[stage], currentMode == CampaignMode.Operations ? 0.10f : 0.06f);
                    skyboxMaterial.SetColor("_Tint", tint);
                    skyboxMaterial.SetFloat("_Exposure", stage == 3 ? 0.92f : 1.04f);
                    skyboxMaterial.SetFloat("_Rotation", stage * 34f + (currentMode == CampaignMode.Operations ? 14f : 0f));
                    skyboxMaterial.SetFloat("_HorizonBoost", stage == 3 ? 0.04f : 0.10f);
                }
                else if (skyboxMaterial.HasProperty("_HorizonColor"))
                {
                    skyboxMaterial.SetColor("_HorizonColor", horizonColors[stage]);
                    skyboxMaterial.SetColor("_ZenithColor", zenithColors[stage]);
                    skyboxMaterial.SetColor("_GroundColor", fogColors[stage] * 0.52f);
                    skyboxMaterial.SetColor("_SunColor", sunColors[stage]);
                    skyboxMaterial.SetVector("_SunDirection", -sun.transform.forward);
                    skyboxMaterial.SetFloat("_SunIntensity", stage == 2 ? 0.34f : 0.42f);
                }
                else
                {
                    skyboxMaterial.SetColor("_SkyTint", zenithColors[stage]);
                    skyboxMaterial.SetColor("_GroundColor", fogColors[stage] * 0.52f);
                    skyboxMaterial.SetFloat("_AtmosphereThickness", 0.68f);
                    skyboxMaterial.SetFloat("_SunSize", 0.025f);
                    skyboxMaterial.SetFloat("_SunSizeConvergence", 5.0f);
                    skyboxMaterial.SetFloat("_Exposure", 0.82f);
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
                    HumanMotionStyle.Conversation, 0.20f, new Color(0.56f, 0.075f, 0.065f), new Color(0.10f, 0.11f, 0.13f));
                AddHuman("SPEAKER", false, new Vector3(0.24f, floorY, currentRange - 0.34f),
                    HumanMotionStyle.CrossingSpeaker, 2.10f, new Color(0.12f, 0.28f, 0.46f), new Color(0.13f, 0.16f, 0.20f));
                AddHuman("SECURITY", false, new Vector3(2.15f, floorY, currentRange + 0.35f),
                    HumanMotionStyle.Guard, 4.30f, new Color(0.12f, 0.14f, 0.16f), new Color(0.08f, 0.09f, 0.10f));
            }
            else if (operation.Kind == OperationKind.HotelWindow)
            {
                CreatePrimitive(PrimitiveType.Cube, "Hotel Foundation", stageRoot,
                    new Vector3(0f, -0.18f, currentRange + 1.1f), new Vector3(12f, 0.35f, 7f), concrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Facade Left", stageRoot,
                    new Vector3(-3.38f, 2.15f, currentRange), new Vector3(5.30f, 4.30f, 0.36f), darkConcrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Facade Right", stageRoot,
                    new Vector3(3.38f, 2.15f, currentRange), new Vector3(5.30f, 4.30f, 0.36f), darkConcrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Facade Sill", stageRoot,
                    new Vector3(0f, 0.50f, currentRange), new Vector3(1.46f, 1.0f, 0.36f), concrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Facade Header", stageRoot,
                    new Vector3(0f, 3.20f, currentRange), new Vector3(1.46f, 2.20f, 0.36f), concrete, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Window Frame L", stageRoot,
                    new Vector3(-0.71f, 1.56f, currentRange - 0.20f), new Vector3(0.09f, 1.18f, 0.10f), steel, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Window Frame R", stageRoot,
                    new Vector3(0.71f, 1.56f, currentRange - 0.20f), new Vector3(0.09f, 1.18f, 0.10f), steel, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Window Glass", stageRoot,
                    new Vector3(0f, 1.56f, currentRange - 0.10f), new Vector3(1.32f, 1.04f, 0.018f),
                    materials.TransparentGlass(new Color(0.55f, 0.78f, 0.90f, 0.23f)), Quaternion.identity, false);
                CreatePrimitive(PrimitiveType.Cube, "Room Floor", stageRoot,
                    new Vector3(0f, -0.08f, currentRange + 2.10f), new Vector3(5.2f, 0.18f, 4f), wood, Quaternion.identity, true);
                CreatePrimitive(PrimitiveType.Cube, "Interior Lamp", stageRoot,
                    new Vector3(-1.7f, 2.42f, currentRange + 1.3f), new Vector3(0.18f, 0.18f, 0.18f), warm, Quaternion.identity, false);

                AddHuman("MOROZOV", true, new Vector3(0f, 0.02f, currentRange + 0.52f),
                    HumanMotionStyle.WindowPatrol, 0.35f, new Color(0.075f, 0.30f, 0.21f), new Color(0.09f, 0.11f, 0.12f));
                AddHuman("ROOM ATTENDANT", false, new Vector3(0.12f, 0.02f, currentRange + 0.28f),
                    HumanMotionStyle.CrossingSpeaker, 3.25f, new Color(0.58f, 0.62f, 0.66f), new Color(0.16f, 0.19f, 0.22f));
            }
            else
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
            }

            GameObject keyObject = new GameObject("Operation Key Light");
            keyObject.transform.SetParent(stageRoot, false);
            keyObject.transform.position = new Vector3(-2.2f, operationStage == 2 ? 10f : 5f, currentRange - 3f);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Point;
            key.color = operationStage == 1 ? new Color(1f, 0.70f, 0.42f) : new Color(0.72f, 0.84f, 1f);
            key.intensity = 2.2f;
            key.range = 18f;
            key.shadows = LightShadows.Soft;
        }

        private void AddHuman(
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
            if (primary) PrimaryHuman = actor;
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

        private void SpawnHumanImpactParticles(Vector3 position, bool primary)
        {
            GameObject particleObject = new GameObject(primary ? "Target Fabric Impact" : "Bystander Fabric Impact");
            particleObject.transform.SetParent(stageRoot, false);
            particleObject.transform.position = position;
            ParticleSystem system = particleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.duration = 0.18f;
            main.startLifetime = 0.48f;
            main.startSpeed = 2.6f;
            main.startSize = 0.045f;
            main.gravityModifier = 0.38f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = primary ? new Color(0.95f, 0.74f, 0.34f) : new Color(0.55f, 0.76f, 0.92f);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)22) });
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 24f;
            shape.radius = 0.055f;
            ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = materials.Solid(primary ? new Color(1f, 0.62f, 0.18f) : new Color(0.45f, 0.70f, 0.95f), true, "_FabricImpact");
            system.Play();
            particleObject.AddComponent<TimedDestroy>().Lifetime = 1.1f;
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
            BuildReadabilityFrame();
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
