using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using Newtonsoft.Json.Linq;
using StellarStriderLib.Models;
using StellarStriderLib;
using UnityEngine;

namespace WarpTen
{
    internal static class ModelCatalog
    {
        private static readonly JObject Catalog = Load();
        private static readonly Color PowerTint = new(.015f, .7f, .88f, 1);
        private static readonly Color IdlePowerTint = new(.40f, .43f, .46f, 1);
        private static readonly Color StatusTint = new(.47f, .9f, .16f, 1);
        private static MaterialPropertyBlock powerProperties;
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");


        private static JObject Load()
        {
            using var stream = typeof(ModelCatalog).Assembly.GetManifestResourceStream("WarpTen.Models.catalog.json") ??
                               throw new InvalidOperationException("WarpTen model catalog is missing.");
            using var reader = new StreamReader(stream);
            return JObject.Parse(reader.ReadToEnd());
        }

        public static MeshDefinition[] Meshes(string id)
        {
            if (Catalog[id] is not JObject model)
                throw new InvalidOperationException("WarpTen model catalog entry is missing: " + id);
            return model["meshes"].Select(mesh =>
            {
                var color = mesh["color"].Values<float>().ToArray();
                var group = (string)mesh["group"];
                return new MeshDefinition
                {
                    Resource = (string)mesh["resource"],
                    Material = new MaterialDefinition
                    {
                        Kind = MaterialKind.Solid,
                        Tint = group == "charge" || group == "status"
                            ? IdlePowerTint
                            : new Color(color[0], color[1], color[2], color[3])
                    }
                };
            }).ToArray();
        }

        public static void Configure(PartBuildContext context, GameObject child, MeshDefinition mesh)
        {
            var renderer = child.GetComponent<Renderer>();
            if (renderer == null)
                return;
            var entry = Catalog.Properties().SelectMany(p => p.Value["meshes"])
                .FirstOrDefault(m => (string)m["resource"] == mesh.Resource);
            if (entry == null)
                return;
            var material = renderer.sharedMaterial;
            material.name = "WarpTen " + mesh.Resource;
            var powered = (string)entry["group"] == "charge" || (string)entry["group"] == "status";
            if (powered)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null && lit.isSupported)
                {
                    material.shader = lit;
                    material.SetTexture(BaseMapId, Texture2D.whiteTexture);
                    material.SetTexture(EmissionMapId, Texture2D.whiteTexture);
                    material.EnableKeyword("_EMISSION");
                    material.SetColor(EmissionColorId, Color.black);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }

                if (material.HasProperty(EmissionColorId))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor(EmissionColorId, Color.black);
                }

                if (material.HasProperty(BaseColorId))
                    material.SetColor(BaseColorId, IdlePowerTint);
                if (material.HasProperty(ColorId))
                    material.SetColor(ColorId, IdlePowerTint);
            }

            if (material.HasProperty(MetallicId))
                material.SetFloat(MetallicId, powered ? .7f : (float)entry["metallic"]);
            if (material.HasProperty(SmoothnessId))
                material.SetFloat(SmoothnessId, 1 - (float)entry["roughness"]);
        }

        public static void PaintPower(Renderer renderer, float charge)
        {
            charge = float.IsNaN(charge) || float.IsInfinity(charge) ? 0 : Mathf.Clamp01(charge);
            var block = powerProperties ??= new MaterialPropertyBlock();
            block.Clear();
            renderer.GetPropertyBlock(block);
            var emissive = renderer.sharedMaterial.HasProperty(EmissionColorId);
            var indicator = renderer.gameObject.name.EndsWith("__status.obj", StringComparison.Ordinal);
            var active = indicator ? StatusTint : PowerTint;
            float intensity = indicator ? 1 : 6;
            var tint = Color.Lerp(IdlePowerTint, active * (emissive ? 1 : intensity), charge);
            tint.a = 1;
            block.SetColor(BaseColorId, tint);
            block.SetColor(ColorId, tint);
            block.SetColor(EmissionColorId, active * (intensity * charge));
            renderer.SetPropertyBlock(block);
        }

        public static BoxShape[] Physics(string id, Vector3 size)
        {
            if (id == "conduit_spacer")
                return new[]
                {
                    Box(0, .25f, 0, .428f, .428f, .25f)
                };
            if (id.StartsWith("conduit_", StringComparison.Ordinal))
            {
                var shapes = new List<BoxShape>
                {
                    Box(0, .5f, 0, .36f, .36f, .36f)
                };
                if (id == "conduit_straight" || id == "conduit_tee")
                    shapes.Add(Box(0, .5f, .25f, .44f, .44f, .5f));
                shapes.Add(Box(0, .5f, -.25f, .44f, .44f, .5f));
                if (id != "conduit_straight")
                    shapes.Add(Box(.25f, .5f, 0, .5f, .44f, .44f));
                return shapes.ToArray();
            }

            if (id == "core")
                return new[]
                {
                    Box(0, .15f, 0, 3, .3f, 3),
                    Box(0, 2.85f, 0, 3, .3f, 3),
                    Box(0, 1.5f, 0, 2.5f, 2.4f, 2.5f)
                };
            if (id == "coil")
                return new[]
                {
                    Box(0, .225f, 0, 3, .45f, 1),
                    Box(0, 1.775f, 0, 3, .45f, 1),
                    Box(-1.25f, 1, 0, .5f, 1.1f, 1),
                    Box(1.25f, 1, 0, .5f, 1.1f, 1)
                };
            return new[]
            {
                Box(0, size.y / 2, 0, size.x, size.y, size.z)
            };
        }

        private static BoxShape Box(float x, float y, float z, float w, float h, float d) => new BoxShape
        {
            Center = new Vector3(x, y, z),
            Size = new Vector3(w, h, d)
        };
    }
}