using System.Collections.Generic;
using System.IO;
using System;
using StellarStriderLib.Models;
using StellarStriderLib;
using UnityEngine;
using WarpTen.Simulation;
using static UnityEngine.Object;

namespace WarpTen
{
    internal static class MatrixItems
    {
        private static readonly Dictionary<uint, MatrixGrade> Items = new();
        private static readonly Dictionary<char, uint> Ids = new();
        private static readonly Dictionary<char, Texture2D> Icons = new();
        public static string Key(MatrixGrade grade) => "annihilation_matrix_" + char.ToLowerInvariant(grade.Code);

        public static void Register(ModRegistry registry)
        {
            foreach (var grade in MatrixGrade.All)
            {
                var tint = grade.Code == 'A' ? new Color(.85f, .4f, 1) :
                    grade.Code == 'B' ? new Color(.25f, .65f, 1) :
                    grade.Code == 'C' ? new Color(.3f, .9f, .45f) : new Color(1, .65f, .2f);
                var meshes = Meshes(tint);
                registry.RegisterItem(new ItemDefinition
                {
                    Id = Key(grade),
                    Name = $"Annihilation Matrix — Grade {grade.Code}",
                    Mass = 1,
                    MaxStackSize = 1,
                    Configure = context => { context.Settings.icon = Icon(grade.Code, tint); },
                    Models = meshes
                });
            }
        }

        private static MeshDefinition[] Meshes(Color tint)
        {
            var meshes = ModelCatalog.Meshes("matrix");
            foreach (var mesh in meshes)
            {
                if (mesh.Resource.EndsWith("__matrix_core.obj", StringComparison.Ordinal))
                {
                    var color = tint * .16f;
                    color.a = 1;
                    mesh.Material.Tint = color;
                }
                else if (mesh.Resource.EndsWith("__matrix_flux.obj", StringComparison.Ordinal))
                    mesh.Material.Tint = tint;
            }

            return meshes;
        }

        private static Texture2D Icon(char grade, Color tint)
        {
            if (Icons.TryGetValue(grade, out var existing))
                return existing;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = $"Annihilation Matrix {grade} ({ColorUtility.ToHtmlStringRGB(tint)})",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            try
            {
                using var stream =
                    typeof(MatrixItems).Assembly.GetManifestResourceStream(
                        $"WarpTen.Models.matrix_{char.ToLowerInvariant(grade)}.png") ??
                    throw new InvalidOperationException("Matrix inventory icon is missing.");
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                if (!texture.LoadImage(bytes.ToArray(), true))
                    throw new InvalidOperationException("Matrix inventory icon is invalid.");
            }
            catch
            {
                Destroy(texture);
                throw;
            }

            Icons.Add(grade, texture);
            return texture;
        }

        public static void Unload()
        {
            Clear();
            foreach (var icon in Icons.Values)
                Destroy(icon);
            Icons.Clear();
        }

        public static void Bind()
        {
            Clear();
            foreach (var grade in MatrixGrade.All)
            {
                if (!StriderRegistry.TryGetNumericId(ItemRef.Mod(Content.ModId + ":" + Key(grade)), out uint id))
                    throw new InvalidOperationException("Annihilation Matrix item is unavailable.");
                Items.Add(id, grade);
                Ids.Add(grade.Code, id);
            }
        }

        public static bool TryGet(uint id, out MatrixGrade grade) => Items.TryGetValue(id, out grade);
        public static bool Contains(uint id) => Items.ContainsKey(id);
        public static uint Id(MatrixGrade grade) => Ids[grade.Code];

        public static void Clear()
        {
            Items.Clear();
            Ids.Clear();
        }
    }
}