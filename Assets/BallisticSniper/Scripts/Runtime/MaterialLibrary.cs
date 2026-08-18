using System.Collections.Generic;
using UnityEngine;

namespace BallisticSniper
{
    /// <summary>
    /// Builds lit materials from the supplied 4x4 high-resolution texture atlas.
    /// No store assets or internet connection are required at runtime.
    /// </summary>
    public sealed class MaterialLibrary
    {
        public enum Surface
        {
            Dirt = 0,
            Grass = 1,
            Sandstone = 2,
            Granite = 3,
            Planks = 4,
            SplinteredWood = 5,
            RustedRedSteel = 6,
            ScratchedBlackSteel = 7,
            CorrugatedSteel = 8,
            Clay = 9,
            WatermelonSkin = 10,
            WatermelonFlesh = 11,
            CrackedGlass = 12,
            PaperTarget = 13,
            Snow = 14,
            Concrete = 15
        }

        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly Texture2D atlas;
        private readonly Shader litShader;
        private readonly Shader transparentShader;
        private readonly Shader unlitShader;

        public MaterialLibrary()
        {
            atlas = Resources.Load<Texture2D>("BallisticSniper/Textures/range_material_atlas");
            litShader = Resources.Load<Shader>("BallisticSniper/Shaders/AtlasLit") ??
                        Shader.Find("BallisticSniper/AtlasLit") ?? Shader.Find("Standard");
            transparentShader = Shader.Find("Standard") ?? litShader;
            unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        }

        public Material Get(
            Surface surface,
            Color tint,
            float metallic = 0f,
            float smoothness = 0.25f,
            string suffix = "")
        {
            string key = surface + "|" + ColorUtility.ToHtmlStringRGBA(tint) + "|" +
                         metallic.ToString("0.00") + "|" + smoothness.ToString("0.00") + "|" + suffix;
            if (materials.TryGetValue(key, out Material cached))
            {
                return cached;
            }

            Material material = new Material(litShader) { name = "MAT_" + surface + suffix };
            ApplyAtlasCell(material, (int)surface);
            ApplyColor(material, tint);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            materials[key] = material;
            return material;
        }

        public Material Solid(Color color, bool emission = false, string suffix = "")
        {
            string key = "solid|" + ColorUtility.ToHtmlStringRGBA(color) + "|" + emission + "|" + suffix;
            if (materials.TryGetValue(key, out Material cached))
            {
                return cached;
            }

            Material material = new Material(emission ? litShader : unlitShader) { name = "MAT_Solid" + suffix };
            ApplyColor(material, color);
            if (emission && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 2.2f);
            }
            materials[key] = material;
            return material;
        }

        public Material TransparentGlass(Color tint)
        {
            const string key = "transparent_glass";
            if (materials.TryGetValue(key, out Material cached)) return cached;

            Material material = new Material(transparentShader) { name = "MAT_Glass" };
            ApplyAtlasCell(material, (int)Surface.CrackedGlass);
            ApplyColor(material, tint);
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 3f);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = 3000;
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.82f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.82f);
            materials[key] = material;
            return material;
        }

        private void ApplyAtlasCell(Material material, int cell)
        {
            if (atlas == null) return;
            int column = cell % 4;
            int rowFromTop = cell / 4;
            Vector2 scale = new Vector2(0.25f, 0.25f);
            Vector2 offset = new Vector2(column * 0.25f, (3 - rowFromTop) * 0.25f);

            if (material.HasProperty("_AtlasCell"))
            {
                material.SetVector("_AtlasCell", new Vector4(offset.x, offset.y, scale.x, scale.y));
                material.SetVector("_Tiling", new Vector4(1f, 1f, 0f, 0f));
            }

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", atlas);
                material.SetTextureScale("_BaseMap", scale);
                material.SetTextureOffset("_BaseMap", offset);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", atlas);
                material.SetTextureScale("_MainTex", scale);
                material.SetTextureOffset("_MainTex", offset);
            }
        }

        private static void ApplyColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }
    }
}
