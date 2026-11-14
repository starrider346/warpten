using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System;
using HarmonyLib;
using Managers.Server.Saving;
using Newtonsoft.Json.Linq;
using Saving.Interface.Model;
using Saving.Serialization.V15.Players;
using Saving.Serialization.V15;
using Saving.Serialization.V4.Items;
using Saving.Serialization.V4.Ships;
using Saving.Services.IO;
using Saving.Services;
using StellarStriderLib;
using WarpTen.Simulation;
using static HarmonyLib.AccessTools;
using HarmonyEngine = HarmonyLib.Harmony;

namespace WarpTen.Runtime
{
    internal static class MatrixItemMigration
    {
        private static readonly Dictionary<uint, char> LegacyIds = new();
        private const string RegistryName = "stellarstrider.registry.json";
        private const string ArchiveName = "warpten.matrix-variants.registry.json";
        private static int templateDepth;

        public static void Install(HarmonyEngine harmony)
        {
            harmony.Patch(Method(typeof(GameLoader), "LoadState"),
                prefix: new HarmonyMethod(typeof(MatrixItemMigration), nameof(Loading)) { priority = Priority.First });
            harmony.Patch(Method(typeof(SaveFileReader), "ReadGame"),
                postfix: new HarmonyMethod(typeof(MatrixItemMigration), nameof(ConvertItems)));
            var library = typeof(StriderRegistry).Assembly;
            harmony.Patch(Method(library.GetType("StellarStriderLib.Core.WorldRegistry", true), "Bind"),
                prefix: new HarmonyMethod(typeof(MatrixItemMigration), nameof(RemoveVariants)));
            harmony.Patch(
                Method(library.GetType("StellarStriderLib.Runtime.ContentSession", true), "Occupied"),
                postfix: new HarmonyMethod(typeof(MatrixItemMigration), nameof(ReserveIds)));
            harmony.Patch(
                Method(typeof(ServerStateLoader), "LoadWorldFromStreamingAssetSave"),
                prefix: new HarmonyMethod(typeof(MatrixItemMigration), nameof(Template)),
                finalizer: new HarmonyMethod(typeof(MatrixItemMigration), nameof(TemplateFinished)));
        }

        private static void Template() => templateDepth++;
        private static void TemplateFinished() => templateDepth--;

        private static void Loading(ISavedGameFile __0)
        {
            LegacyIds.Clear();
            var directory = __0?.SaveFileInfo?.Directory?.FullName;
            if (directory == null || templateDepth != 0)
                return;
            var registry = Path.Combine(directory, RegistryName);
            var archive = Path.Combine(directory, ArchiveName);
            if (File.Exists(archive))
                ReadIds(JObject.Parse(File.ReadAllText(archive)));
            if (!File.Exists(registry))
                return;
            var document = JObject.Parse(File.ReadAllText(registry));
            ReadIds(document);
            if (LegacyIds.Count == 0 || File.Exists(archive))
                return;
            var temporary = archive + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true))
                    writer.Write(document.ToString());
                stream.Flush(true);
            }

            File.Move(temporary, archive);
        }

        internal static bool IsVariant(string key, out char grade)
        {
            const string prefix = "warpten:annihilation_matrix_";
            grade = default;
            if (key == null || !key.StartsWith(prefix, StringComparison.Ordinal) || key.Length <= prefix.Length + 2)
                return false;
            grade = char.ToUpperInvariant(key[prefix.Length]);
            if ("DCBA".IndexOf(grade) < 0 || key[prefix.Length + 1] != '_')
                return false;
            var suffix = key.Substring(prefix.Length + 2);
            return suffix == "spent" || suffix.Length == 3 && int.TryParse(suffix, out int oldVariant) &&
                oldVariant > 0 && oldVariant < 100;
        }

        private static void ReadIds(JObject document)
        {
            if (document["items"] is not JObject items)
                return;
            foreach (var property in items.Properties())
                if (IsVariant(property.Name, out char grade))
                    LegacyIds[(uint)property.Value] = grade;
        }

        private static void RemoveVariants(object __instance)
        {
            var maps = (IDictionary)Field(__instance.GetType(), "_maps").GetValue(__instance);
            foreach (DictionaryEntry map in maps)
            {
                if (map.Key.ToString() != "Item")
                    continue;
                var items = (IDictionary)map.Value;
                foreach (var key in items.Keys.Cast<string>().Where(k => IsVariant(k, out _)).ToArray())
                    items.Remove(key);
            }
        }

        private static void ReserveIds(object __0, ref IEnumerable<uint> __result)
        {
            if (__0.ToString() == "Item" && LegacyIds.Count != 0)
                __result = __result.Concat(LegacyIds.Keys).Distinct().ToArray();
        }

        private static uint ConvertId(uint id) => LegacyIds.TryGetValue(id, out char grade)
            ? MatrixItems.Id(MatrixGrade.All.Single(g => g.Code == grade))
            : id;

        private static void Slots(SerializableInventorySlot[] slots)
        {
            if (slots == null)
                return;
            for (var i = 0; i < slots.Length; i++)
                if (slots[i].hasItem)
                    slots[i].itemId = ConvertId(slots[i].itemId);
        }

        internal static void ConvertItems(SerializableGameWorld __result)
        {
            if (LegacyIds.Count == 0)
                return;
            foreach (var player in __result.players ??
                                   Array.Empty<SerializablePlayer>())
            {
                Slots(player.inventory);
                Slots(player.toolbar);
            }

            foreach (var ship in __result.ships ?? Array.Empty<Saving.Serialization.V15.Ships.SerializableShip>())
            foreach (var storage in ship.storageStates ??
                                    Array.Empty<SerializableStorageState>())
                Slots(storage.slots);
            if (__result.worldObjects != null)
                for (var i = 0; i < __result.worldObjects.Length; i++)
                {
                    __result.worldObjects[i].itemId = ConvertId(__result.worldObjects[i].itemId);
                    __result.worldObjects[i].contentItemId = ConvertId(__result.worldObjects[i].contentItemId);
                }
        }
    }
}