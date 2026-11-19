using System.Collections.Generic;
using UnityEngine;

namespace UnityLab.EditorTools
{
    /// <summary>
    /// Low-poly, flat-shaded meshes built from code. They replace the third-party models the
    /// original university projects used, so every asset in the repository is our own.
    /// Each mesh is built around the origin and then fitted to the bounds of the model it
    /// replaces, so existing scenes keep their scale and layout.
    /// </summary>
    public static class ProceduralMeshes
    {
        public enum Kind
        {
            Box,
            CarBody,
            Wheel,
            Pawn,
            House,
            Tree,
            Stump,
            Rock,
            Ring,
        }

        /// <summary>Guesses what a model represents from its name.</summary>
        public static Kind Classify(string modelName)
        {
            string n = modelName.ToLowerInvariant();
            if (n.Contains("wheel")) return Kind.Wheel;
            if (n.Contains("veh") || n.Contains("car")) return Kind.CarBody;
            if (n.Contains("bean") || n.Contains("character")) return Kind.Pawn;
            if (n.Contains("bld") || n.Contains("house")) return Kind.House;
            if (n.Contains("stump")) return Kind.Stump;
            if (n.Contains("tree")) return Kind.Tree;
            if (n.Contains("mountain") || n.Contains("rock")) return Kind.Rock;
            if (n.Contains("ring")) return Kind.Ring;
            return Kind.Box;
        }

        /// <summary>Number of materials (submeshes) a kind uses.</summary>
        public static int SubmeshCount(Kind kind) => kind == Kind.Tree ? 2 : 1;

        public static Mesh Build(Kind kind, Bounds target, string name)
        {
            var builder = new Builder();
            switch (kind)
            {
                case Kind.CarBody: CarBody(builder); break;
                case Kind.Wheel: builder.Cylinder(Vector3.zero, ThinAxis(target), 0.5f, 0.5f, 18); break;
                case Kind.Pawn: Pawn(builder); break;
                case Kind.House: House(builder); break;
                case Kind.Tree: Tree(builder); break;
                case Kind.Stump: builder.Cylinder(new Vector3(0, 0.25f, 0), Vector3.up, 0.5f, 0.5f, 10); break;
                case Kind.Rock: Rock(builder); break;
                case Kind.Ring: builder.Torus(1f, 0.08f, 32, 8, ThinAxis(target)); break;
                default: builder.Box(Vector3.zero, Vector3.one); break;
            }
            Mesh mesh = builder.ToMesh(name);
            FitTo(mesh, target, kind == Kind.Wheel || kind == Kind.Ring ? ThinAxis(target) : Vector3.zero);
            return mesh;
        }

        /// <summary>The axis along which the bounds are thinnest: a wheel's axle, a hoop's normal.</summary>
        public static Vector3 ThinAxis(Bounds bounds)
        {
            Vector3 size = bounds.size;
            if (size.x <= size.y && size.x <= size.z) return Vector3.right;
            return size.y <= size.z ? Vector3.up : Vector3.forward;
        }

        static void CarBody(Builder b)
        {
            // Proportions of a small hatchback, forward is +Z.
            b.Box(new Vector3(0, 0.45f, 0), new Vector3(1.8f, 0.55f, 4.0f));
            b.Wedge(new Vector3(0, 0.95f, -0.25f), new Vector3(1.6f, 0.5f, 2.2f), 0.75f);
            b.Box(new Vector3(0, 0.3f, 2.03f), new Vector3(1.7f, 0.2f, 0.1f));
            b.Box(new Vector3(0, 0.3f, -2.03f), new Vector3(1.7f, 0.2f, 0.1f));
        }

        static void Pawn(Builder b)
        {
            // Chunky toy soldier: body, head and a hint of shoulders.
            b.Cylinder(new Vector3(0, 0.55f, 0), Vector3.up, 0.32f, 1.1f, 12);
            b.Box(new Vector3(0, 0.95f, 0), new Vector3(0.8f, 0.25f, 0.45f));
            b.Sphere(new Vector3(0, 1.45f, 0), 0.26f, 10, 6);
        }

        static void House(Builder b)
        {
            b.Box(new Vector3(0, 1.0f, 0), new Vector3(4f, 2f, 3.5f));
            b.Prism(new Vector3(0, 2.6f, 0), new Vector3(4.4f, 1.2f, 3.9f));
            b.Box(new Vector3(1.2f, 2.9f, 0.6f), new Vector3(0.4f, 0.9f, 0.4f));
        }

        static void Tree(Builder b)
        {
            b.Submesh(0);
            b.Cylinder(new Vector3(0, 0.6f, 0), Vector3.up, 0.18f, 1.2f, 8);
            b.Submesh(1);
            b.Cone(new Vector3(0, 1.0f, 0), 1.0f, 1.6f, 10);
            b.Cone(new Vector3(0, 2.0f, 0), 0.75f, 1.3f, 10);
        }

        static void Rock(Builder b)
        {
            var random = new System.Random(7);
            b.Sphere(Vector3.zero, 1f, 9, 5, () => 0.8f + 0.35f * (float)random.NextDouble());
        }

        /// <summary>
        /// Scales and moves the mesh so its bounds match <paramref name="target"/>. Round shapes
        /// (wheels, hoops) pass their axis so the two axes across it share one scale and stay circular.
        /// </summary>
        static void FitTo(Mesh mesh, Bounds target, Vector3 roundAxis)
        {
            Bounds source = mesh.bounds;
            Vector3 scale = new Vector3(
                SafeRatio(target.size.x, source.size.x),
                SafeRatio(target.size.y, source.size.y),
                SafeRatio(target.size.z, source.size.z));
            if (roundAxis == Vector3.right)
                scale.y = scale.z = Mathf.Min(scale.y, scale.z);
            else if (roundAxis == Vector3.up)
                scale.x = scale.z = Mathf.Min(scale.x, scale.z);
            else if (roundAxis == Vector3.forward)
                scale.x = scale.y = Mathf.Min(scale.x, scale.y);

            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = Vector3.Scale(vertices[i] - source.center, scale) + target.center;
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }

        static float SafeRatio(float target, float source) => source > 1e-5f && target > 1e-5f ? target / source : 1f;

        /// <summary>Accumulates flat-shaded triangles (no shared vertices, so normals stay sharp).</summary>
        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<List<int>> submeshes = new List<List<int>> { new List<int>() };
            int current;

            public void Submesh(int index)
            {
                while (submeshes.Count <= index)
                    submeshes.Add(new List<int>());
                current = index;
            }

            void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                submeshes[current].Add(start);
                submeshes[current].Add(start + 1);
                submeshes[current].Add(start + 2);
            }

            /// <summary>
            /// Makes every triangle added since <paramref name="firstVertex"/> face away from the
            /// centre of its part. All parts are convex (or a tube, for the torus), so this gives
            /// correct front faces without having to get every winding right by hand.
            /// </summary>
            void Orient(int firstVertex, System.Func<Vector3, Vector3> centreOf)
            {
                for (int i = firstVertex; i + 2 < vertices.Count; i += 3)
                {
                    Vector3 a = vertices[i], b = vertices[i + 1], c = vertices[i + 2];
                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    Vector3 centroid = (a + b + c) / 3f;
                    if (Vector3.Dot(normal, centroid - centreOf(centroid)) < 0f)
                    {
                        vertices[i + 1] = c;
                        vertices[i + 2] = b;
                    }
                }
            }

            /// <summary>Quad from four corners in order around its edge.</summary>
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Triangle(a, b, c);
                Triangle(a, c, d);
            }

            public void Box(Vector3 center, Vector3 size) => Wedge(center, size, 1f);

            /// <summary>Box whose top face is shrunk by <paramref name="topScale"/> (a car cabin).</summary>
            public void Wedge(Vector3 center, Vector3 size, float topScale)
            {
                Vector3 h = size * 0.5f;
                Vector3 t = new Vector3(h.x * topScale, h.y, h.z * topScale);
                Vector3[] p =
                {
                    center + new Vector3(-h.x, -h.y, -h.z), center + new Vector3(h.x, -h.y, -h.z),
                    center + new Vector3(h.x, -h.y, h.z), center + new Vector3(-h.x, -h.y, h.z),
                    center + new Vector3(-t.x, t.y, -t.z), center + new Vector3(t.x, t.y, -t.z),
                    center + new Vector3(t.x, t.y, t.z), center + new Vector3(-t.x, t.y, t.z),
                };
                int first = vertices.Count;
                Quad(p[4], p[7], p[6], p[5]); // top
                Quad(p[0], p[1], p[2], p[3]); // bottom
                Quad(p[0], p[4], p[5], p[1]); // back (-Z)
                Quad(p[2], p[6], p[7], p[3]); // front (+Z)
                Quad(p[3], p[7], p[4], p[0]); // left
                Quad(p[1], p[5], p[6], p[2]); // right
                Orient(first, _ => center);
            }

            /// <summary>Triangular roof running along X.</summary>
            public void Prism(Vector3 center, Vector3 size)
            {
                Vector3 h = size * 0.5f;
                Vector3 a = center + new Vector3(-h.x, -h.y, -h.z), b = center + new Vector3(h.x, -h.y, -h.z);
                Vector3 c = center + new Vector3(h.x, -h.y, h.z), d = center + new Vector3(-h.x, -h.y, h.z);
                Vector3 r0 = center + new Vector3(-h.x, h.y, 0), r1 = center + new Vector3(h.x, h.y, 0);
                int first = vertices.Count;
                Quad(a, r0, r1, b);
                Quad(c, r1, r0, d);
                Triangle(d, r0, a);
                Triangle(b, r1, c);
                Quad(a, b, c, d);
                Orient(first, _ => center);
            }

            public void Cylinder(Vector3 center, Vector3 axis, float radius, float length, int segments)
            {
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
                Vector3 half = axis.normalized * (length * 0.5f);
                int first = vertices.Count;
                for (int i = 0; i < segments; i++)
                {
                    float a0 = Mathf.PI * 2f * i / segments;
                    float a1 = Mathf.PI * 2f * (i + 1) / segments;
                    Vector3 o0 = rotation * new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                    Vector3 o1 = rotation * new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                    Vector3 top = center + half, bottom = center - half;
                    Quad(bottom + o0, top + o0, top + o1, bottom + o1);
                    Triangle(top, top + o1, top + o0);
                    Triangle(bottom, bottom + o0, bottom + o1);
                }
                Orient(first, _ => center);
            }

            public void Cone(Vector3 baseCenter, float radius, float height, int segments)
            {
                Vector3 tip = baseCenter + Vector3.up * height;
                int first = vertices.Count;
                for (int i = 0; i < segments; i++)
                {
                    float a0 = Mathf.PI * 2f * i / segments;
                    float a1 = Mathf.PI * 2f * (i + 1) / segments;
                    Vector3 p0 = baseCenter + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                    Vector3 p1 = baseCenter + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                    Triangle(p0, tip, p1);
                    Triangle(baseCenter, p0, p1);
                }
                Vector3 inside = baseCenter + Vector3.up * (height * 0.25f);
                Orient(first, _ => inside);
            }

            public void Sphere(Vector3 center, float radius, int segments, int rings, System.Func<float> jitter = null)
            {
                var grid = new Vector3[rings + 1, segments];
                int first = vertices.Count;
                for (int r = 0; r <= rings; r++)
                {
                    float phi = Mathf.PI * r / rings;
                    for (int s = 0; s < segments; s++)
                    {
                        float theta = Mathf.PI * 2f * s / segments;
                        float k = (r == 0 || r == rings || jitter == null) ? 1f : jitter();
                        grid[r, s] = center + new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)) * radius * k;
                    }
                }
                for (int r = 0; r < rings; r++)
                {
                    for (int s = 0; s < segments; s++)
                    {
                        int n = (s + 1) % segments;
                        Vector3 a = grid[r, s], b = grid[r, n], c = grid[r + 1, n], d = grid[r + 1, s];
                        if (r > 0)
                            Triangle(a, b, d);
                        if (r < rings - 1)
                            Triangle(b, c, d);
                    }
                }
                Orient(first, _ => center);
            }

            /// <summary>Ring around <paramref name="axis"/> (the direction you look through it).</summary>
            public void Torus(float majorRadius, float minorRadius, int segments, int sides, Vector3 axis)
            {
                Quaternion rotation = Quaternion.FromToRotation(Vector3.right, axis);
                Vector3 Point(int i, int j)
                {
                    float u = Mathf.PI * 2f * i / segments;
                    float v = Mathf.PI * 2f * j / sides;
                    float d = majorRadius + minorRadius * Mathf.Cos(v);
                    return rotation * (new Vector3(0f, d * Mathf.Sin(u), d * Mathf.Cos(u)) + Vector3.right * (minorRadius * Mathf.Sin(v)));
                }
                int first = vertices.Count;
                for (int i = 0; i < segments; i++)
                    for (int j = 0; j < sides; j++)
                        Quad(Point(i, j), Point(i + 1, j), Point(i + 1, j + 1), Point(i, j + 1));
                // Each face points away from the centre line of the tube.
                Orient(first, p => Vector3.ProjectOnPlane(p, axis).normalized * majorRadius);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.subMeshCount = submeshes.Count;
                for (int i = 0; i < submeshes.Count; i++)
                    mesh.SetTriangles(submeshes[i], i);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
