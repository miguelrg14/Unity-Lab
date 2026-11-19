using NUnit.Framework;
using UnityEngine;
using UnityLab.EditorTools;

namespace UnityLab.Tests.Editor
{
    public class ProceduralMeshTests
    {
        static readonly Bounds Target = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 3f, 4f));

        [Test]
        public void EveryKind_FitsTheTargetBounds([Values] ProceduralMeshes.Kind kind)
        {
            Mesh mesh = ProceduralMeshes.Build(kind, Target, kind.ToString());
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            Assert.That(mesh.subMeshCount, Is.EqualTo(ProceduralMeshes.SubmeshCount(kind)));
            Assert.That(Vector3.Distance(mesh.bounds.center, Target.center), Is.LessThan(0.01f));
            Assert.That(mesh.bounds.size.x, Is.LessThanOrEqualTo(Target.size.x + 0.01f));
            Assert.That(mesh.bounds.size.y, Is.LessThanOrEqualTo(Target.size.y + 0.01f));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Faces_PointAwayFromTheCentre([Values(ProceduralMeshes.Kind.Box, ProceduralMeshes.Kind.Rock, ProceduralMeshes.Kind.Wheel, ProceduralMeshes.Kind.Stump)] ProceduralMeshes.Kind kind)
        {
            Mesh mesh = ProceduralMeshes.Build(kind, new Bounds(Vector3.zero, Vector3.one * 2f), kind.ToString());
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            int outward = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(normal, (a + b + c) / 3f - mesh.bounds.center) >= 0f)
                    outward++;
            }
            // Single convex parts centred on the mesh: every face must point outwards.
            Assert.That(outward, Is.EqualTo(triangles.Length / 3));
            Object.DestroyImmediate(mesh);
        }

        [TestCase("SM_PolygonCity_Veh_Car_Small_01", ProceduralMeshes.Kind.CarBody)]
        [TestCase("SM_Veh_Car_Small_Wheel_fl", ProceduralMeshes.Kind.Wheel)]
        [TestCase("SM_Bean_Cop_01", ProceduralMeshes.Kind.Pawn)]
        [TestCase("SM_Generic_TreeStump_01", ProceduralMeshes.Kind.Stump)]
        [TestCase("SM_Generic_Tree_02", ProceduralMeshes.Kind.Tree)]
        [TestCase("SM_PolygonApocalypse_Bld_House_01", ProceduralMeshes.Kind.House)]
        [TestCase("SM_Generic_Mountains_Grass_02", ProceduralMeshes.Kind.Rock)]
        [TestCase("Something else", ProceduralMeshes.Kind.Box)]
        public void Classify(string name, ProceduralMeshes.Kind expected)
        {
            Assert.That(ProceduralMeshes.Classify(name), Is.EqualTo(expected));
        }
    }
}
