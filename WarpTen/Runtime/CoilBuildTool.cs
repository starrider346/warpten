using System.Linq;
using System;
using Core.Utils;
using HarmonyLib;
using Managers.Interface.Model;
using Managers.Server.Services;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using Ships.Services;
using Ships;
using StellarStriderLib;
using Tools.Build.Model;
using Tools.Build;
using UnityEngine;
using WarpTen.Simulation;
using WorldTracking.Interface.Values;
using static Core.Services.ServiceLocator;
using static HarmonyLib.AccessTools;
using static Tools.Common.RaycastMasks;
using NRotation = System.Numerics.Quaternion;
using NVector = System.Numerics.Vector3;
using HarmonyEngine = HarmonyLib.Harmony;

namespace WarpTen.Runtime
{
    internal static class CoilBuildTool
    {
        private const float Range = 5;
        private static bool valid, active, hasTarget;
        private static Quaternion previewRotation;
        private static float lastDetection;

        private static readonly Action<PartPreview> Invalid =
            MethodDelegate<Action<PartPreview>>(Method(typeof(PartPreview),
                "MarkPreviewAsInvalid"));

        private static NVector V(Vector3 v) => new(v.x, v.y, v.z);
        private static Vector3 V(NVector v) => new(v.X, v.Y, v.Z);
        private static NRotation Q(Quaternion q) => new(q.x, q.y, q.z, q.w);
        private static Quaternion Q(NRotation q) => new(q.X, q.Y, q.Z, q.W);

        public static void Install(HarmonyEngine harmony)
        {
            harmony.Patch(Method(typeof(PartPreviewDetection), "CalculateSnapDetectionUpdate"),
                prefix: new HarmonyMethod(typeof(CoilBuildTool), nameof(Detect)));
            harmony.Patch(Method(typeof(PartPreview), "Rotate"),
                prefix: new HarmonyMethod(typeof(CoilBuildTool), nameof(Rotate)));
            harmony.Patch(Method(typeof(PartPreview), "UpdatePlacement"),
                postfix: new HarmonyMethod(typeof(CoilBuildTool), nameof(PreviewUpdated)));
            harmony.Patch(Method(typeof(PartPreview), "GetPlacement"),
                prefix: new HarmonyMethod(typeof(CoilBuildTool), nameof(GetPlacement)));
            harmony.Patch(Method(typeof(PartPreview), "OnPartUnselected"),
                postfix: new HarmonyMethod(typeof(CoilBuildTool), nameof(Clear)));
            var logic = GetDeclaredMethods(typeof(ShipInteractionService))
                .Single(m => m.Name.StartsWith("RpcLogic___PlaceFloatingPart_ServerRPC___", StringComparison.Ordinal));
            harmony.Patch(logic, prefix: new HarmonyMethod(typeof(CoilBuildTool), nameof(ValidateRequest)));
        }

        private static FloatingPartPlacement NativePose(ConduitPose pose)
        {
            var rotation = Q(pose.Rotation);
            return new FloatingPartPlacement(V(pose.Position),
                PartOrientationHelper.SnapAnyOrientation(rotation * Vector3.forward),
                PartOrientationHelper.SnapAnyOrientation(rotation * Vector3.up));
        }

        private static ConduitPose Pose(FloatingPartPlacement placement) =>
            new(V(placement.LocalPosition), Q(placement.Rotation));

        private static bool IsTarget(StatefulPart part) =>
            part.Settings.id == Content.InfuserId || part.Settings.id == Content.CoilId;

        private static ConduitPose Attachment(StatefulPart part)
        {
            var offset = part.Settings.id == Content.InfuserId
                ? NacelleLayout.FirstCoilOffset
                : NacelleLayout.CoilLength;
            return new ConduitPose(V(part.LocalPosition - part.LocalRotation * Vector3.forward * offset),
                Q(part.LocalRotation));
        }

        private static ConduitPose ChainAttachment(TrackedShip ship, StatefulPart target)
        {
            var coils = ship.StatefulParts.Where(p => p.Settings.id == Content.CoilId).ToArray();
            CoilNode Node(StatefulPart p) => new(p.Id, V(p.LocalPosition), Q(p.LocalRotation));
            foreach (var infuser in ship.StatefulParts.Where(p => p.Settings.id == Content.InfuserId))
            {
                var chain = NacelleLayout.Chain(Node(infuser), coils.Select(Node).ToArray());
                if (target.Id != infuser.Id && !chain.Contains(target.Id))
                    continue;
                return chain.Count == 0
                    ? Attachment(infuser)
                    : Attachment(coils.Single(p => p.Id == chain[chain.Count - 1]));
            }

            var end = target;
            for (var i = 0; i < coils.Length; i++)
            {
                var nextPose = Attachment(end);
                var matches = coils.Where(p =>
                        ConduitPlacement.Matches(new ConduitPose(V(p.LocalPosition), Q(p.LocalRotation)), nextPose))
                    .ToArray();
                if (matches.Length != 1)
                    break;
                end = matches[0];
            }

            return Attachment(end);
        }

        private static bool Detect(SnapDetectionState __0, CameraPhysicsData __1, ref SnapDetectionResults __result)
        {
            var settings = __0.PartSettings;
            if (settings == null || settings.id != Content.CoilId || __0.IgnoredPart.HasValue)
                return true;
            active = true;
            valid = false;
            hasTarget = false;
            lastDetection = Time.unscaledTime;
            __result = new SnapDetectionResults
            {
                pos = __1.CameraPos + __1.Direction * 2.5f
            };
            if (!StriderRegistry.WorldReady)
                return false;
            var ships = GetService<ShipsClientTracker>();
            if (ships == null)
                return false;
            if (!ClientScene.PhysicsScene.Raycast(__1.CameraPos, __1.Direction, out var hit, Range,
                    BuildDetectionLayer, QueryTriggerInteraction.UseGlobal))
                return false;
            IShipPartPlacements targetProxy = null;
            StatefulPart targetPart = null;
            TrackedShip targetShip = null;
            foreach (var ship in ships.Ships)
            {
                if (ship.IsShipDestroyed || !ship.IsVisualLoaded || !ship.IsRigidbodyLoaded)
                    continue;
                var proxy = ship.GetPartPlacements();
                if (proxy == null)
                    continue;
                foreach (var part in ship.StatefulParts)
                {
                    if (!IsTarget(part) ||
                        !ShipPartsTagDetection.ColliderIsPart(hit.collider, new PartContext(ship.Id, part.Id)))
                        continue;
                    targetProxy = proxy;
                    targetPart = part;
                    targetShip = ship;
                    break;
                }

                if (targetPart != null)
                    break;
            }

            if (targetPart == null)
                return false;
            var solution = ChainAttachment(targetShip, targetPart);
            var placement = NativePose(solution);
            var representable = ConduitPlacement.Matches(solution, Pose(placement));
            valid = representable && targetProxy.IsFloatingPlacementFree(placement, settings.Bounds, null);
            hasTarget = true;
            previewRotation = targetProxy.ToWorldRotation(placement.Rotation);
            __result = new SnapDetectionResults
            {
                IsSnapped = valid,
                shipPartsProxy = targetProxy,
                placement = placement,
                pos = targetProxy.ToWorldPos(placement)
            };
            return false;
        }

        private static bool Rotate(PartSettings ____actualPreviewSettings)
        {
            if (____actualPreviewSettings == null || ____actualPreviewSettings.id != Content.CoilId)
                return true;
            return false;
        }

        private static void PreviewUpdated(PartPreview __instance, PartSettings ____actualPreviewSettings,
            ref Vector3 ____currentPreviewPos, Vector3 ____previewTargetPos, GameObject ____objectPreview)
        {
            if (____actualPreviewSettings == null || ____actualPreviewSettings.id != Content.CoilId)
                return;
            ____currentPreviewPos = ____previewTargetPos;
            if (hasTarget && ____objectPreview != null)
                ____objectPreview.transform.rotation = previewRotation;
            if (!valid)
                Invalid(__instance);
        }

        private static bool GetPlacement(PartSettings ____actualPreviewSettings, ref PreviewPlacement __result)
        {
            if (____actualPreviewSettings == null || ____actualPreviewSettings.id != Content.CoilId)
                return true;
            if (active && valid && Time.unscaledTime - lastDetection < .25f)
                return true;
            __result = null;
            return false;
        }

        private static bool ValidateRequest(PlaceFloatingPartNetworkRequest __0, ShipsServerTracker ____ships)
        {
            if (__0.SettingsId != Content.CoilId)
                return true;
            var ship = ____ships.GetShip(__0.ShipId);
            if (ship == null || !StriderRegistry.WorldReady)
                return false;
            var pose = Pose(__0.Placement);
            foreach (var part in ship.StatefulParts)
            {
                if (IsTarget(part) && ConduitPlacement.Matches(ChainAttachment(ship, part), pose))
                    return true;
            }

            return false;
        }

        public static void Clear()
        {
            active = valid = hasTarget = false;
        }
    }
}