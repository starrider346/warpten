using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using Ships.Interface.Model.Parts;
using Ships;
using StellarStriderLib;
using UnityEngine.Rendering;
using UnityEngine;
using WarpTen.Simulation;
using static UnityEngine.Object;

namespace WarpTen.Runtime
{
    internal static class NacelleDebug
    {
        private static readonly Dictionary<PartContext, TrackedShipClient> Ships = new();

        private static readonly Color WidthColor = new(.1f, .8f, 1, .12f),
            CurveColor = new(1, .8f, .15f, .12f),
            InvalidColor = new(1, .15f, .1f, .18f);

        private static readonly Surface[] Surfaces = new Surface[4];
        private static bool enabled;
        private static GameObject root;
        private static Material material;
        private static NacelleGeometry displayed;
        private static TrackedShipClient selected;
        private static string status;

        public static void Observe(PartInstance p)
        {
            if (!p.IsServer && p.Ship is TrackedShipClient ship && Content.IsCore(p.Part))
                Ships[p.Id] = ship;
        }

        public static void Remove(PartInstance p)
        {
            if (!p.IsServer)
                Ships.Remove(p.Id);
        }

        public static void Toggle()
        {
            enabled = !enabled;
            if (!enabled)
                ReleaseSurfaces();
            MelonLogger.Msg("Nacelle rule rendering " + (enabled ? "enabled" : "disabled") + ". F9 toggles it.");
        }

        public static void LateUpdate()
        {
            if (!enabled || !StriderRegistry.WorldReady)
                return;
            var camera = Camera.main;
            var next = InfuserHover.HoveredShip ?? Ships.Values.Distinct()
                .Where(s => !s.IsShipDestroyed && s.IsVisualLoaded && s.VisualObject != null).OrderBy(s =>
                    camera == null ? 0 : (s.VisualObject.transform.position - camera.transform.position).sqrMagnitude)
                .FirstOrDefault();
            if (next == null)
            {
                ReleaseSurfaces();
                status = "Place a Warp Core and two aligned infusers to show their limits.";
                return;
            }

            var geometry = NacelleGeometry.Find(next);
            status =
                $"Ship {next.Id}: {geometry.Blocker ?? "Nacelle rules satisfied"}\nOutboard hull L {geometry.LeftOverhang:F2}m, R {geometry.RightOverhang:F2}m / max 3m; clear-zone blocks {geometry.ClearanceBlocks}";
            if (next == selected && geometry == displayed && root != null)
                return;
            ReleaseSurfaces();
            selected = next;
            displayed = geometry;
            if (geometry.Clearance == null)
                return;
            if (material == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored") ??
                             Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
                if (shader == null)
                {
                    status = "No debug surface shader is available.";
                    return;
                }

                material = new Material(shader)
                {
                    name = "WarpTen nacelle debug",
                    hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = 3100
                };
                material.SetColor("_Color", Color.white);
                if (material.HasProperty("_SrcBlend"))
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend"))
                    material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                if (material.HasProperty("_Cull"))
                    material.SetInt("_Cull", (int)CullMode.Off);
                if (material.HasProperty("_ZWrite"))
                    material.SetInt("_ZWrite", 0);
                if (material.HasProperty("_ZTest"))
                    material.SetInt("_ZTest", (int)CompareFunction.Always);
            }

            root = new GameObject("WarpTen nacelle rule surfaces")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = next.VisualObject.gameObject.layer
            };
            root.transform.SetParent(next.VisualObject.transform, false);
            root.transform.localPosition = geometry.FramePosition;
            root.transform.localRotation = geometry.FrameRotation;
            var field = geometry.Clearance;
            var minY = geometry.FrameMin.y - 1;
            var maxY = geometry.FrameMax.y + 1;
            var minZ = geometry.FrameMin.z - 1;
            var maxZ = geometry.FrameMax.z + 1;

            void Width(int index, float x, bool valid)
            {
                var vertices = new[]
                {
                    new Vector3(x, minY, minZ),
                    new Vector3(x, maxY, minZ),
                    new Vector3(x, minY, maxZ),
                    new Vector3(x, maxY, maxZ)
                };
                Surfaces[index] = new Surface(root, index == 0 ? "Left width limit" : "Right width limit", material);
                Surfaces[index].Build(vertices, StripTriangles(1),
                    new[] { vertices[0], vertices[1], vertices[3], vertices[2] }, valid ? WidthColor : InvalidColor);
            }

            Width(0, field.LeftWidthLimit, geometry.LeftOverhang <= NacelleClearance.MaximumOutboardBlocks + 1e-5f);
            Width(1, field.RightWidthLimit, geometry.RightOverhang <= NacelleClearance.MaximumOutboardBlocks + 1e-5f);
            for (var side = 0; side < 2; side++)
            {
                var vertices = new Vector3[(NacelleClearance.CurveSegments + 1) * 2];
                var outline = new List<Vector3>();
                for (var i = 0; i <= NacelleClearance.CurveSegments; i++)
                {
                    var x = field.CurveX(i);
                    var y = field.HeightAt(x) * (side == 0 ? 1 : -1);
                    vertices[2 * i] = new Vector3(x, y, field.MinZ);
                    vertices[2 * i + 1] = new Vector3(x, y, field.MaxZ);
                    outline.Add(vertices[2 * i]);
                }

                for (var i = NacelleClearance.CurveSegments; i >= 0; i--)
                    outline.Add(vertices[2 * i + 1]);
                Surfaces[side + 2] = new Surface(root,
                    side == 0 ? "Upper nacelle clearance" : "Lower nacelle clearance", material);
                Surfaces[side + 2].Build(vertices, StripTriangles(NacelleClearance.CurveSegments), outline.ToArray(),
                    geometry.ClearanceBlocks == 0 ? CurveColor : InvalidColor);
            }
        }

        private static int[] StripTriangles(int segments)
        {
            var triangles = new List<int>();
            for (var i = 0; i < segments; i++)
            {
                var a = i * 2;
                var b = a + 1;
                var c = a + 2;
                var d = a + 3;
                triangles.AddRange(new[] { a, b, c, c, b, d });
            }

            return triangles.ToArray();
        }

        public static void OnGUI()
        {
            if (!enabled || !StriderRegistry.WorldReady)
                return;
            GUI.Box(new Rect(20, 590, 780, 100),
                "F9 nacelle rules — hovered infuser or nearest ship\nCyan: width limits | Yellow: curved clear zone | Red: violated rule\n" +
                status);
        }

        private sealed class Surface
        {
            private readonly GameObject gameObject;
            private readonly Mesh mesh;
            private readonly LineRenderer outline;

            public Surface(GameObject parent, string name, Material material)
            {
                gameObject = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer))
                {
                    layer = parent.layer
                };
                gameObject.transform.SetParent(parent.transform, false);
                mesh = new Mesh
                {
                    name = name
                };
                gameObject.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = gameObject.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                outline = gameObject.AddComponent<LineRenderer>();
                outline.sharedMaterial = material;
                outline.useWorldSpace = false;
                outline.loop = true;
                outline.widthMultiplier = .035f;
                outline.shadowCastingMode = ShadowCastingMode.Off;
                outline.receiveShadows = false;
            }

            public void Build(Vector3[] vertices, int[] indices, Vector3[] boundary, Color color)
            {
                mesh.vertices = vertices;
                mesh.colors = Enumerable.Repeat(color, vertices.Length).ToArray();
                mesh.triangles = indices;
                mesh.RecalculateBounds();
                color.a = .95f;
                outline.startColor = outline.endColor = color;
                outline.positionCount = boundary.Length;
                outline.SetPositions(boundary);
            }

            public void Dispose()
            {
                Destroy(mesh);
                Destroy(gameObject);
            }
        }

        private static void ReleaseSurfaces()
        {
            for (var i = 0; i < Surfaces.Length; i++)
            {
                Surfaces[i]?.Dispose();
                Surfaces[i] = null;
            }

            if (root != null)
                Destroy(root);
            root = null;
            selected = null;
            displayed = null;
        }

        public static void Clear()
        {
            enabled = false;
            ReleaseSurfaces();
            Ships.Clear();
            status = null;
            if (material != null)
                Destroy(material);
            material = null;
        }
    }
}