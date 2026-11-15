using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using HarmonyLib;
using Items.Interface.Service;
using Items.Model;
using Players.Interface.Model;
using Players.Inventory;
using Players.Network;
using Ships.Cables;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.Parts;
using Ships.Services;
using Ships;
using StellarStriderLib;
using UI.Inventory;
using WarpTen.Simulation;
using static Core.Services.ServiceLocator;
using static HarmonyLib.AccessTools;
using HarmonyEngine = HarmonyLib.Harmony;

namespace WarpTen.Runtime
{
    internal static class CoreInventory
    {
        private static readonly Dictionary<PartContext, PartInstance> Server = new(), Client = new();
        private static readonly PropertyInfo SlotsProperty = typeof(StorageState).GetProperty("InventorySlots");

        private static readonly FieldInfo ProviderPlayer =
            Field(typeof(StorageInventoryProvider), "_playerState");

        public static void Install(HarmonyEngine harmony)
        {
            Patch(harmony, typeof(StorageState), "get_SlotCount", nameof(SlotCount));
            Patch(harmony, typeof(StorageState), "GetSlot", nameof(ReadSlot));
            Patch(harmony, typeof(StorageState), "WithUpdatedSlot", nameof(UpdateSlot));
            var serialization =
                TypeByName("Managers.Server.Network.Serialization.StatefulPartSerialization") ??
                throw new TypeLoadException("Native part-state serialization is unavailable.");
            Patch(harmony, serialization, "WriteState", nameof(WriteNativeStorage));
            Patch(harmony, serialization, "ReadStateData", nameof(ReadNativeStorage));
            Patch(harmony, typeof(TrackedShipServer), "SetChestSlot", nameof(ValidateSlot));
            var accessor = typeof(PlayerInventoryTransactions).GetNestedType("ChestAccessor", BindingFlags.NonPublic) ??
                           throw new MissingMemberException("Native chest accessor is unavailable.");
            Patch(harmony, accessor, "CanReceiveItem", nameof(CanReceive));
            foreach (var name in new[]
                     {
                         "MoveItem",
                         "MoveSplitItem"
                     }
                    )
                Patch(harmony, typeof(PlayerInventoryTransactions), name, nameof(BeforeMove));
            foreach (var name in new[]
                     {
                         "ShiftItem",
                         "ShiftSplitItem"
                     }
                    )
                Patch(harmony, typeof(PlayerInventoryTransactions), name, nameof(BeforeShift));
            Patch(harmony, typeof(PlayerInventoryTransactions), "GetItemAtSlot", nameof(BeforeDrop));
            Patch(harmony, typeof(InventoryGrid), "UpdateInventory", nameof(UpdateGrid));
            harmony.Patch(
                Method(typeof(InventoryGrid), "OnDisable") ??
                throw new MissingMethodException(typeof(InventoryGrid).FullName, "OnDisable"),
                postfix: new HarmonyMethod(typeof(CoreInventory), nameof(RestoreGrid)));
        }

        private static void Patch(HarmonyEngine harmony, Type type, string method, string hook) => harmony.Patch(
            Method(type, method) ?? throw new MissingMethodException(type.FullName, method),
            prefix: new HarmonyMethod(typeof(CoreInventory), hook));

        private static InventorySlot[] Slots(StorageState state) =>
            state == null ? null : (InventorySlot[])SlotsProperty.GetValue(state);

        private static bool Single(StorageState state) => Slots(state)?.Length == 1;

        private static bool SlotCount(StorageState __instance, ref int __result)
        {
            if (!Single(__instance))
                return true;
            __result = 1;
            return false;
        }

        private static bool ReadSlot(StorageState __instance, byte __0, ref InventorySlot __result)
        {
            if (!Single(__instance) || __0 == 0)
                return true;
            __result = default;
            return false;
        }

        private static bool UpdateSlot(StorageState __instance, byte __0, InventorySlot __1, ref IStoragePart __result)
        {
            if (!Single(__instance))
                return true;
            __result = __0 == 0 ? Storage(__1, __instance.IsInUse) : __instance;
            return false;
        }

        internal static StorageState Storage(InventorySlot slot, bool inUse = false)
        {
            var state = new StorageState();
            SlotsProperty.SetValue(state, new[] { slot });
            typeof(StorageState).GetProperty("IsInUse").SetValue(state, inUse);
            return state;
        }

        private static IStatefulPartState NativeStorage(IStatefulPartState state)
        {
            if (state is not StorageState storage || !Single(storage))
                return state;
            var wire = new StorageState();
            Slots(wire)[0] = storage.GetSlot(0);
            typeof(StorageState).GetProperty("IsInUse").SetValue(wire, storage.IsInUse);
            return wire;
        }

        private static void WriteNativeStorage(ref IStatefulPartState __1) => __1 = NativeStorage(__1);
        private static void ReadNativeStorage(ref IStatefulPartState __0) => __0 = NativeStorage(__0);

        public static void Add(PartInstance part)
        {
            if (!Content.IsCore(part.Part))
                return;
            (part.IsServer ? Server : Client)[part.Id] = part;
            if (!part.IsServer || part.Ship is not TrackedShipServer ship)
                return;
            if (!Single(part.Part.State as StorageState))
            {
                var old = part.Part.State as StorageState;
                if (old != null && Slots(old).Skip(1).Any(s => s.HasItem))
                    throw new InvalidOperationException("Warp Core has unexpected extra inventory contents.");
                ship.UpdatePartState(part.Id.PartId, Storage(old?.GetSlot(0) ?? default, old?.IsInUse ?? false));
            }
        }

        public static void Remove(PartInstance part)
        {
            (part.IsServer ? Server : Client).Remove(part.Id);
        }

        public static void Clear()
        {
            Server.Clear();
            Client.Clear();
        }

        public static bool Read(PartInstance part, out MatrixGrade grade)
        {
            grade = default;
            if (part?.Part.State is not IStoragePart inventory || !Content.IsCore(part.Part) || inventory.SlotCount < 1)
                return false;
            if (part.IsServer && inventory.SlotCount != 1)
                return false;
            var slot = inventory.GetSlot(0);
            return slot.HasItem && slot.Quantity == 1 && MatrixItems.TryGet(slot.ItemId, out grade);
        }

        private static PartInstance OpenCore(PlayerNetworkState state)
        {
            var player = state.ServerState;
            return player.Type == PlayerStateType.InteractingWithChest &&
                   Server.TryGetValue(new PartContext(player.EntityId, player.PartId), out var part) && part.IsActive
                ? part
                : null;
        }

        private static bool CanReceive(PlayerNetworkState ____state, uint __0, ref bool __result)
        {
            if (OpenCore(____state) == null)
                return true;
            __result = MatrixItems.Contains(__0);
            return false;
        }

        private static bool Valid(InventorySlot slot) =>
            !slot.HasItem || slot.Quantity == 1 && MatrixItems.Contains(slot.ItemId);

        private static bool ValidateSlot(TrackedShipServer __instance, ushort __0, byte __1, InventorySlot __2) =>
            !__instance.TryGetStatefulPart(__0, out var part) || !Content.IsCore(part) || __1 == 0 && Valid(__2);

        private static bool TrySlot(PlayerNetworkState player, PartInstance core, ItemSlotId id, out InventorySlot slot)
        {
            slot = default;
            if (id.Type == InventoryType.Chest)
            {
                if (id.SlotId != 0 || core.Part.State is not IStoragePart storage)
                    return false;
                slot = storage.GetSlot(0);
                return true;
            }

            var list = id.Type == InventoryType.ToolBar ? player.Toolbar :
                id.Type == InventoryType.MainInventory ? player.Inventory : null;
            if (list == null || id.SlotId >= list.Count)
                return false;
            slot = list[id.SlotId];
            return true;
        }

        private static bool BeforeMove(PlayerNetworkState ____playerState, ItemSlotId __0, ItemSlotId __1)
        {
            var core = OpenCore(____playerState);
            if (core == null || __0.Type != InventoryType.Chest && __1.Type != InventoryType.Chest)
                return true;
            if (__0.Type == __1.Type && __0.SlotId == __1.SlotId)
                return false;
            if (!TrySlot(____playerState, core, __0, out var from) || !TrySlot(____playerState, core, __1, out var to))
                return false;
            if (__1.Type == InventoryType.Chest && !Valid(from) || __0.Type == InventoryType.Chest && !Valid(to))
                return false;
            return true;
        }

        private static bool BeforeShift(PlayerNetworkState ____playerState, ItemSlotId __0)
        {
            var core = OpenCore(____playerState);
            if (core == null)
                return true;
            if (!TrySlot(____playerState, core, __0, out var source))
                return false;
            if (__0.Type == InventoryType.Chest)
            {
                if (!____playerState.Inventory.Concat(____playerState.Toolbar).Any(s => !s.HasItem))
                    return false;
            }
            else if (source.HasItem && MatrixItems.Contains(source.ItemId))
            {
                var installed = ((IStoragePart)core.Part.State).GetSlot(0);
                if (installed.HasItem &&
                    !____playerState.Inventory.Concat(____playerState.Toolbar).Any(s => !s.HasItem))
                    return false;
            }

            return true;
        }

        private static bool BeforeDrop(PlayerNetworkState ____playerState, ItemSlotId __0, ref ItemDrop __result)
        {
            var core = OpenCore(____playerState);
            if (core == null || __0.Type != InventoryType.Chest)
                return true;
            if (__0.SlotId != 0)
            {
                __result = default;
                return false;
            }

            return true;
        }

        private static bool UpdateGrid(InventoryType ___source, InventorySlotUI[] ____slots,
            IGenericInventoryProvider ____inventoryProvider)
        {
            if (___source != InventoryType.Chest || ____slots == null)
                return true;
            var player = ____inventoryProvider is StorageInventoryProvider provider
                ? ProviderPlayer.GetValue(provider) as IPlayerStateClientRead
                : null;
            PartInstance core = null;
            if (player?.ClientState.Type == PlayerStateType.InteractingWithChest)
                Client.TryGetValue(new PartContext(player.ClientState.EntityId, player.ClientState.PartId), out core);
            for (var i = 0; i < ____slots.Length; i++)
                ____slots[i].gameObject.SetActive(core == null || i == 0);
            if (core == null)
                return true;
            ____slots[0].UpdateSlot(core.Part.State is IStoragePart storage ? storage.GetSlot(0) : default);
            return false;
        }

        private static void RestoreGrid(InventoryType ___source, InventorySlotUI[] ____slots)
        {
            if (___source == InventoryType.Chest && ____slots != null)
                foreach (var slot in ____slots)
                    if (slot != null)
                        slot.gameObject.SetActive(true);
        }

        internal static PartInstance ClientCore(IGenericInventoryProvider provider)
        {
            var player = provider is StorageInventoryProvider storage
                ? ProviderPlayer.GetValue(storage) as IPlayerStateClientRead
                : null;
            if (player?.ClientState.Type != PlayerStateType.InteractingWithChest)
                return null;
            return Client.TryGetValue(new PartContext(player.ClientState.EntityId, player.ClientState.PartId),
                out var core) && core.IsActive
                ? core
                : null;
        }

        public static bool TryCharging(uint ship, ushort part, out float remaining)
        {
            remaining = 0;
            if (!Client.TryGetValue(new PartContext(ship, part), out var core))
                return false;
            var phase = core.State.Get<string>("phase");
            if (phase != "Charging" && phase != "Ready")
                return false;
            remaining = (float)core.State.Get<double>("chargeRemaining");
            return Tuning.Finite(remaining) && remaining >= 0;
        }

        public static void RetirePower(uint ship, ushort part)
        {
            var cables = GetService<CablesServerTracker>();
            if (cables != null && cables.TryGetCableId(new EntityPartSocket(ship, part, 1), out uint cable))
                cables.DeleteCable(cable);
        }
    }
}
