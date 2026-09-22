using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BallisticSniper
{
    /// <summary>
    /// Lightweight runtime mesh factory for mission characters. The goal is a
    /// readable, genuinely volumetric human silhouette without billboard art
    /// or primitive cubes. Meshes are generated once per body part and remain
    /// cheap enough for mobile hardware.
    /// </summary>
    internal static class ProceduralHumanMesh
    {
        public static Mesh Torso(string name, float height, float shoulderWidth, float waistWidth, float depth)
        {
            float half = height * 0.5f;
            return Lathe(
                name,
                new[] { -half, -half * 0.58f, half * 0.18f, half * 0.72f, half },
                new[] { waistWidth * 0.46f, waistWidth * 0.52f, shoulderWidth * 0.50f, shoulderWidth * 0.54f, shoulderWidth * 0.42f },
                new[] { depth * 0.43f, depth * 0.50f, depth * 0.54f, depth * 0.50f, depth * 0.40f },
                12);
        }

        public static Mesh Pelvis(string name, float height, float width, float depth)
        {
            float half = height * 0.5f;
            return Lathe(
                name,
                new[] { -half, -half * 0.45f, half * 0.25f, half },
                new[] { width * 0.40f, width * 0.50f, width * 0.48f, width * 0.38f },
                new[] { depth * 0.40f, depth * 0.50f, depth * 0.48f, depth * 0.36f },
                12);
        }

        public static Mesh Limb(string name, float height, float radiusTop, float radiusBottom, float depthScale = 0.92f)
        {
            float half = height * 0.5f;
            float cap = Mathf.Min(height * 0.12f, Mathf.Min(radiusTop, radiusBottom) * 0.72f);
            return Lathe(
                name,
                new[] { -half, -half + cap, half - cap, half },
                new[] { radiusBottom * 0.22f, radiusBottom, radiusTop, radiusTop * 0.22f },
                new[] { radiusBottom * depthScale * 0.22f, radiusBottom * depthScale, radiusTop * depthScale, radiusTop * depthScale * 0.22f },
                10);
        }

        public static Mesh Head(string name, Vector3 size)
        {
            return Ellipsoid(name, size, 14, 10);
        }

        public static Mesh Hand(string name, Vector3 size)
        {
            return Ellipsoid(name, size, 10, 7);
        }

        public static Mesh Shoe(string name, Vector3 size)
        {
            Mesh mesh = Ellipsoid(name, size, 12, 7);
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                if (v.y < -size.y * 0.28f) v.y = -size.y * 0.28f;
                if (v.z < 0f) v.z *= 1.22f;
                vertices[i] = v;
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh HairCap(string name, Vector3 size)
        {
            Mesh mesh = Ellipsoid(name, size, 14, 8);
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                if (v.y < -size.y * 0.06f) v.y = -size.y * 0.06f;
                vertices[i] = v;
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh Lathe(
            string name,
            float[] yLevels,
            float[] radiusX,
            float[] radiusZ,
            int sides)
        {
            int rings = yLevels.Length;
            var vertices = new List<Vector3>(rings * sides + 2);
            var uv = new List<Vector2>(rings * sides + 2);
            var triangles = new List<int>((rings - 1) * sides * 6 + sides * 6);

            for (int ring = 0; ring < rings; ring++)
            {
                float v = rings <= 1 ? 0f : ring / (float)(rings - 1);
                for (int side = 0; side < sides; side++)
                {
                    float u = side / (float)sides;
                    float angle = u * Mathf.PI * 2f;
                    vertices.Add(new Vector3(
                        Mathf.Cos(angle) * radiusX[ring],
                        yLevels[ring],
                        Mathf.Sin(angle) * radiusZ[ring]));
                    uv.Add(new Vector2(u, v));
                }
            }

            for (int ring = 0; ring < rings - 1; ring++)
            {
                for (int side = 0; side < sides; side++)
                {
                    int next = (side + 1) % sides;
                    int a = ring * sides + side;
                    int b = ring * sides + next;
                    int c = (ring + 1) * sides + side;
                    int d = (ring + 1) * sides + next;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            int bottom = vertices.Count;
            vertices.Add(new Vector3(0f, yLevels[0], 0f));
            uv.Add(new Vector2(0.5f, 0f));
            int top = vertices.Count;
            vertices.Add(new Vector3(0f, yLevels[rings - 1], 0f));
            uv.Add(new Vector2(0.5f, 1f));

            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                triangles.Add(bottom);
                triangles.Add(next);
                triangles.Add(side);

                int topA = (rings - 1) * sides + side;
                int topB = (rings - 1) * sides + next;
                triangles.Add(top);
                triangles.Add(topA);
                triangles.Add(topB);
            }

            Mesh mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            UploadForRuntime(mesh);
            return mesh;
        }

        private static Mesh Ellipsoid(string name, Vector3 size, int longitude, int latitude)
        {
            var vertices = new List<Vector3>((longitude + 1) * (latitude + 1));
            var uv = new List<Vector2>((longitude + 1) * (latitude + 1));
            var triangles = new List<int>(longitude * latitude * 6);

            for (int y = 0; y <= latitude; y++)
            {
                float v = y / (float)latitude;
                float phi = Mathf.PI * v;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);
                for (int x = 0; x <= longitude; x++)
                {
                    float u = x / (float)longitude;
                    float theta = u * Mathf.PI * 2f;
                    vertices.Add(new Vector3(
                        Mathf.Cos(theta) * sinPhi * size.x * 0.5f,
                        cosPhi * size.y * 0.5f,
                        Mathf.Sin(theta) * sinPhi * size.z * 0.5f));
                    uv.Add(new Vector2(u, 1f - v));
                }
            }

            int stride = longitude + 1;
            for (int y = 0; y < latitude; y++)
            {
                for (int x = 0; x < longitude; x++)
                {
                    int a = y * stride + x;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            Mesh mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            UploadForRuntime(mesh);
            return mesh;
        }

        private static void UploadForRuntime(Mesh mesh)
        {
            mesh.MarkDynamic();
        }
    }
}
