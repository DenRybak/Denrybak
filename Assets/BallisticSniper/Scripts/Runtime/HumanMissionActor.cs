using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BallisticSniper
{
    public enum HumanMotionStyle
    {
        Static,
        Conversation,
        TargetPatrol,
        CivilianWalk,
        WindowPatrol,
        CrossingSpeaker,
        RooftopPatrol,
        Guard
    }

    public enum HumanBodyZone
    {
        Head,
        Torso,
        Pelvis,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg
    }

    public sealed class HumanHitPart : MonoBehaviour
    {
        public HumanMissionActor Owner { get; private set; }
        public HumanBodyZone Zone { get; private set; }

        public void Initialize(HumanMissionActor owner, HumanBodyZone zone)
        {
            Owner = owner;
            Zone = zone;
        }
    }

    /// <summary>
    /// Volumetric mission character with a lightweight procedural skeleton.
    /// No billboard art is used: every visible body region is a real mesh,
    /// receives lighting/shadows and owns a physical collider. Before impact
    /// the skeleton is animated kinematically; a hit releases the same bones
    /// into a jointed ragdoll.
    /// </summary>
    public sealed class HumanMissionActor : MonoBehaviour
    {
        private readonly List<Rigidbody> bodies = new List<Rigidbody>();
        private Vector3 basePosition;
        private HumanMotionStyle motion;
        private float phase;
        private int operationStage;
        private bool ragdolled;
        private float facingYaw;
        private float lastTickClock;
        private Vector3 previousKinematicPosition;
        private bool hasPreviousKinematicPosition;
        private float walkCycle;
        private Transform rideVehicle;
        private Vector3 rideSeatLocal;
        private Vector3 rideEntryStart;
        private float rideStartClock;

        private Transform chest;
        private Transform pelvis;
        private Transform head;
        private Transform leftUpperArm;
        private Transform rightUpperArm;
        private Transform leftForearm;
        private Transform rightForearm;
        private Transform leftThigh;
        private Transform rightThigh;
        private Transform leftCalf;
        private Transform rightCalf;

        public bool IsPrimary { get; private set; }
        public bool IsRagdolled => ragdolled;
        public Vector3 AimCentre => transform.position + Vector3.up * 1.34f;
        public Vector3 ReplayFocus => chest != null ? chest.position : AimCentre;
        public float Depth => transform.position.z;
        public IReadOnlyList<Rigidbody> Bodies => bodies;
        public HumanBodyZone LastHitZone { get; private set; } = HumanBodyZone.Torso;

        public static HumanMissionActor Create(
            Transform parent,
            MaterialLibrary materials,
            string characterName,
            bool primary,
            Vector3 position,
            HumanMotionStyle motionStyle,
            int stage,
            float phaseOffset,
            Color jacketColor,
            Color trouserColor)
        {
            GameObject root = new GameObject((primary ? "MISSION TARGET — " : "CIVILIAN — ") + characterName);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            HumanMissionActor actor = root.AddComponent<HumanMissionActor>();
            actor.IsPrimary = primary;
            actor.basePosition = position;
            actor.motion = motionStyle;
            actor.operationStage = stage;
            actor.phase = phaseOffset;
            actor.BuildRig(materials, jacketColor, trouserColor, primary);

            Animator animator = root.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            actor.Tick(0f);
            return actor;
        }

        public void BeginVehicleEscape(Transform vehicle, Vector3 localSeat, float clock)
        {
            if (ragdolled || vehicle == null) return;
            rideVehicle = vehicle;
            rideSeatLocal = localSeat;
            rideEntryStart = transform.position;
            rideStartClock = clock;
            motion = HumanMotionStyle.Static;
            hasPreviousKinematicPosition = false;
        }

        public void Tick(float clock)
        {
            if (ragdolled) return;

            if (rideVehicle != null)
            {
                float entry = Mathf.Clamp01((clock - rideStartClock) / 0.95f);
                float eased = entry * entry * (3f - 2f * entry);
                Vector3 seat = rideVehicle.TransformPoint(rideSeatLocal);
                Vector3 position = Vector3.Lerp(rideEntryStart, seat, eased);
                position += Vector3.up * (Mathf.Sin(entry * Mathf.PI) * 0.42f);
                transform.position = position;
                facingYaw = rideVehicle.eulerAngles.y;
                transform.rotation = Quaternion.Euler(0f, facingYaw, 0f);
                AnimatePose(clock, Mathf.Sin(clock * 1.72f + phase), 0f, entry < 1f ? 0.35f : 0f);
                return;
            }

            Vector3 position = basePosition;
            float gesture = Mathf.Sin(clock * 1.72f + phase);
            float slower = Mathf.Sin(clock * 0.58f + phase);
            float travel = 0f;
            float yaw = 0f;

            switch (motion)
            {
                case HumanMotionStyle.Conversation:
                    position.x += slower * (IsPrimary ? 0.34f : 0.46f);
                    position.z += Mathf.Cos(clock * 0.44f + phase) * 0.08f;
                    yaw = gesture * 11f + (IsPrimary ? -7f : 7f);
                    travel = Mathf.Cos(clock * 0.58f + phase) * 0.28f;
                    break;

                case HumanMotionStyle.TargetPatrol:
                {
                    float p = Mathf.PingPong(clock * 0.34f + phase, 2f) - 1f;
                    float eased = p * p * (3f - 2f * Mathf.Abs(p));
                    position.x += eased * 1.35f;
                    position.z += Mathf.Sin(clock * 0.31f + phase) * 0.18f;
                    travel = Mathf.Cos(clock * 1.07f + phase);
                    yaw = travel >= 0f ? 14f : -14f;
                    break;
                }

                case HumanMotionStyle.CivilianWalk:
                {
                    float p = Mathf.PingPong(clock * 0.29f + phase, 2f) - 1f;
                    position.x += p * 2.05f;
                    position.z += Mathf.Sin(clock * 0.37f + phase) * 0.28f;
                    travel = Mathf.Cos(clock * 0.91f + phase);
                    yaw = travel >= 0f ? 28f : -28f;
                    break;
                }

                case HumanMotionStyle.WindowPatrol:
                    position.x += Mathf.Sin(clock * 0.74f + phase) * 0.72f;
                    travel = Mathf.Cos(clock * 0.74f + phase);
                    yaw = travel >= 0f ? 10f : -10f;
                    break;

                case HumanMotionStyle.CrossingSpeaker:
                    position.x += Mathf.Sin(clock * 0.88f + phase) * 0.92f;
                    position.z -= 0.20f + Mathf.Cos(clock * 0.63f + phase) * 0.12f;
                    travel = Mathf.Cos(clock * 0.88f + phase);
                    yaw = 12f + gesture * 15f;
                    break;

                case HumanMotionStyle.RooftopPatrol:
                    position.x += Mathf.Sin(clock * 0.46f + phase) * 1.18f;
                    travel = Mathf.Cos(clock * 0.46f + phase);
                    yaw = travel >= 0f ? 15f : -15f;
                    break;

                case HumanMotionStyle.Guard:
                    position.x += Mathf.Sin(clock * 0.31f + phase) * 0.34f;
                    travel = Mathf.Cos(clock * 0.31f + phase) * 0.35f;
                    yaw = Mathf.Sin(clock * 0.27f + phase) * 24f;
                    break;
            }

            float targetYaw = yaw;
            bool walking =
                motion == HumanMotionStyle.TargetPatrol ||
                motion == HumanMotionStyle.CivilianWalk ||
                motion == HumanMotionStyle.WindowPatrol ||
                motion == HumanMotionStyle.CrossingSpeaker ||
                motion == HumanMotionStyle.RooftopPatrol;
            if (walking && Mathf.Abs(travel) > 0.045f)
            {
                float walkFacing = travel >= 0f ? 68f : -68f;
                targetYaw = Mathf.Lerp(yaw, walkFacing, 0.82f);
            }

            float tickDelta = Mathf.Clamp(clock - lastTickClock, 0f, 0.08f);
            lastTickClock = clock;

            Vector3 displacement = hasPreviousKinematicPosition
                ? position - previousKinematicPosition
                : Vector3.zero;
            previousKinematicPosition = position;
            hasPreviousKinematicPosition = true;

            Vector3 planarDisplacement = new Vector3(displacement.x, 0f, displacement.z);
            float groundSpeed = tickDelta > 0.0001f ? planarDisplacement.magnitude / tickDelta : 0f;
            if (walking && planarDisplacement.sqrMagnitude > 0.000001f)
            {
                // Drive the gait phase from actual travelled distance so the
                // feet no longer cycle independently of the body's motion.
                walkCycle += planarDisplacement.magnitude / 0.62f * Mathf.PI * 2f;
                targetYaw = Mathf.Atan2(planarDisplacement.x, planarDisplacement.z) * Mathf.Rad2Deg;
            }

            float turnBlend = 1f - Mathf.Exp(-tickDelta * 9.0f);
            facingYaw = Mathf.LerpAngle(facingYaw, targetYaw, turnBlend);

            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, facingYaw, 0f);
            AnimatePose(clock, gesture, travel, groundSpeed);
        }

        public bool ContainsImpact(Vector3 impactPoint)
        {
            Vector2 point = new Vector2(impactPoint.x - transform.position.x, impactPoint.y - transform.position.y);
            bool headHit = Ellipse(point, new Vector2(0f, 1.75f), new Vector2(0.22f, 0.25f)) <= 1f;
            bool torsoHit = Ellipse(point, new Vector2(0f, 1.29f), new Vector2(0.34f, 0.44f)) <= 1f;
            bool pelvisHit = Ellipse(point, new Vector2(0f, 0.91f), new Vector2(0.29f, 0.23f)) <= 1f;
            bool legsHit = Mathf.Abs(point.x) <= 0.28f && point.y >= 0.03f && point.y <= 0.79f;
            bool armsHit = Mathf.Abs(point.x) <= 0.54f && point.y >= 0.78f && point.y <= 1.52f;
            return headHit || torsoHit || pelvisHit || legsHit || armsHit;
        }

        public float NormalizedDistance(Vector3 impactPoint)
        {
            Vector2 point = new Vector2(impactPoint.x - transform.position.x, impactPoint.y - transform.position.y);
            float headDistance = Ellipse(point, new Vector2(0f, 1.75f), new Vector2(0.22f, 0.25f));
            float torsoDistance = Ellipse(point, new Vector2(0f, 1.29f), new Vector2(0.34f, 0.44f));
            float legsDistance = Ellipse(point, new Vector2(0f, 0.43f), new Vector2(0.30f, 0.46f));
            return Mathf.Min(headDistance, torsoDistance, legsDistance);
        }

        public Vector2 ErrorFromCentre(Vector3 impactPoint)
        {
            return new Vector2(impactPoint.x - AimCentre.x, impactPoint.y - AimCentre.y);
        }

        public HumanBodyZone HitZoneAt(Vector3 impactPoint)
        {
            Vector3 local = transform.InverseTransformPoint(impactPoint);
            if (local.y >= 1.55f) return HumanBodyZone.Head;
            if (local.y >= 1.04f && Mathf.Abs(local.x) < 0.31f) return HumanBodyZone.Torso;
            if (local.y >= 0.76f && Mathf.Abs(local.x) < 0.29f) return HumanBodyZone.Pelvis;
            if (local.y >= 0.77f) return local.x < 0f ? HumanBodyZone.LeftArm : HumanBodyZone.RightArm;
            return local.x < 0f ? HumanBodyZone.LeftLeg : HumanBodyZone.RightLeg;
        }

        public void ActivateRagdoll(Vector3 impactPoint, Vector3 shotDirection, float impulse)
        {
            if (ragdolled) return;
            ragdolled = true;
            LastHitZone = HitZoneAt(impactPoint);

            Rigidbody struck = null;
            float nearest = float.MaxValue;
            for (int i = 0; i < bodies.Count; i++)
            {
                Rigidbody body = bodies[i];
                body.isKinematic = false;
                body.useGravity = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                float distance = Vector3.SqrMagnitude(body.worldCenterOfMass - impactPoint);
                if (distance < nearest)
                {
                    nearest = distance;
                    struck = body;
                }
            }

            Vector3 direction = shotDirection.sqrMagnitude > 0.01f ? shotDirection.normalized : Vector3.forward;
            if (struck != null)
            {
                struck.AddForceAtPosition(
                    direction * impulse + Vector3.up * impulse * 0.14f,
                    impactPoint,
                    ForceMode.Impulse);
                struck.AddTorque(new Vector3(impulse * 0.10f, impulse * 0.07f, -impulse * 0.13f), ForceMode.Impulse);
            }

            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] == struck) continue;
                bodies[i].AddForce(direction * impulse * 0.045f, ForceMode.Impulse);
            }
        }

        private void AnimatePose(float clock, float gesture, float travel, float groundSpeed)
        {
            bool walking =
                motion == HumanMotionStyle.TargetPatrol ||
                motion == HumanMotionStyle.CivilianWalk ||
                motion == HumanMotionStyle.WindowPatrol ||
                motion == HumanMotionStyle.CrossingSpeaker ||
                motion == HumanMotionStyle.RooftopPatrol;

            float moveAmount = walking
                ? Mathf.Clamp01(0.34f + groundSpeed * 0.92f)
                : motion == HumanMotionStyle.Guard ? 0.18f : 0.08f;

            float cycle = walking ? walkCycle + phase * 0.35f : clock * 2.25f + phase;
            float step = Mathf.Sin(cycle);
            float halfStep = Mathf.Sin(cycle * 0.5f);
            float footLiftL = Mathf.Max(0f, Mathf.Sin(cycle + 0.18f)) * moveAmount;
            float footLiftR = Mathf.Max(0f, Mathf.Sin(cycle + Mathf.PI + 0.18f)) * moveAmount;
            float stride = step * 42f * moveAmount;
            float bob = Mathf.Abs(Mathf.Sin(cycle)) * 0.050f * moveAmount;
            float hipSway = halfStep * 0.034f * moveAmount;
            float torsoTwist = -step * 5.8f * moveAmount;
            float talk = motion == HumanMotionStyle.Conversation || motion == HumanMotionStyle.CrossingSpeaker
                ? gesture
                : gesture * 0.08f;

            if (pelvis != null)
            {
                pelvis.localPosition = new Vector3(hipSway, 0.90f + bob, 0f);
                pelvis.localRotation = Quaternion.Euler(
                    -Mathf.Abs(step) * 1.8f * moveAmount,
                    step * 3.2f * moveAmount,
                    -step * 4.2f * moveAmount);
            }

            if (chest != null)
            {
                chest.localPosition = new Vector3(-hipSway * 0.52f, 1.29f + bob * 0.84f, 0f);
                chest.localRotation = Quaternion.Euler(
                    -2.4f * moveAmount + gesture * 0.7f,
                    torsoTwist + talk * 4.2f,
                    step * 3.8f * moveAmount);
            }

            if (head != null)
            {
                head.localPosition = new Vector3(0f, 1.76f + bob * 0.64f, -0.01f);
                head.localRotation = Quaternion.Euler(
                    -gesture * 1.0f + Mathf.Abs(step) * 0.55f * moveAmount,
                    -torsoTwist * 0.42f + talk * 8.5f,
                    -step * 1.4f * moveAmount);
            }

            float shoulderY = 1.46f + bob * 0.86f;
            Quaternion leftArmRot = Quaternion.Euler(stride * 0.92f, torsoTwist * 0.16f, -9f - talk * 16f);
            Quaternion rightArmRot = Quaternion.Euler(-stride * 0.92f, -torsoTwist * 0.16f, 9f + talk * 19f);
            SetSegmentFromAnchor(leftUpperArm, new Vector3(-0.30f, shoulderY, 0f), leftArmRot, 0.48f);
            SetSegmentFromAnchor(rightUpperArm, new Vector3(0.30f, shoulderY, 0f), rightArmRot, 0.48f);

            Vector3 leftElbow = new Vector3(-0.30f, shoulderY, 0f) + leftArmRot * Vector3.down * 0.48f;
            Vector3 rightElbow = new Vector3(0.30f, shoulderY, 0f) + rightArmRot * Vector3.down * 0.48f;
            Quaternion leftForearmRot = leftArmRot * Quaternion.Euler(
                14f + footLiftR * 16f + Mathf.Max(0f, talk) * 16f, 0f, 0f);
            Quaternion rightForearmRot = rightArmRot * Quaternion.Euler(
                14f + footLiftL * 16f + Mathf.Max(0f, -talk) * 16f, 0f, 0f);
            SetSegmentFromAnchor(leftForearm, leftElbow, leftForearmRot, 0.42f);
            SetSegmentFromAnchor(rightForearm, rightElbow, rightForearmRot, 0.42f);

            float hipY = 0.79f + bob * 0.88f;
            Quaternion leftThighRot = Quaternion.Euler(-stride, 0f, -2.0f + step * 2.8f * moveAmount);
            Quaternion rightThighRot = Quaternion.Euler(stride, 0f, 2.0f - step * 2.8f * moveAmount);
            SetSegmentFromAnchor(leftThigh, new Vector3(-0.14f + hipSway, hipY, 0f), leftThighRot, 0.54f);
            SetSegmentFromAnchor(rightThigh, new Vector3(0.14f + hipSway, hipY, 0f), rightThighRot, 0.54f);

            Vector3 leftKnee = new Vector3(-0.14f + hipSway, hipY, 0f) + leftThighRot * Vector3.down * 0.54f;
            Vector3 rightKnee = new Vector3(0.14f + hipSway, hipY, 0f) + rightThighRot * Vector3.down * 0.54f;
            Quaternion leftCalfRot = leftThighRot * Quaternion.Euler(footLiftL * 46f, 0f, 0f);
            Quaternion rightCalfRot = rightThighRot * Quaternion.Euler(footLiftR * 46f, 0f, 0f);
            SetSegmentFromAnchor(leftCalf, leftKnee, leftCalfRot, 0.46f);
            SetSegmentFromAnchor(rightCalf, rightKnee, rightCalfRot, 0.46f);

            if (leftCalf != null)
                leftCalf.localPosition += new Vector3(0f, footLiftL * 0.052f, -step * 0.045f * moveAmount);
            if (rightCalf != null)
                rightCalf.localPosition += new Vector3(0f, footLiftR * 0.052f, step * 0.045f * moveAmount);
        }

        private static void SetSegmentFromAnchor(
            Transform segment,
            Vector3 anchor,
            Quaternion rotation,
            float length)
        {
            if (segment == null) return;
            segment.localRotation = rotation;
            segment.localPosition = anchor + rotation * Vector3.down * (length * 0.5f);
        }

        private void BuildRig(MaterialLibrary materials, Color jacketColor, Color trouserColor, bool primary)
        {
            float skinShift = Mathf.Repeat(phase * 0.17f, 1f);
            Color skinColor = Color.Lerp(
                new Color(0.44f, 0.27f, 0.19f),
                new Color(0.83f, 0.63f, 0.48f),
                0.38f + skinShift * 0.50f);
            Material skin = materials.Solid(skinColor, false, "_SkinV5");
            Color readableJacket;
            if (primary)
            {
                readableJacket = operationStage == 0
                    ? new Color(0.94f, 0.075f, 0.045f)
                    : operationStage == 1
                        ? new Color(0.045f, 0.72f, 0.26f)
                        : new Color(0.96f, 0.64f, 0.12f);
            }
            else
            {
                float grey = jacketColor.grayscale;
                readableJacket = Color.Lerp(jacketColor, new Color(grey, grey, grey), 0.48f);
                readableJacket *= 0.78f;
            }
            Color readableTrousers = Color.Lerp(trouserColor, new Color(0.045f, 0.055f, 0.070f), 0.46f);
            Material jacket = primary
                ? materials.Solid(readableJacket, false, "_PrimaryJacketV53")
                : materials.Get(MaterialLibrary.Surface.Planks, readableJacket, 0f, 0.22f, "_ClothV53");
            Material trousers = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel, readableTrousers, 0f, 0.22f, "_TrousersV53");
            Material shirt = materials.Solid(primary ? new Color(1.00f, 0.96f, 0.84f) : new Color(0.42f, 0.49f, 0.53f), false, "_ShirtV53");
            Material leather = materials.Solid(new Color(0.045f, 0.040f, 0.035f), false, "_LeatherV5");
            Material hairMaterial = materials.Solid(
                Color.Lerp(new Color(0.04f, 0.03f, 0.025f), new Color(0.26f, 0.14f, 0.07f), skinShift),
                false,
                "_HairV5");
            Material eye = materials.Solid(new Color(0.015f, 0.020f, 0.018f), false, "_EyesV5");
            Material accent = materials.Solid(
                primary ? Color.Lerp(readableJacket, Color.white, 0.24f) : new Color(0.16f, 0.22f, 0.26f),
                primary,
                "_AccentV53");

            Rigidbody pelvisBody = CreateBone(
                "Pelvis", ProceduralHumanMesh.Pelvis("Pelvis Mesh", 0.34f, 0.48f, 0.31f),
                new Vector3(0f, 0.90f, 0f), Quaternion.identity, trousers,
                HumanBodyZone.Pelvis, ColliderKind.Capsule, new Vector3(0.44f, 0.34f, 0.28f), 12f);
            Rigidbody chestBody = CreateBone(
                "Chest", ProceduralHumanMesh.Torso("Torso Mesh", 0.58f, 0.64f, 0.48f, 0.37f),
                new Vector3(0f, 1.29f, 0f), Quaternion.identity, jacket,
                HumanBodyZone.Torso, ColliderKind.Capsule, new Vector3(0.58f, 0.58f, 0.34f), 22f);
            Rigidbody headBody = CreateBone(
                "Head", ProceduralHumanMesh.Head("Head Mesh", new Vector3(0.36f, 0.44f, 0.34f)),
                new Vector3(0f, 1.76f, -0.01f), Quaternion.identity, skin,
                HumanBodyZone.Head, ColliderKind.Sphere, new Vector3(0.36f, 0.42f, 0.34f), 5f);

            Rigidbody upperArmL = CreateBone(
                "Upper Arm L", ProceduralHumanMesh.Limb("Upper Arm L Mesh", 0.48f, 0.085f, 0.078f),
                new Vector3(-0.36f, 1.30f, 0f), Quaternion.Euler(0f, 0f, -8f), jacket,
                HumanBodyZone.LeftArm, ColliderKind.Capsule, new Vector3(0.17f, 0.48f, 0.15f), 3f);
            Rigidbody upperArmR = CreateBone(
                "Upper Arm R", ProceduralHumanMesh.Limb("Upper Arm R Mesh", 0.48f, 0.085f, 0.078f),
                new Vector3(0.36f, 1.30f, 0f), Quaternion.Euler(0f, 0f, 8f), jacket,
                HumanBodyZone.RightArm, ColliderKind.Capsule, new Vector3(0.17f, 0.48f, 0.15f), 3f);
            Rigidbody forearmL = CreateBone(
                "Forearm L", ProceduralHumanMesh.Limb("Forearm L Mesh", 0.42f, 0.072f, 0.060f),
                new Vector3(-0.40f, 0.91f, -0.01f), Quaternion.Euler(0f, 0f, -4f), skin,
                HumanBodyZone.LeftArm, ColliderKind.Capsule, new Vector3(0.15f, 0.42f, 0.14f), 2f);
            Rigidbody forearmR = CreateBone(
                "Forearm R", ProceduralHumanMesh.Limb("Forearm R Mesh", 0.42f, 0.072f, 0.060f),
                new Vector3(0.40f, 0.91f, -0.01f), Quaternion.Euler(0f, 0f, 4f), skin,
                HumanBodyZone.RightArm, ColliderKind.Capsule, new Vector3(0.15f, 0.42f, 0.14f), 2f);

            Rigidbody thighL = CreateBone(
                "Thigh L", ProceduralHumanMesh.Limb("Thigh L Mesh", 0.54f, 0.115f, 0.092f, 0.82f),
                new Vector3(-0.14f, 0.61f, 0f), Quaternion.identity, trousers,
                HumanBodyZone.LeftLeg, ColliderKind.Capsule, new Vector3(0.23f, 0.54f, 0.19f), 8f);
            Rigidbody thighR = CreateBone(
                "Thigh R", ProceduralHumanMesh.Limb("Thigh R Mesh", 0.54f, 0.115f, 0.092f, 0.82f),
                new Vector3(0.14f, 0.61f, 0f), Quaternion.identity, trousers,
                HumanBodyZone.RightLeg, ColliderKind.Capsule, new Vector3(0.23f, 0.54f, 0.19f), 8f);
            Rigidbody calfL = CreateBone(
                "Calf L", ProceduralHumanMesh.Limb("Calf L Mesh", 0.46f, 0.090f, 0.068f, 0.78f),
                new Vector3(-0.14f, 0.24f, 0f), Quaternion.identity, trousers,
                HumanBodyZone.LeftLeg, ColliderKind.Capsule, new Vector3(0.18f, 0.46f, 0.15f), 5f);
            Rigidbody calfR = CreateBone(
                "Calf R", ProceduralHumanMesh.Limb("Calf R Mesh", 0.46f, 0.090f, 0.068f, 0.78f),
                new Vector3(0.14f, 0.24f, 0f), Quaternion.identity, trousers,
                HumanBodyZone.RightLeg, ColliderKind.Capsule, new Vector3(0.18f, 0.46f, 0.15f), 5f);

            ConnectAt(chestBody, pelvisBody, new Vector3(0f, 1.06f, 0f), new Vector3(1f, 0f, 0f), 18f, 20f);
            ConnectAt(headBody, chestBody, new Vector3(0f, 1.55f, 0f), new Vector3(1f, 0f, 0f), 24f, 18f);
            ConnectAt(upperArmL, chestBody, new Vector3(-0.30f, 1.46f, 0f), Vector3.forward, 46f, 34f);
            ConnectAt(upperArmR, chestBody, new Vector3(0.30f, 1.46f, 0f), Vector3.forward, 46f, 34f);
            ConnectAt(forearmL, upperArmL, new Vector3(-0.39f, 1.08f, 0f), Vector3.forward, 18f, 14f);
            ConnectAt(forearmR, upperArmR, new Vector3(0.39f, 1.08f, 0f), Vector3.forward, 18f, 14f);
            ConnectAt(thighL, pelvisBody, new Vector3(-0.14f, 0.79f, 0f), Vector3.forward, 30f, 24f);
            ConnectAt(thighR, pelvisBody, new Vector3(0.14f, 0.79f, 0f), Vector3.forward, 30f, 24f);
            ConnectAt(calfL, thighL, new Vector3(-0.14f, 0.38f, 0f), Vector3.forward, 12f, 8f);
            ConnectAt(calfR, thighR, new Vector3(0.14f, 0.38f, 0f), Vector3.forward, 12f, 8f);

            pelvis = pelvisBody.transform;
            chest = chestBody.transform;
            head = headBody.transform;
            leftUpperArm = upperArmL.transform;
            rightUpperArm = upperArmR.transform;
            leftForearm = forearmL.transform;
            rightForearm = forearmR.transform;
            leftThigh = thighL.transform;
            rightThigh = thighR.transform;
            leftCalf = calfL.transform;
            rightCalf = calfR.transform;

            AttachMesh(head, "Hair", ProceduralHumanMesh.HairCap("Hair Mesh", new Vector3(0.38f, 0.23f, 0.35f)),
                new Vector3(0f, 0.125f, 0.005f), Quaternion.identity, hairMaterial);
            AttachMesh(head, "Nose", ProceduralHumanMesh.Hand("Nose Mesh", new Vector3(0.055f, 0.075f, 0.070f)),
                new Vector3(0f, -0.035f, -0.175f), Quaternion.identity, skin);
            AttachMesh(head, "Eye L", ProceduralHumanMesh.Hand("Eye L Mesh", Vector3.one * 0.034f),
                new Vector3(-0.062f, 0.035f, -0.164f), Quaternion.identity, eye);
            AttachMesh(head, "Eye R", ProceduralHumanMesh.Hand("Eye R Mesh", Vector3.one * 0.034f),
                new Vector3(0.062f, 0.035f, -0.164f), Quaternion.identity, eye);

            AttachMesh(forearmL.transform, "Hand L", ProceduralHumanMesh.Hand("Hand L Mesh", new Vector3(0.15f, 0.19f, 0.12f)),
                new Vector3(0f, -0.245f, 0f), Quaternion.identity, skin);
            AttachMesh(forearmR.transform, "Hand R", ProceduralHumanMesh.Hand("Hand R Mesh", new Vector3(0.15f, 0.19f, 0.12f)),
                new Vector3(0f, -0.245f, 0f), Quaternion.identity, skin);
            AttachMesh(calfL.transform, "Shoe L", ProceduralHumanMesh.Shoe("Shoe L Mesh", new Vector3(0.22f, 0.14f, 0.34f)),
                new Vector3(0f, -0.245f, -0.055f), Quaternion.identity, leather);
            AttachMesh(calfR.transform, "Shoe R", ProceduralHumanMesh.Shoe("Shoe R Mesh", new Vector3(0.22f, 0.14f, 0.34f)),
                new Vector3(0f, -0.245f, -0.055f), Quaternion.identity, leather);

            AttachMesh(chest, "Shirt Front", ProceduralHumanMesh.Torso("Shirt Front Mesh", 0.38f, 0.40f, 0.34f, 0.18f),
                new Vector3(0f, 0.015f, -0.105f), Quaternion.identity, shirt);
            AttachMesh(chest, "Wardrobe Accent", ProceduralHumanMesh.Limb(
                    "Accent Mesh", primary ? 0.34f : 0.24f, primary ? 0.052f : 0.024f, 0.016f, 0.60f),
                new Vector3(0f, 0.0f, -0.212f), Quaternion.identity, accent);

            if (operationStage == 2 && IsPrimary)
            {
                AttachMesh(pelvis, "Long Coat", ProceduralHumanMesh.Torso("Coat Tail Mesh", 0.62f, 0.54f, 0.46f, 0.30f),
                    new Vector3(0f, -0.22f, 0.05f), Quaternion.identity, jacket);
            }
        }

        private enum ColliderKind
        {
            Capsule,
            Sphere
        }

        private Rigidbody CreateBone(
            string name,
            Mesh mesh,
            Vector3 localPosition,
            Quaternion localRotation,
            Material material,
            HumanBodyZone zone,
            ColliderKind colliderKind,
            Vector3 colliderSize,
            float mass)
        {
            GameObject bone = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            bone.transform.SetParent(transform, false);
            bone.transform.localPosition = localPosition;
            bone.transform.localRotation = localRotation;

            MeshFilter filter = bone.GetComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = bone.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            if (colliderKind == ColliderKind.Sphere)
            {
                SphereCollider collider = bone.AddComponent<SphereCollider>();
                collider.radius = Mathf.Max(colliderSize.x, Mathf.Max(colliderSize.y, colliderSize.z)) * 0.46f;
            }
            else
            {
                CapsuleCollider collider = bone.AddComponent<CapsuleCollider>();
                collider.direction = 1;
                collider.radius = Mathf.Max(colliderSize.x, colliderSize.z) * 0.48f;
                collider.height = Mathf.Max(colliderSize.y, collider.radius * 2.05f);
            }

            HumanHitPart hitPart = bone.AddComponent<HumanHitPart>();
            hitPart.Initialize(this, zone);

            Rigidbody body = bone.AddComponent<Rigidbody>();
            body.mass = mass;
            body.isKinematic = true;
            body.useGravity = true;
            body.drag = 0.04f;
            body.angularDrag = 0.08f;
            bodies.Add(body);
            return body;
        }

        private void AttachMesh(
            Transform bone,
            string name,
            Mesh mesh,
            Vector3 localPosition,
            Quaternion localRotation,
            Material material)
        {
            GameObject decoration = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            decoration.transform.SetParent(bone, false);
            decoration.transform.localPosition = localPosition;
            decoration.transform.localRotation = localRotation;
            decoration.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = decoration.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private void ConnectAt(
            Rigidbody body,
            Rigidbody connected,
            Vector3 rootLocalAnchor,
            Vector3 axis,
            float swing,
            float twist)
        {
            Vector3 worldAnchor = transform.TransformPoint(rootLocalAnchor);
            CharacterJoint joint = body.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = connected;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = body.transform.InverseTransformPoint(worldAnchor);
            joint.connectedAnchor = connected.transform.InverseTransformPoint(worldAnchor);
            joint.axis = axis;
            joint.enableProjection = true;
            joint.enablePreprocessing = true;

            SoftJointLimit low = joint.lowTwistLimit;
            low.limit = -twist;
            joint.lowTwistLimit = low;
            SoftJointLimit high = joint.highTwistLimit;
            high.limit = twist;
            joint.highTwistLimit = high;
            SoftJointLimit swingOne = joint.swing1Limit;
            swingOne.limit = swing;
            joint.swing1Limit = swingOne;
            SoftJointLimit swingTwo = joint.swing2Limit;
            swingTwo.limit = swing * 0.72f;
            joint.swing2Limit = swingTwo;
        }

        private static float Ellipse(Vector2 point, Vector2 centre, Vector2 radii)
        {
            float x = (point.x - centre.x) / Mathf.Max(0.01f, radii.x);
            float y = (point.y - centre.y) / Mathf.Max(0.01f, radii.y);
            return Mathf.Sqrt(x * x + y * y);
        }
    }
}
