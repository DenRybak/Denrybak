using System;
using UnityEngine;

namespace BallisticSniper
{
    public struct ShotRecord
    {
        public Vector3 Start;
        public Vector3 Impact;
        public Vector3 TargetCentre;
        public BallisticSolution Solution;
        public float VisualDuration;
        public float WindMetresPerSecond;
        public float RangeMetres;
    }

    public static class ProjectileVisualFactory
    {
        private static Mesh bulletMesh;

        public static GameObject Create(string name, Material material)
        {
            GameObject bullet = new GameObject(name);
            MeshFilter filter = bullet.AddComponent<MeshFilter>();
            filter.sharedMesh = BulletMesh();
            MeshRenderer renderer = bullet.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return bullet;
        }

        private static Mesh BulletMesh()
        {
            if (bulletMesh != null) return bulletMesh;

            const int segments = 24;
            float[] heights =
            {
                -0.0145f, -0.0130f, -0.0105f, 0.0045f,
                 0.0105f,  0.0145f,  0.0173f, 0.0190f
            };
            float[] radii =
            {
                0.00335f, 0.00375f, 0.00355f, 0.00355f,
                0.00340f, 0.00275f, 0.00145f, 0.00016f
            };

            int ringCount = heights.Length;
            Vector3[] vertices = new Vector3[ringCount * segments + 2];
            Vector2[] uvs = new Vector2[vertices.Length];
            int vi = 0;
            for (int ring = 0; ring < ringCount; ring++)
            {
                float v = ring / (float)(ringCount - 1);
                for (int s = 0; s < segments; s++)
                {
                    float u = s / (float)segments;
                    float angle = u * Mathf.PI * 2f;
                    vertices[vi] = new Vector3(
                        Mathf.Cos(angle) * radii[ring],
                        heights[ring],
                        Mathf.Sin(angle) * radii[ring]);
                    uvs[vi] = new Vector2(u, v);
                    vi++;
                }
            }

            int bottomCentre = vi++;
            int topCentre = vi;
            vertices[bottomCentre] = new Vector3(0f, heights[0], 0f);
            vertices[topCentre] = new Vector3(0f, heights[ringCount - 1], 0f);
            uvs[bottomCentre] = new Vector2(0.5f, 0f);
            uvs[topCentre] = new Vector2(0.5f, 1f);

            int[] triangles = new int[(ringCount - 1) * segments * 6 + segments * 6];
            int ti = 0;
            for (int ring = 0; ring < ringCount - 1; ring++)
            {
                int lower = ring * segments;
                int upper = (ring + 1) * segments;
                for (int s = 0; s < segments; s++)
                {
                    int n = (s + 1) % segments;
                    int a = lower + s;
                    int b = lower + n;
                    int cc = upper + s;
                    int d = upper + n;
                    triangles[ti++] = a;
                    triangles[ti++] = cc;
                    triangles[ti++] = d;
                    triangles[ti++] = a;
                    triangles[ti++] = d;
                    triangles[ti++] = b;
                }
            }

            for (int s = 0; s < segments; s++)
            {
                int n = (s + 1) % segments;
                triangles[ti++] = bottomCentre;
                triangles[ti++] = s;
                triangles[ti++] = n;

                int topRing = (ringCount - 1) * segments;
                triangles[ti++] = topCentre;
                triangles[ti++] = topRing + n;
                triangles[ti++] = topRing + s;
            }

            bulletMesh = new Mesh { name = "V54 Realistic Rifle Projectile" };
            bulletMesh.vertices = vertices;
            bulletMesh.uv = uvs;
            bulletMesh.triangles = triangles;
            bulletMesh.RecalculateNormals();
            bulletMesh.RecalculateTangents();
            bulletMesh.RecalculateBounds();
            return bulletMesh;
        }
    }

    public sealed class ProjectileTracer : MonoBehaviour
    {
        private ShotRecord shot;
        private float age;
        private Action completed;
        private TrailRenderer trail;

        public void Begin(ShotRecord record, Material trailMaterial, Action onCompleted)
        {
            shot = record;
            completed = onCompleted;
            age = 0f;
            transform.position = record.Start;

            trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = Mathf.Clamp(record.VisualDuration * 0.125f, 0.065f, 0.125f);
            trail.widthMultiplier = 0.00095f;
            trail.minVertexDistance = 0.0035f;
            trail.numCornerVertices = 14;
            trail.numCapVertices = 12;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.generateLightingData = false;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.autodestruct = false;
            trail.sharedMaterial = trailMaterial;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1.00f, -0.22f, -0.22f),
                new Keyframe(0.28f, 0.76f, -0.72f, -0.72f),
                new Keyframe(0.68f, 0.28f, -0.82f, -0.82f),
                new Keyframe(1f, 0.00f, -0.16f, 0f));
            Gradient flightGradient = new Gradient();
            flightGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 0.92f, 0.72f), 0f),
                    new GradientColorKey(new Color(1.00f, 0.61f, 0.18f), 0.48f),
                    new GradientColorKey(new Color(0.56f, 0.17f, 0.035f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.88f, 0f),
                    new GradientAlphaKey(0.62f, 0.24f),
                    new GradientAlphaKey(0.28f, 0.64f),
                    new GradientAlphaKey(0.060f, 0.88f),
                    new GradientAlphaKey(0f, 1f)
                });
            trail.colorGradient = flightGradient;
        }

        private void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / Mathf.Max(0.01f, shot.VisualDuration));
            Vector3 position = ShotPath.Position(shot, t);
            Vector3 next = ShotPath.Position(shot, Mathf.Min(1f, t + 0.004f));
            transform.position = position;
            Vector3 direction = next - position;
            if (direction.sqrMagnitude > 0.000001f)
            {
                transform.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            }
            if (t >= 1f)
            {
                Action callback = completed;
                completed = null;
                callback?.Invoke();
                if (trail != null) trail.transform.SetParent(null, true);
                Destroy(gameObject);
            }
        }
    }

    public static class ShotPath
    {
        public static Vector3 Position(ShotRecord shot, float t)
        {
            t = Mathf.Clamp01(t);
            Vector3 position = Vector3.LerpUnclamped(shot.Start, shot.Impact, t);
            float physicalTime = (float)shot.Solution.TimeSeconds;
            float parabolicLift = 0.5f * (float)Ballistics.Gravity * physicalTime * physicalTime * t * (1f - t);
            position.y += parabolicLift;
            float nonlinearWind = (float)shot.Solution.WindDriftMetres * (t * t - t) * 0.33f;
            position.x += nonlinearWind;
            return position;
        }
    }

    public sealed class KillCamDirector : MonoBehaviour
    {
        private Camera targetCamera;
        private ShotRecord shot;
        private Material bulletMaterial;
        private Material trailMaterial;
        private GameObject bullet;
        private GameObject impactHighlight;
        private TrailRenderer trail;
        private Action completed;
        private float elapsed;
        private float duration;
        private const float ImpactHoldSeconds = 0.42f;
        private float originalNearClip;
        private int variant;
        private bool impactVisible;
        private bool closeUpReported;
        private int impactHoldFrames;

        public bool Active { get; private set; }
        public int Variant => variant;
        public float Progress => duration <= 0f ? 0f : Mathf.Clamp01(elapsed / duration);

        public void Initialize(Camera camera, Material projectileMaterial, Material tracerMaterial)
        {
            targetCamera = camera;
            bulletMaterial = projectileMaterial;
            trailMaterial = tracerMaterial;
        }

        public void Begin(ShotRecord record, int cameraVariant, Action onCompleted)
        {
            shot = record;
            variant = Mathf.Abs(cameraVariant) % GameRules.CinematicNames.Length;
            completed = onCompleted;
            elapsed = 0f;
            duration = 2.15f + (variant % 3) * 0.06f;
            originalNearClip = targetCamera.nearClipPlane;
            targetCamera.nearClipPlane = 0.04f;
            Active = true;
            impactVisible = false;
            closeUpReported = false;
            impactHoldFrames = 0;

            bullet = ProjectileVisualFactory.Create("Kill-cam Realistic Rifle Projectile", bulletMaterial);
            trail = bullet.AddComponent<TrailRenderer>();
            trail.time = 0.175f;
            trail.widthMultiplier = 0.00110f;
            trail.minVertexDistance = 0.0030f;
            trail.numCornerVertices = 16;
            trail.numCapVertices = 14;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.generateLightingData = false;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.autodestruct = false;
            trail.sharedMaterial = trailMaterial;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1.00f, -0.28f, -0.28f),
                new Keyframe(0.22f, 0.84f, -0.60f, -0.60f),
                new Keyframe(0.58f, 0.42f, -0.90f, -0.90f),
                new Keyframe(0.84f, 0.14f, -0.68f, -0.68f),
                new Keyframe(1f, 0.00f, -0.20f, 0f));
            Gradient killGradient = new Gradient();
            killGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1.00f, 0.94f, 0.76f), 0f),
                    new GradientColorKey(new Color(1.00f, 0.62f, 0.18f), 0.46f),
                    new GradientColorKey(new Color(0.56f, 0.16f, 0.03f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.94f, 0f),
                    new GradientAlphaKey(0.68f, 0.24f),
                    new GradientAlphaKey(0.32f, 0.62f),
                    new GradientAlphaKey(0.080f, 0.88f),
                    new GradientAlphaKey(0f, 1f)
                });
            trail.colorGradient = killGradient;

            impactHighlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impactHighlight.name = "Kill-cam Impact Point";
            impactHighlight.transform.localScale = Vector3.one * 0.012f;
            Collider impactCollider = impactHighlight.GetComponent<Collider>();
            if (impactCollider != null) impactCollider.enabled = false;
            MeshRenderer impactRenderer = impactHighlight.GetComponent<MeshRenderer>();
            if (impactRenderer != null) impactRenderer.sharedMaterial = bulletMaterial;
            impactHighlight.SetActive(false);
        }

        public void StopImmediately()
        {
            if (bullet != null) Destroy(bullet);
            if (impactHighlight != null) Destroy(impactHighlight);
            if (targetCamera != null) targetCamera.nearClipPlane = originalNearClip;
            Active = false;
            completed = null;
        }

        private void Update()
        {
            if (!Active || targetCamera == null) return;
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // The replay gives the final third of the clip to impact detail.
            float bulletT = Mathf.Clamp01(t / 0.92f);
            Vector3 bulletPosition = ShotPath.Position(shot, bulletT);
            Vector3 nextPosition = ShotPath.Position(shot, Mathf.Min(1f, bulletT + 0.003f));
            bullet.transform.position = bulletPosition;
            Vector3 bulletDirection = nextPosition - bulletPosition;
            if (bulletDirection.sqrMagnitude > 0.000001f)
            {
                bullet.transform.rotation = Quaternion.FromToRotation(Vector3.up, bulletDirection.normalized);
            }

            if (!impactVisible && bulletT >= 0.995f)
            {
                Vector3 approach = (shot.Impact - shot.Start).normalized;
                if (approach.sqrMagnitude < 0.5f) approach = Vector3.forward;
                impactHighlight.transform.position = shot.Impact - approach * 0.020f;
                impactHighlight.SetActive(true);
                if (trail != null) trail.emitting = false;
                impactVisible = true;
            }

            if (t >= 1f) impactHoldFrames++;
            PoseCamera(t, bulletT, bulletPosition, bulletDirection.normalized);
            if (elapsed >= duration + ImpactHoldSeconds && impactHoldFrames >= 6)
            {
                Finish();
            }
        }

        private void PoseCamera(float progress, float bulletT, Vector3 bulletPosition, Vector3 bulletDirection)
        {
            Vector3 direction = bulletDirection.sqrMagnitude > 0.000001f
                ? bulletDirection.normalized
                : (shot.Impact - shot.Start).normalized;
            if (direction.sqrMagnitude < 0.5f) direction = Vector3.forward;

            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            if (side.sqrMagnitude < 0.5f) side = Vector3.right;
            float sideSign = (variant & 1) == 0 ? 1f : -1f;

            // True chase camera: stay attached to the projectile for almost
            // the entire replay. Variants only change the offset slightly,
            // rather than cutting to unrelated viewpoints.
            int profile = variant % 3;
            float behind = profile == 0 ? 1.25f : profile == 1 ? 1.65f : 1.05f;
            float lateral = profile == 0 ? 0.24f : profile == 1 ? 0.40f : 0.14f;
            float height = profile == 0 ? 0.12f : profile == 1 ? 0.20f : 0.07f;

            Vector3 cameraPosition =
                bulletPosition - direction * behind +
                side * lateral * sideSign +
                Vector3.up * height;
            Vector3 lookAt = bulletPosition + direction * 3.8f;
            float fov = profile == 1 ? 31f : 28f;

            // Only at the final instant ease into a readable impact detail.
            CalculateImpactCloseUp(shot, variant, out Vector3 impactPosition, out Vector3 impactLookAt, out float impactFov);
            float impactBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.94f, 1.0f, progress));
            cameraPosition = Vector3.Lerp(cameraPosition, impactPosition, impactBlend);
            lookAt = Vector3.Lerp(lookAt, impactLookAt, impactBlend);
            fov = Mathf.Lerp(fov, impactFov, impactBlend);

            cameraPosition.y = Mathf.Max(0.10f, cameraPosition.y);
            targetCamera.transform.position = cameraPosition;
            Vector3 forward = lookAt - cameraPosition;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            targetCamera.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            targetCamera.fieldOfView = fov;

            if (!closeUpReported && progress >= 1f && impactHoldFrames >= 3)
            {
                closeUpReported = true;
                Vector3 viewport = targetCamera.WorldToViewportPoint(shot.Impact);
                Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "BALLISTIC_ANDROID_IMPACT_CLOSEUP variant={0} fov={1:0.0} height={2:0.00} distance={3:0.00} viewport={4:0.00},{5:0.00}",
                    variant, targetCamera.fieldOfView,
                    targetCamera.transform.position.y - shot.Impact.y,
                    Vector3.Distance(targetCamera.transform.position, shot.Impact),
                    viewport.x, viewport.y));
            }
        }

        public static void CalculateImpactCloseUp(
            ShotRecord record,
            int cameraVariant,
            out Vector3 cameraPosition,
            out Vector3 lookAt,
            out float fieldOfView)
        {
            Vector3 approach = (record.Impact - record.Start).normalized;
            if (approach.sqrMagnitude < 0.5f) approach = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, approach).normalized;
            if (side.sqrMagnitude < 0.5f) side = Vector3.right;
            float sideSign = (Mathf.Abs(cameraVariant) & 1) == 0 ? 1f : -1f;
            cameraPosition = record.Impact - approach * 1.65f +
                             side * (0.22f * sideSign) + Vector3.up * 0.10f;
            lookAt = record.Impact + Vector3.up * 0.010f;
            fieldOfView = 24f;
        }

        private void Finish()
        {
            Active = false;
            targetCamera.nearClipPlane = originalNearClip;
            if (bullet != null) Destroy(bullet);
            if (impactHighlight != null) Destroy(impactHighlight);
            Action callback = completed;
            completed = null;
            callback?.Invoke();
        }
    }
}
