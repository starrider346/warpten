using System.Linq;
using System;
using HarmonyLib;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts.StateTypes;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.State;
using Ships.Interface.Model;
using Ships.Parts.Common.Services;
using Ships.Parts.WarpDrive;
using Ships.Physics;
using Ships.Services;
using Ships;
using UnityEngine;
using WorldTracking.Interface.Model.Spaces;
using WorldTracking.Spaces;
using static HarmonyLib.AccessTools;
using HarmonyEngine = HarmonyLib.Harmony;

namespace WarpTen.Runtime
{
    internal static class NativeHooks
    {
        public static void Install(HarmonyEngine harmony)
        {
            CoreSocketVisuals.Install(harmony);
            CoreInventory.Install(harmony);
            MatrixItemMigration.Install(harmony);
            WarpRotationLimit.Install(harmony);
            Patch(harmony, typeof(TrackedShipServer), "UpdateParts", nameof(Complete), false);
            Patch(harmony, typeof(WarpSpacesServerUpdater), "UpdateWarpSpaces", nameof(CompleteEntries), false);
            Patch(harmony, typeof(ShipWarpDrivesUpdater), "UpdateWarpDriveState", nameof(UpdateDrive), true);
            Patch(harmony, typeof(ShipWarpPhysics), "CalculateWarpTargetVector", nameof(Target), true);
            Patch(harmony, typeof(ShipWarpPhysics), "UpdateWarpPhysics", nameof(Physics), true);
            var hover = typeof(ShipInteractionHoverToggler);
            Patch(harmony, hover, "HoverEnter", nameof(HighlightModel), true);
            Patch(harmony, hover, "HoverLeave", nameof(HighlightModel), true);
        }

        private static void HighlightModel(PartContext __0, ref string __1,
            ShipsClientTracker ____ships)
        {
            if (!____ships.TryGetShipVisuals(__0.ShipId, out var ship) ||
                !ship.TryGetFloatingPartVisual(__0.PartId, out var visual))
                return;
            var root = visual.GameObject;
            if (root.GetComponentsInChildren<MeshFilter>(true).Any(filter =>
                    filter.sharedMesh != null &&
                    filter.sharedMesh.name.StartsWith("WarpTen.Models.", StringComparison.Ordinal)))
                __1 = root.name;
        }

        private static void Patch(HarmonyEngine h, Type type, string name, string handler, bool prefix)
        {
            var method = Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
            var hook = new HarmonyMethod(typeof(NativeHooks), handler);
            if (prefix)
                h.Patch(method, prefix: hook);
            else
                h.Patch(method, postfix: hook);
        }

        private static void Complete(TrackedShipServer __instance, float deltaTime) =>
            DriveRuntime.Complete(__instance, deltaTime);

        private static void CompleteEntries() => DriveRuntime.CompleteEntries();

        private static bool UpdateDrive(ushort __0, IShipStateRead ____ship,
            ShipState ____networkState)
        {
            if (!____ship.TryGetStatefulPart(__0, out var p))
                return true;
            if (Content.IsCore(p))
                return false;
            if (Content.CoreId != 0 && ____ship.StatefulParts.Any(Content.IsCore))
            {
                if (p.State is WarpDriveState state &&
                    state.State != WarpDriveStateType.Idle)
                    ____networkState.SetPartState(p.Id,
                        DriveRuntime.NewNative(WarpDriveStateType.Idle, 0));
                return false;
            }

            return true;
        }

        private static bool Target(TrackedShipServer ____ship, WarpSpace __0, ref Vector3 __result)
        {
            if (!DriveRuntime.TryGet(____ship.Id, out var drive) ||
                !____ship.TryGetStatefulPart(__0.SourcePartId, out var core) || !Content.IsCore(core))
                return true;
            __result = ____ship.Rotation * (NacelleGeometry.Find(____ship).FrameRotation * Vector3.forward) *
                       (float)drive.Speed;
            return false;
        }

        private static bool Physics(ShipWarpPhysics __instance, TrackedShipServer ____ship)
        {
            if (!DriveRuntime.OwnsWarp(____ship))
                return true;
            if (DriveRuntime.TryGet(____ship.Id, out var drive) && drive.WantsWarp)
                return true;
            __instance.ExitWarp();
            return false;
        }
    }
}
