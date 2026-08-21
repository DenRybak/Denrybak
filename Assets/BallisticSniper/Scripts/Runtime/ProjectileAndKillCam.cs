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
            transform.localScale = new Vector3(0.035f, 0.11f, 0.035f);

            MeshRenderer renderer = GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = bulletMaterial;
            Collider collider = GetComponent<Collider>();
            if (collider != null) collider.enabled = false;

            trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = Mathf.Clamp(record.VisualDuration * 0.22f, 0.08f, 0.36f);
            trail.startWidth = 0.055f;
            trail.endWidth = 0.006f;
            trail.minVertexDistance = 0.7f;
            trail.sharedMaterial = bulletMaterial;
            trail.startColor = new Color(1f, 0.82f, 0.24f, 1f);
            trail.endColor = new Color(1f, 0.23f, 0.03f, 0f);
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
        private float originalNearClip;
        private int variant;
        private bool impactVisible;
        private bool closeUpReported;

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
            duration = 2.55f + (variant % 4) * 0.18f;
            originalNearClip = targetCamera.nearClipPlane;
            targetCamera.nearClipPlane = 0.015f;
            Active = true;
            impactVisible = false;
            closeUpReported = false;

            bullet = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bullet.name = "Kill-cam .308 Projectile";
            bullet.transform.localScale = new Vector3(0.045f, 0.18f, 0.045f);
            Collider bulletCollider = bullet.GetComponent<Collider>();
            if (bulletCollider != null) bulletCollider.enabled = false;
            MeshRenderer renderer = bullet.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = bulletMaterial;
            trail = bullet.AddComponent<TrailRenderer>();
            trail.time = 0.42f;
            trail.startWidth = 0.085f;
            trail.endWidth = 0.008f;
            trail.minVertexDistance = 0.16f;
            trail.sharedMaterial = bulletMaterial;
            trail.startColor = new Color(1f, 0.88f, 0.35f, 1f);
            trail.endColor = new Color(1f, 0.18f, 0.02f, 0f);

            impactHighlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impactHighlight.name = "Kill-cam Impact Point";
            impactHighlight.transform.localScale = Vector3.one * 0.16f;
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
            float bulletT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.82f));
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
                impactHighlight.transform.position = shot.Impact - approach * 0.055f;
                impactHighlight.SetActive(true);
                impactVisible = true;
            }

            PoseCamera(t, bulletT, bulletPosition, bulletDirection.normalized);
            if (t >= 1f)
            {
                Finish();
            }
        }

        private void PoseCamera(float progress, float bulletT, Vector3 bulletPosition, Vector3 bulletDirection)
        {
            Vector3 end = shot.Impact;
            Vector3 side = Vector3.Cross(Vector3.up, bulletDirection).normalized;
            if (side.sqrMagnitude < 0.5f) side = Vector3.right;
            Vector3 cameraPosition;
            Vector3 lookAt;
            float roll = 0f;
            float fov = 38f;

            switch (variant)
            {
                case 0: // Chase camera.
                    cameraPosition = bulletPosition - bulletDirection * 5.8f + Vector3.up * 0.75f;
                    lookAt = bulletPosition + bulletDirection * 8f;
                    fov = 48f;
                    break;
                case 1: // Lateral tracking.
                    cameraPosition = bulletPosition + side * 7f + Vector3.up * 1.2f;
                    lookAt = bulletPosition;
                    fov = 42f;
                    break;
                case 2: // Skimming low camera.
                    cameraPosition = bulletPosition - bulletDirection * 2.8f + side * 1.5f;
                    cameraPosition.y = Mathf.Max(0.18f, Mathf.Min(cameraPosition.y, 0.55f));
                    lookAt = bulletPosition + bulletDirection * 4f;
                    fov = 54f;
                    break;
                case 3: // Top down.
                    cameraPosition = bulletPosition + Vector3.up * 13f - bulletDirection * 1.5f;
                    lookAt = bulletPosition + bulletDirection * 2f;
                    fov = 36f;
                    break;
                case 4: // Orbit left around target.
                {
                    float angle = Mathf.Lerp(-145f, -28f, progress) * Mathf.Deg2Rad;
                    cameraPosition = end + new Vector3(Mathf.Sin(angle) * 8f, 2.8f + Mathf.Sin(progress * Mathf.PI) * 2f, Mathf.Cos(angle) * 8f);
                    lookAt = Vector3.Lerp(bulletPosition, end, progress * 0.55f);
                    fov = 34f;
                    break;
                }
                case 5: // Orbit right around target.
                {
                    float angle = Mathf.Lerp(145f, 28f, progress) * Mathf.Deg2Rad;
                    cameraPosition = end + new Vector3(Mathf.Sin(angle) * 9f, 3.4f, Mathf.Cos(angle) * 9f);
                    lookAt = Vector3.Lerp(bulletPosition, end, progress * 0.60f);
                    fov = 34f;
                    break;
                }
                case 6: // Target POV.
                    cameraPosition = end + new Vector3(0.32f, 0.24f, 0.34f);
                    lookAt = bulletPosition;
                    fov = Mathf.Lerp(31f, 60f, progress);
                    break;
                case 7: // Bullet roll.
                    cameraPosition = bulletPosition - bulletDirection * 1.2f + Vector3.up * 0.12f;
                    lookAt = bulletPosition + bulletDirection * 5f;
                    roll = Mathf.Lerp(0f, 330f, progress);
                    fov = 61f;
                    break;
                case 8: // Long lens.
                    cameraPosition = end - Vector3.forward * 42f + side * 9f + Vector3.up * 5f;
                    lookAt = bulletPosition;
                    fov = 15f;
                    break;
                case 9: // Reverse dolly.
                    cameraPosition = end - Vector3.forward * Mathf.Lerp(2.2f, 16f, progress) + side * 2f + Vector3.up * 1.2f;
                    lookAt = bulletPosition;
                    fov = Mathf.Lerp(58f, 27f, progress);
                    break;
                case 10: // Ridge camera.
                    cameraPosition = Vector3.Lerp(shot.Start, end, 0.72f) + side * 24f + Vector3.up * 11f;
                    lookAt = bulletPosition;
                    fov = 24f;
                    break;
                case 11: // Parallel racing camera.
                    cameraPosition = bulletPosition + side * 3.2f + Vector3.up * 0.35f - bulletDirection * 0.8f;
                    lookAt = bulletPosition + bulletDirection * 1.5f;
                    fov = 47f;
                    break;
                case 12: // Impact macro.
                    cameraPosition = end + side * 0.72f + Vector3.up * 0.34f - Vector3.forward * 0.42f;
                    lookAt = Vector3.Lerp(bulletPosition, end, Mathf.SmoothStep(0f, 1f, progress));
                    fov = Mathf.Lerp(28f, 16f, progress);
                    break;
                default: // Crane descending into impact.
                    cameraPosition = end + side * Mathf.Lerp(15f, 5f, progress) +
                                     Vector3.up * Mathf.Lerp(18f, 2.7f, progress) -
                                     Vector3.forward * Mathf.Lerp(12f, 3f, progress);
                    lookAt = Vector3.Lerp(bulletPosition, end, progress * 0.72f);
                    fov = Mathf.Lerp(39f, 28f, progress);
                    break;
            }

            // Every variant ends on the same readable impact composition:
            // near the target plane, from the shooter's side, with a tight
            // lens and enough hold time to inspect the exact hit location.
            CalculateImpactCloseUp(shot, variant, out Vector3 impactPosition, out Vector3 impactLookAt, out float impactFov);
            float impactBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.67f, 0.87f, progress));
            cameraPosition = Vector3.Lerp(cameraPosition, impactPosition, impactBlend);
            lookAt = Vector3.Lerp(lookAt, impactLookAt, impactBlend);
            fov = Mathf.Lerp(fov, impactFov, impactBlend);
            roll = Mathf.Lerp(roll, 0f, impactBlend);

            cameraPosition.y = Mathf.Max(0.15f, cameraPosition.y);
            targetCamera.transform.position = cameraPosition;
            Vector3 forward = lookAt - cameraPosition;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            targetCamera.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up) * Quaternion.AngleAxis(roll, Vector3.forward);
            targetCamera.fieldOfView = fov;

            if (!closeUpReported && progress >= 0.84f)
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
            cameraPosition = record.Impact - approach * 2.60f +
                             side * (0.42f * sideSign) + Vector3.up * 0.20f;
            lookAt = record.Impact + Vector3.up * 0.015f;
            fieldOfView = 17f;
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
