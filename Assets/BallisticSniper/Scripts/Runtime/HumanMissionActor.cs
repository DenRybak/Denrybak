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

        public void Tick(float clock)
        {
            if (ragdolled) return;

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

            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            AnimatePose(clock, gesture, travel);
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

        private void AnimatePose(float clock, float gesture, float travel)
        {
            float walkStrength = motion == HumanMotionStyle.Static || motion == HumanMotionStyle.Guard ? 0.18f : 1f;
            float stride = Mathf.Sin(clock * 4.15f + phase) * 24f * walkStrength * Mathf.Clamp01(Mathf.Abs(travel) + 0.18f);
            float talk = motion == HumanMotionStyle.Conversation || motion == HumanMotionStyle.CrossingSpeaker
                ? gesture
                : gesture * 0.12f;

            if (chest != null)
            {
                chest.localRotation = Quaternion.Euler(gesture * 1.2f, talk * 2.2f, -travel * 1.4f);
                Vector3 p = chest.localPosition;
                p.y = 1.29f + Mathf.Sin(clock * 1.65f + phase) * 0.008f;
                chest.localPosition = p;
            }

            if (head != null)
                head.localRotation = Quaternion.Euler(-gesture * 1.6f, talk * 7f + travel * 2f, 0f);

            if (leftUpperArm != null)
                leftUpperArm.localRotation = Quaternion.Euler(stride * 0.72f, 0f, -8f - talk * 18f);
            if (rightUpperArm != null)
                rightUpperArm.localRotation = Quaternion.Euler(-stride * 0.72f, 0f, 8f + talk * 22f);
            if (leftForearm != null)
                leftForearm.localRotation = Quaternion.Euler(Mathf.Max(0f, -stride) * 0.28f, 0f, -4f - Mathf.Max(0f, talk) * 24f);
            if (rightForearm != null)
                rightForearm.localRotation = Quaternion.Euler(Mathf.Max(0f, stride) * 0.28f, 0f, 4f + Mathf.Max(0f, -talk) * 24f);
            if (leftThigh != null)
                leftThigh.localRotation = Quaternion.Euler(-stride, 0f, -1.5f);
            if (rightThigh != null)
                rightThigh.localRotation = Quaternion.Euler(stride, 0f, 1.5f);
            if (leftCalf != null)
                leftCalf.localRotation = Quaternion.Euler(Mathf.Max(0f, stride) * 0.52f, 0f, 0f);
            if (rightCalf != null)
                rightCalf.localRotation = Quaternion.Euler(Mathf.Max(0f, -stride) * 0.52f, 0f, 0f);
        }

        private void BuildRig(MaterialLibrary materials, Color jacketColor, Color trouserColor, bool primary)
        {
            float skinShift = Mathf.Repeat(phase * 0.17f, 1f);
            Color skinColor = Color.Lerp(
                new Color(0.44f, 0.27f, 0.19f),
                new Color(0.83f, 0.63f, 0.48f),
                0.38f + skinShift * 0.50f);
            Material skin = materials.Solid(skinColor, false, "_SkinV5");
            Material jacket = materials.Get(MaterialLibrary.Surface.Planks, jacketColor, 0f, 0.42f, "_ClothV5");
            Material trousers = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel, trouserColor, 0f, 0.36f, "_TrousersV5");
            Material shirt = materials.Solid(primary ? new Color(0.88f, 0.88f, 0.80f) : new Color(0.52f, 0.64f, 0.68f), false, "_ShirtV5");
            Material leather = materials.Solid(new Color(0.045f, 0.040f, 0.035f), false, "_LeatherV5");
            Material hairMaterial = materials.Solid(
                Color.Lerp(new Color(0.04f, 0.03f, 0.025f), new Color(0.26f, 0.14f, 0.07f), skinShift),
                false,
                "_HairV5");
            Material eye = materials.Solid(new Color(0.015f, 0.020f, 0.018f), false, "_EyesV5");
            Material accent = materials.Solid(primary ? new Color(0.72f, 0.12f, 0.08f) : new Color(0.10f, 0.30f, 0.48f), false, "_AccentV5");

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
            AttachMesh(chest, "Wardrobe Accent", ProceduralHumanMesh.Limb("Accent Mesh", 0.28f, 0.024f, 0.015f, 0.60f),
                new Vector3(0f, 0.0f, -0.205f), Quaternion.identity, accent);

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
