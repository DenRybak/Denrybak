using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BallisticSniper
{
    public enum HumanMotionStyle
    {
        Static,
        Conversation,
        WindowPatrol,
        CrossingSpeaker,
        RooftopPatrol,
        Guard
    }

    /// <summary>
    /// A runtime-built, fully physical mission character. Each major body part
    /// owns a rigidbody and is connected with constrained CharacterJoints.
    /// During the observation phase the bodies remain kinematic; an impact
    /// releases the whole rig and applies the selected rifle's impulse exactly
    /// at the struck body part.
    /// </summary>
    public sealed class HumanMissionActor : MonoBehaviour
    {
        private readonly List<Rigidbody> bodies = new List<Rigidbody>();
        private Vector3 basePosition;
        private HumanMotionStyle motion;
        private float phase;
        private int operationStage;
        private bool ragdolled;
        private Transform leftUpperArm;
        private Transform rightUpperArm;
        private Transform leftForearm;
        private Transform rightForearm;

        public bool IsPrimary { get; private set; }
        public bool IsRagdolled => ragdolled;
        public Vector3 AimCentre => transform.position + Vector3.up * 1.34f;
        public float Depth => transform.position.z;
        public IReadOnlyList<Rigidbody> Bodies => bodies;

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
            GameObject root = new GameObject((primary ? "MISSION TARGET — " : "BYSTANDER — ") + characterName);
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            HumanMissionActor actor = root.AddComponent<HumanMissionActor>();
            actor.IsPrimary = primary;
            actor.basePosition = position;
            actor.motion = motionStyle;
            actor.operationStage = stage;
            actor.phase = phaseOffset;
            actor.BuildRig(materials, jacketColor, trouserColor, primary);
            actor.Tick(0f);
            return actor;
        }

        public void Tick(float clock)
        {
            if (ragdolled) return;

            Vector3 position = basePosition;
            float gesture = Mathf.Sin(clock * 1.72f + phase);
            float slower = Mathf.Sin(clock * 0.58f + phase);
            float yaw = 0f;

            switch (motion)
            {
                case HumanMotionStyle.Conversation:
                    position.x += slower * (IsPrimary ? 0.22f : 0.34f);
                    yaw = gesture * 9f + (IsPrimary ? -8f : 8f);
                    break;
                case HumanMotionStyle.WindowPatrol:
                    position.x += Mathf.Sin(clock * 0.74f + phase) * 0.72f;
                    yaw = slower * 18f;
                    break;
                case HumanMotionStyle.CrossingSpeaker:
                    position.x += Mathf.Sin(clock * 0.88f + phase) * 0.92f;
                    position.z -= 0.20f + Mathf.Cos(clock * 0.63f + phase) * 0.12f;
                    yaw = 12f + gesture * 15f;
                    break;
                case HumanMotionStyle.RooftopPatrol:
                    position.x += Mathf.Sin(clock * 0.46f + phase) * 1.18f;
                    yaw = slower * 20f;
                    break;
                case HumanMotionStyle.Guard:
                    position.x += Mathf.Sin(clock * 0.31f + phase) * 0.34f;
                    yaw = Mathf.Sin(clock * 0.27f + phase) * 24f;
                    break;
            }

            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            float talk = motion == HumanMotionStyle.Conversation || motion == HumanMotionStyle.CrossingSpeaker
                ? gesture
                : gesture * 0.18f;
            if (leftUpperArm != null)
                leftUpperArm.localRotation = Quaternion.Euler(0f, 0f, -10f - talk * 24f);
            if (rightUpperArm != null)
                rightUpperArm.localRotation = Quaternion.Euler(0f, 0f, 10f + talk * 29f);
            if (leftForearm != null)
                leftForearm.localRotation = Quaternion.Euler(0f, 0f, -6f - Mathf.Max(0f, talk) * 35f);
            if (rightForearm != null)
                rightForearm.localRotation = Quaternion.Euler(0f, 0f, 6f + Mathf.Max(0f, -talk) * 35f);
        }

        public bool ContainsImpact(Vector3 impactPoint)
        {
            Vector2 point = new Vector2(impactPoint.x - transform.position.x, impactPoint.y - transform.position.y);
            bool head = Ellipse(point, new Vector2(0f, 1.70f), new Vector2(0.22f, 0.25f)) <= 1f;
            bool torso = Ellipse(point, new Vector2(0f, 1.23f), new Vector2(0.34f, 0.48f)) <= 1f;
            bool pelvis = Ellipse(point, new Vector2(0f, 0.84f), new Vector2(0.30f, 0.25f)) <= 1f;
            bool legs = (Mathf.Abs(point.x) <= 0.27f && point.y >= 0.08f && point.y <= 0.78f);
            bool arms = (Mathf.Abs(point.x) <= 0.52f && point.y >= 0.79f && point.y <= 1.55f);
            return head || torso || pelvis || legs || arms;
        }

        public float NormalizedDistance(Vector3 impactPoint)
        {
            Vector2 point = new Vector2(impactPoint.x - transform.position.x, impactPoint.y - transform.position.y);
            float head = Ellipse(point, new Vector2(0f, 1.70f), new Vector2(0.22f, 0.25f));
            float torso = Ellipse(point, new Vector2(0f, 1.23f), new Vector2(0.34f, 0.48f));
            float legs = Ellipse(point, new Vector2(0f, 0.47f), new Vector2(0.30f, 0.44f));
            return Mathf.Min(head, torso, legs);
        }

        public Vector2 ErrorFromCentre(Vector3 impactPoint)
        {
            return new Vector2(impactPoint.x - AimCentre.x, impactPoint.y - AimCentre.y);
        }

        public void ActivateRagdoll(Vector3 impactPoint, Vector3 shotDirection, float impulse)
        {
            if (ragdolled) return;
            ragdolled = true;
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
                    direction * impulse + Vector3.up * impulse * 0.18f,
                    impactPoint,
                    ForceMode.Impulse);
                struck.AddTorque(new Vector3(impulse * 0.12f, impulse * 0.08f, -impulse * 0.16f), ForceMode.Impulse);
            }

            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] == struck) continue;
                bodies[i].AddForce(direction * impulse * 0.055f, ForceMode.Impulse);
            }
        }

        private void BuildRig(MaterialLibrary materials, Color jacketColor, Color trouserColor, bool primary)
        {
            Material skin = materials.Solid(new Color(0.72f, 0.49f, 0.35f), false, "_Skin");
            Material jacket = materials.Get(MaterialLibrary.Surface.Planks, jacketColor, 0f, 0.34f, "_TailoredCloth");
            Material trousers = materials.Get(MaterialLibrary.Surface.ScratchedBlackSteel, trouserColor, 0f, 0.22f, "_Trousers");
            Material shirt = materials.Solid(primary ? new Color(0.91f, 0.90f, 0.82f) : new Color(0.68f, 0.77f, 0.80f), false, "_Shirt");
            Material leather = materials.Solid(new Color(0.055f, 0.048f, 0.043f), false, "_Leather");
            Material hair = materials.Solid(new Color(0.065f, 0.045f, 0.035f), false, "_Hair");
            Material eye = materials.Solid(new Color(0.018f, 0.023f, 0.021f), false, "_Eyes");
            Material accent = materials.Solid(primary ? new Color(0.84f, 0.16f, 0.10f) : new Color(0.12f, 0.35f, 0.53f), false, "_WardrobeAccent");

            Rigidbody pelvis = CreateBone("Pelvis", PrimitiveType.Capsule, new Vector3(0f, 0.86f, 0f), new Vector3(0.23f, 0.22f, 0.18f), trousers, Quaternion.identity, 12f);
            Rigidbody chest = CreateBone("Chest", PrimitiveType.Capsule, new Vector3(0f, 1.25f, 0f), new Vector3(0.28f, 0.34f, 0.19f), jacket, Quaternion.identity, 22f);
            Rigidbody head = CreateBone("Head", PrimitiveType.Sphere, new Vector3(0f, 1.72f, -0.01f), new Vector3(0.19f, 0.22f, 0.18f), skin, Quaternion.identity, 5f);

            Rigidbody upperArmL = CreateBone("Upper Arm L", PrimitiveType.Capsule, new Vector3(-0.34f, 1.26f, 0f), new Vector3(0.085f, 0.27f, 0.085f), jacket, Quaternion.Euler(0f, 0f, -10f), 3f);
            Rigidbody upperArmR = CreateBone("Upper Arm R", PrimitiveType.Capsule, new Vector3(0.34f, 1.26f, 0f), new Vector3(0.085f, 0.27f, 0.085f), jacket, Quaternion.Euler(0f, 0f, 10f), 3f);
            Rigidbody forearmL = CreateBone("Forearm L", PrimitiveType.Capsule, new Vector3(-0.40f, 0.88f, -0.01f), new Vector3(0.075f, 0.23f, 0.075f), skin, Quaternion.Euler(0f, 0f, -6f), 2f);
            Rigidbody forearmR = CreateBone("Forearm R", PrimitiveType.Capsule, new Vector3(0.40f, 0.88f, -0.01f), new Vector3(0.075f, 0.23f, 0.075f), skin, Quaternion.Euler(0f, 0f, 6f), 2f);

            Rigidbody thighL = CreateBone("Thigh L", PrimitiveType.Capsule, new Vector3(-0.13f, 0.55f, 0f), new Vector3(0.115f, 0.27f, 0.115f), trousers, Quaternion.identity, 8f);
            Rigidbody thighR = CreateBone("Thigh R", PrimitiveType.Capsule, new Vector3(0.13f, 0.55f, 0f), new Vector3(0.115f, 0.27f, 0.115f), trousers, Quaternion.identity, 8f);
            Rigidbody calfL = CreateBone("Calf L", PrimitiveType.Capsule, new Vector3(-0.13f, 0.19f, 0f), new Vector3(0.095f, 0.23f, 0.095f), trousers, Quaternion.identity, 5f);
            Rigidbody calfR = CreateBone("Calf R", PrimitiveType.Capsule, new Vector3(0.13f, 0.19f, 0f), new Vector3(0.095f, 0.23f, 0.095f), trousers, Quaternion.identity, 5f);

            Connect(chest, pelvis, new Vector3(1f, 0f, 0f), 18f, 22f);
            Connect(head, chest, new Vector3(1f, 0f, 0f), 24f, 18f);
            Connect(upperArmL, chest, Vector3.forward, 48f, 35f);
            Connect(upperArmR, chest, Vector3.forward, 48f, 35f);
            Connect(forearmL, upperArmL, Vector3.forward, 18f, 14f);
            Connect(forearmR, upperArmR, Vector3.forward, 18f, 14f);
            Connect(thighL, pelvis, Vector3.forward, 30f, 24f);
            Connect(thighR, pelvis, Vector3.forward, 30f, 24f);
            Connect(calfL, thighL, Vector3.forward, 12f, 8f);
            Connect(calfR, thighR, Vector3.forward, 12f, 8f);

            leftUpperArm = upperArmL.transform;
            rightUpperArm = upperArmR.transform;
            leftForearm = forearmL.transform;
            rightForearm = forearmR.transform;

            // Close-up wardrobe and facial layers keep the impact replay readable.
            AttachDecoration(chest.transform, "Shirt Front", PrimitiveType.Cube, new Vector3(0f, 1.31f, -0.185f), new Vector3(0.19f, 0.30f, 0.018f), shirt, Quaternion.identity);
            AttachDecoration(chest.transform, "Left Lapel", PrimitiveType.Cube, new Vector3(-0.10f, 1.38f, -0.215f), new Vector3(0.07f, 0.22f, 0.018f), jacket, Quaternion.Euler(0f, 0f, -24f));
            AttachDecoration(chest.transform, "Right Lapel", PrimitiveType.Cube, new Vector3(0.10f, 1.38f, -0.215f), new Vector3(0.07f, 0.22f, 0.018f), jacket, Quaternion.Euler(0f, 0f, 24f));
            AttachDecoration(chest.transform, "Tie", PrimitiveType.Cube, new Vector3(0f, 1.28f, -0.238f), new Vector3(0.032f, 0.19f, 0.012f), accent, Quaternion.identity);
            AttachDecoration(pelvis.transform, "Belt", PrimitiveType.Cube, new Vector3(0f, 0.88f, -0.19f), new Vector3(0.25f, 0.035f, 0.022f), leather, Quaternion.identity);
            AttachDecoration(head.transform, "Hair", PrimitiveType.Sphere, new Vector3(0f, 1.86f, 0.005f), new Vector3(0.195f, 0.105f, 0.18f), hair, Quaternion.identity);
            AttachDecoration(head.transform, "Nose", PrimitiveType.Sphere, new Vector3(0f, 1.70f, -0.182f), new Vector3(0.035f, 0.045f, 0.035f), skin, Quaternion.identity);
            AttachDecoration(head.transform, "Eye L", PrimitiveType.Sphere, new Vector3(-0.065f, 1.76f, -0.176f), Vector3.one * 0.018f, eye, Quaternion.identity);
            AttachDecoration(head.transform, "Eye R", PrimitiveType.Sphere, new Vector3(0.065f, 1.76f, -0.176f), Vector3.one * 0.018f, eye, Quaternion.identity);
            AttachDecoration(forearmL.transform, "Hand L", PrimitiveType.Sphere, new Vector3(-0.42f, 0.62f, -0.01f), new Vector3(0.085f, 0.105f, 0.07f), skin, Quaternion.identity);
            AttachDecoration(forearmR.transform, "Hand R", PrimitiveType.Sphere, new Vector3(0.42f, 0.62f, -0.01f), new Vector3(0.085f, 0.105f, 0.07f), skin, Quaternion.identity);
            AttachDecoration(calfL.transform, "Boot L", PrimitiveType.Cube, new Vector3(-0.13f, 0.015f, -0.055f), new Vector3(0.13f, 0.08f, 0.22f), leather, Quaternion.identity);
            AttachDecoration(calfR.transform, "Boot R", PrimitiveType.Cube, new Vector3(0.13f, 0.015f, -0.055f), new Vector3(0.13f, 0.08f, 0.22f), leather, Quaternion.identity);

            if (operationStage == 2 && IsPrimary)
            {
                AttachDecoration(chest.transform, "Long Coat L", PrimitiveType.Cube, new Vector3(-0.13f, 0.88f, 0.10f), new Vector3(0.13f, 0.42f, 0.10f), jacket, Quaternion.Euler(6f, 0f, 2f));
                AttachDecoration(chest.transform, "Long Coat R", PrimitiveType.Cube, new Vector3(0.13f, 0.88f, 0.10f), new Vector3(0.13f, 0.42f, 0.10f), jacket, Quaternion.Euler(6f, 0f, -2f));
            }
        }

        private Rigidbody CreateBone(
            string name,
            PrimitiveType primitive,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            Quaternion rotation,
            float mass)
        {
            GameObject bone = RangeWorld.CreatePrimitive(
                primitive,
                name,
                transform,
                localPosition,
                localScale,
                material,
                rotation,
                true);
            Rigidbody body = bone.AddComponent<Rigidbody>();
            body.mass = mass;
            body.isKinematic = true;
            body.useGravity = true;
            body.drag = 0.04f;
            body.angularDrag = 0.08f;
            bodies.Add(body);
            return body;
        }

        private void AttachDecoration(
            Transform bone,
            string name,
            PrimitiveType primitive,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            Quaternion rotation)
        {
            GameObject decoration = RangeWorld.CreatePrimitive(
                primitive,
                name,
                transform,
                localPosition,
                localScale,
                material,
                rotation,
                false);
            decoration.transform.SetParent(bone, true);
            Renderer renderer = decoration.GetComponent<Renderer>();
            if (renderer != null) renderer.shadowCastingMode = ShadowCastingMode.On;
        }

        private static void Connect(Rigidbody body, Rigidbody connected, Vector3 axis, float swing, float twist)
        {
            CharacterJoint joint = body.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = connected;
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
