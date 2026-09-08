using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace BallisticSniper
{
    /// <summary>
    /// Explicit references for components created indirectly by Unity's primitive factory.
    /// This complements link.xml and stripEngineCode=false for IL2CPP Player builds.
    /// </summary>
    [Preserve]
    internal static class RuntimeTypeRetention
    {
        private static readonly Type[] RequiredTypes =
        {
            typeof(BoxCollider),
            typeof(SphereCollider),
            typeof(CapsuleCollider),
            typeof(MeshCollider),
            typeof(Rigidbody),
            typeof(CharacterJoint),
            typeof(MeshFilter),
            typeof(MeshRenderer),
            typeof(LineRenderer),
            typeof(TrailRenderer),
            typeof(ParticleSystem),
            typeof(ParticleSystemRenderer),
            typeof(AudioSource),
            typeof(AudioListener),
            typeof(Camera),
            typeof(Light)
        };

        [Preserve]
        internal static void EnsureLinked()
        {
            GC.KeepAlive(RequiredTypes);
        }
    }
}
