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

    public sealed class ProjectileTracer : MonoBehaviour
    {
        private ShotRecord shot;
        private float age;
        private Action completed;
        private TrailRenderer trail;

        public void Begin(ShotRecord record, Material bulletMaterial, Action onCompleted)
        {
            shot = record;
            completed = onCompleted;
            age = 0f;
            transform.position = record.Start;
            transform.localScale = new Vector3(0.008f, 0.050f, 0.008f);

            MeshRenderer renderer = GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = bulletMaterial;
            Collider collider = GetComponent<Collider>();
            if (collider != null) collider.enabled = false;

            trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = Mathf.Clamp(record.VisualDuration * 0.09f, 0.035f, 0.10f);
            trail.startWidth = 0.006f;
            trail.endWidth = 0.0004f;
            trail.minVertexDistance = 0.09f;
            trail.numCornerVertices = 2;
            trail.numCapVertices = 2;
            trail.sharedMaterial = bulletMaterial;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.15f),
                new Keyframe(0.16f, 0.62f),
                new Keyframe(0.38f, 0.28f),
                new Keyframe(0.62f, 0.74f),
                new Keyframe(0.82f, 0.34f),
                new Keyframe(1f, 0f));
            Gradient flightGradient = new Gradient();
            flightGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.96f, 0.82f), 0f),
                    new GradientColorKey(new Color(1f, 0.78f, 0.34f), 0.42f),
                    new GradientColorKey(new Color(0.95f, 0.42f, 0.12f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.70f, 0f),
                    new GradientAlphaKey(0.18f, 0.20f),
                    new GradientAlphaKey(0.48f, 0.42f),
                    new GradientAlphaKey(0.10f, 0.65f),
                    new GradientAlphaKey(0.26f, 0.82f),
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

        public void Initialize(Camera camera, Material material)
        {
            targetCamera = camera;
            bulletMaterial = material;
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

            bullet = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bullet.name = "Kill-cam .308 Projectile";
            bullet.transform.localScale = new Vector3(0.010f, 0.070f, 0.010f);
            Collider bulletCollider = bullet.GetComponent<Collider>();
            if (bulletCollider != null) bulletCollider.enabled = false;
            MeshRenderer renderer = bullet.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = bulletMaterial;
            trail = bullet.AddComponent<TrailRenderer>();
            trail.time = 0.14f;
            trail.startWidth = 0.008f;
            trail.endWidth = 0.0005f;
            trail.minVertexDistance = 0.055f;
            trail.numCornerVertices = 3;
            trail.numCapVertices = 2;
            trail.sharedMaterial = bulletMaterial;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.20f),
                new Keyframe(0.12f, 0.72f),
                new Keyframe(0.30f, 0.36f),
                new Keyframe(0.48f, 0.82f),
                new Keyframe(0.70f, 0.26f),
                new Keyframe(0.88f, 0.54f),
                new Keyframe(1f, 0f));
            Gradient killGradient = new Gradient();
            killGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.98f, 0.90f), 0f),
                    new GradientColorKey(new Color(1f, 0.80f, 0.38f), 0.46f),
                    new GradientColorKey(new Color(0.95f, 0.34f, 0.08f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.78f, 0f),
                    new GradientAlphaKey(0.18f, 0.18f),
                    new GradientAlphaKey(0.52f, 0.38f),
                    new GradientAlphaKey(0.12f, 0.60f),
                    new GradientAlphaKey(0.32f, 0.80f),
                    new GradientAlphaKey(0f, 1f)
                });
            trail.colorGradient = killGradient;

            impactHighlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impactHighlight.name = "Kill-cam Impact Point";
            impactHighlight.transform.localScale = Vector3.one * 0.024f;
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
