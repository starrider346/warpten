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
    internal static class ConduitBuildTool
    {
        private const float Range = 5, AimRadius = .19f;
        private static ushort selected;
        private static int turns;
        private static bool valid, active, hasTarget;
        private static Quaternion previewRotation;
        private static float lastDetection;
        private static CameraPhysicsData lastCamera;

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
                prefix: new HarmonyMethod(typeof(ConduitBuildTool), nameof(Detect)));
            harmony.Patch(Method(typeof(PartPreview), "Rotate"),
                prefix: new HarmonyMethod(typeof(ConduitBuildTool), nameof(Rotate)));
            harmony.Patch(Method(typeof(PartPreview), "UpdatePlacement"),
                postfix: new HarmonyMethod(typeof(ConduitBuildTool), nameof(PreviewUpdated)));
            harmony.Patch(Method(typeof(PartPreview), "GetPlacement"),
                prefix: new HarmonyMethod(typeof(ConduitBuildTool), nameof(GetPlacement)));
            harmony.Patch(Method(typeof(PartPreview), "OnPartUnselected"),
                postfix: new HarmonyMethod(typeof(ConduitBuildTool), nameof(Clear)));
            var logic = GetDeclaredMethods(typeof(ShipInteractionService))
                .Single(m => m.Name.StartsWith("RpcLogic___PlaceFloatingPart_ServerRPC___", StringComparison.Ordinal));
            harmony.Patch(logic, prefix: new HarmonyMethod(typeof(ConduitBuildTool), nameof(ValidateRequest)));
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

        private static Vector3 PortPosition(StatefulPart part, PlasmaPort port) =>
            part.LocalPosition + part.LocalRotation * V(port.Position);

        private static bool Detect(SnapDetectionState __0, CameraPhysicsData __1, ref SnapDetectionResults __result)
        {
            var settings = __0.PartSettings;
            if (settings == null || !Content.IsConduit(settings.id) || __0.IgnoredPart.HasValue)
                return true;
            if (selected != settings.id)
            {
                selected = settings.id;
                turns = 0;
            }

            active = true;
            valid = false;
            hasTarget = false;
            lastDetection = Time.unscaledTime;
            lastCamera = __1;
            __result = new SnapDetectionResults
            {
                pos = __1.CameraPos + __1.Direction * 2.5f
            };
            if (!StriderRegistry.WorldReady)
                return false;
            var ships = GetService<ShipsClientTracker>();
            if (ships == null)
                return false;
            var best = float.MaxValue;
            TrackedShipClient targetShip = null;
            IShipPartPlacements targetProxy = null;
            StatefulPart targetPart = null;
            PlasmaPort targetPort = default;
            foreach (var ship in ships.Ships)
            {
                if (ship.IsShipDestroyed || !ship.IsVisualLoaded || !ship.IsRigidbodyLoaded)
                    continue;
                var proxy = ship.GetPartPlacements();
                if (proxy == null)
                    continue;
                var network = PlasmaConduits.Find(ship);
                foreach (var part in ship.StatefulParts)
                {
                    if (!PlasmaConduits.Kind(part, out var kind))
                        continue;
                    var rotation = proxy.ToWorldRotation(part.LocalRotation);
                    var root = proxy.ToWorldPos(part.Placement);
                    var ports = PlasmaNetwork.Ports(kind);
                    for (var i = 0; i < ports.Count; i++)
                    {
                        if (!network.IsPortOpen(part.Id, i))
                            continue;
                        var port = ports[i];
                        var world = root + rotation * V(port.Position);
                        var delta = world - __1.CameraPos;
                        var along = Vector3.Dot(delta, __1.Direction);
                        if (along <= .05f || delta.sqrMagnitude > Range * Range)
                            continue;
                        var off = (delta - __1.Direction * along).sqrMagnitude;
                        if (off > AimRadius * AimRadius)
                            continue;
                        var score = off / (along * along);
                        if (score >= best)
                            continue;
                        if (ClientScene.PhysicsScene.Raycast(__1.CameraPos, delta.normalized, out var hit,
                                Mathf.Max(0, delta.magnitude - .02f), BuildDetectionLayer,
                                QueryTriggerInteraction.Ignore) &&
                            (!ShipPartsTagDetection.ColliderIsPart(hit.collider, new PartContext(ship.Id, part.Id)) ||
                             (hit.point - world).sqrMagnitude > .30f * .30f))
                            continue;
                        best = score;
                        targetShip = ship;
                        targetProxy = proxy;
                        targetPart = part;
                        targetPort = port;
                    }
                }
            }

            if (targetShip == null)
                return false;
            var point = PortPosition(targetPart, targetPort);
            var direction = targetPart.LocalRotation * V(targetPort.Normal);
            var up = targetPart.LocalRotation * Vector3.up;
            var piece = Content.ConduitKind(settings.id);
            if (!ConduitPlacement.TryAttach(piece, V(point), V(direction), V(up), turns, out var solution))
                return false;
            var placement = NativePose(solution);
            var representable = ConduitPlacement.Matches(solution, Pose(placement)) &&
                                ConduitPlacement.IsAttached(piece, Pose(placement), V(point), V(direction));
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

        private static bool Rotate(PartPreview __instance, float __0, PartSettings ____actualPreviewSettings)
        {
            if (____actualPreviewSettings == null || !Content.IsConduit(____actualPreviewSettings.id))
                return true;
            if (selected != ____actualPreviewSettings.id)
            {
                selected = ____actualPreviewSettings.id;
                turns = 0;
            }

            if (__0 == 0 || !Application.isFocused)
                return false;
            turns = (turns + (__0 > 0 ? 1 : 3)) % 4;
            if (active && Time.unscaledTime - lastDetection < .25f)
                __instance.UpdatePhysics(lastCamera);
            return false;
        }

        private static void PreviewUpdated(PartPreview __instance, PartSettings ____actualPreviewSettings,
            ref Vector3 ____currentPreviewPos, Vector3 ____previewTargetPos, GameObject ____objectPreview)
        {
            if (____actualPreviewSettings == null || !Content.IsConduit(____actualPreviewSettings.id))
                return;
            ____currentPreviewPos = ____previewTargetPos;
            if (hasTarget && ____objectPreview != null)
                ____objectPreview.transform.rotation = previewRotation;
            if (!valid)
                Invalid(__instance);
        }

        private static bool GetPlacement(PartSettings ____actualPreviewSettings, ref PreviewPlacement __result)
        {
            if (____actualPreviewSettings == null || !Content.IsConduit(____actualPreviewSettings.id))
                return true;
            if (active && valid && Time.unscaledTime - lastDetection < .25f)
                return true;
            __result = null;
            return false;
        }

        private static bool ValidateRequest(PlaceFloatingPartNetworkRequest __0, ShipsServerTracker ____ships)
        {
            if (!Content.IsConduit(__0.SettingsId))
                return true;
            var ship = ____ships.GetShip(__0.ShipId);
            if (ship == null || !StriderRegistry.WorldReady)
                return false;
            var pose = Pose(__0.Placement);
            var kind = Content.ConduitKind(__0.SettingsId);
            var network = PlasmaConduits.Find(ship);
            foreach (var part in ship.StatefulParts)
            {
                if (!PlasmaConduits.Kind(part, out var targetKind))
                    continue;
                var ports = PlasmaNetwork.Ports(targetKind);
                for (var i = 0; i < ports.Count; i++)
                {
                    if (!network.IsPortOpen(part.Id, i))
                        continue;
                    var point = V(PortPosition(part, ports[i]));
                    var normal = V(part.LocalRotation * V(ports[i].Normal));
                    if (!ConduitPlacement.IsAttached(kind, pose, point, normal))
                        continue;
                    for (var roll = 0; roll < 4; roll++)
                        if (ConduitPlacement.TryAttach(kind, point, normal, V(part.LocalRotation * Vector3.up), roll,
                                out var expected) && ConduitPlacement.Matches(expected, pose))
                            return true;
                }
            }

            return false;
        }

        public static void Clear()
        {
            active = valid = hasTarget = false;
            selected = 0;
            turns = 0;
        }
    }
}