using UnityEngine;

namespace BallisticSniper
{
    [RequireComponent(typeof(Camera))]
    public sealed class SceneToneMapper : MonoBehaviour
    {
        private Material material;

        private void OnEnable()
        {
            Shader shader = Resources.Load<Shader>("BallisticSniper/Shaders/SceneGrade") ??
                            Shader.Find("Hidden/BallisticSniper/SceneGrade");
            if (shader != null && shader.isSupported)
            {
                material = new Material(shader)
                {
                    name = "Ballistic Scene Grade",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (material == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            material.SetFloat("_Exposure", 1.14f);
            material.SetFloat("_Contrast", 1.13f);
            material.SetFloat("_Saturation", 1.08f);
            material.SetFloat("_Vignette", 0.035f);
            material.SetFloat("_Sharpness", 0.42f);
            Graphics.Blit(source, destination, material);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
