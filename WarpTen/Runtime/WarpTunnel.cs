using System.Collections.Generic;
using System;
using HarmonyLib;
using MelonLoader;
using Players.Interface.Services;
using Ships.Services;
using Ships.Visuals.VisualEffects;
using Ships;
using StellarStriderLib;
using UnityEngine.Rendering;
using UnityEngine;
using WarpTen.Simulation;
using WorldTracking.Interface.Model.Spaces;
using WorldTracking.Interface.Services;
using static System.Math;
using static Core.Services.ServiceLocator;
using static HarmonyLib.AccessTools;
using static UnityEngine.Object;
using HarmonyEngine = HarmonyLib.Harmony;

namespace WarpTen.Runtime
{
    internal static class WarpTunnel
    {
        private struct Bubble
        {
            public uint Ship;
            public Renderer Renderer;
            public bool Previous;
            public bool Suppressed;
        }

        private static readonly List<Bubble> Bubbles = new();
        private static readonly TunnelTransition Transition = new();
        private static readonly JumpTransition Jump = new();
        private static readonly int JumpParameters = Shader.PropertyToID("_JumpParams");
        private static uint chargingShip;
        private static ushort chargingCore;

        private static readonly int Forward = Shader.PropertyToID("_TravelForward"),
            Right = Shader.PropertyToID("_TravelRight"),
            Up = Shader.PropertyToID("_TravelUp"),
            Parameters = Shader.PropertyToID("_TunnelParams");

        private static AssetBundle bundle;
        private static Material material;
        private static GameObject shell;
        private static MeshRenderer renderer;
        private static MaterialPropertyBlock properties;
        private static Camera camera;
        private static Vector3 direction = Vector3.forward;
        private static float speed;
        private static uint source, warpId;
        private static bool failed;

        public static void Install(HarmonyEngine harmony)
        {
            harmony.Patch(Method(typeof(ShipWarpBubbleEffectAnimator), "SetupWarpBubbleObject"),
                postfix: new HarmonyMethod(typeof(WarpTunnel), nameof(BubbleCreated)));
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        }

        private static void BubbleCreated(TrackedShipClient __1, GameObject __result)
        {
            if (__result == null)
                return;
            var r = __result.GetComponentInChildren<MeshRenderer>();
            if (r != null)
                Bubbles.Add(new Bubble { Ship = __1.Id, Renderer = r });
        }

        private static void Suppress(uint ship)
        {
            for (var i = Bubbles.Count - 1; i >= 0; i--)
            {
                var b = Bubbles[i];
                if (b.Renderer == null)
                {
                    Bubbles.RemoveAt(i);
                    continue;
                }

                var hide = ship != 0 && b.Ship == ship;
                if (hide && !b.Suppressed)
                {
                    b.Previous = b.Renderer.forceRenderingOff;
                    b.Suppressed = true;
                }

                if (b.Suppressed)
                    b.Renderer.forceRenderingOff = hide || b.Previous;
                if (!hide)
                    b.Suppressed = false;
                Bubbles[i] = b;
            }
        }

        private struct ClientSpaces : ITunnelSpaces
        {
            public ISpacesClientProvider Spaces;
            public ShipsClientTracker Ships;

            public bool Describe(uint id, out uint parent, out bool warp, out bool warpTen)
            {
                parent = 0;
                warp = warpTen = false;
                if (!Spaces.TryGetSpace(id, out var space))
                    return false;
                if (space is WarpSpace w)
                {
                    warp = true;
                    warpTen = Ships.TryGetTrackedShip(w.SourceShipId, out var owner) &&
                              owner.TryGetStatefulPart(w.SourcePartId, out var part) && Content.IsCore(part);
                    return true;
                }

                if (space is not ShipRelativeSpace relative || !Ships.TryGetTrackedShip(relative.ShipId, out var ship))
                    return false;
                parent = ship.SpaceId;
                return true;
            }
        }

        private static bool TryCharging(ISpacesClientProvider spaces, ShipsClientTracker ships, uint local,
            out float remaining)
        {
            remaining = 0;
            for (var depth = 0; depth < 64; depth++)
            {
                if (!spaces.TryGetSpace(local, out var space) || space is not ShipRelativeSpace relative ||
                    !ships.TryGetTrackedShip(relative.ShipId, out var ship))
                    return false;
                foreach (var part in ship.StatefulParts)
                {
                    if (!Content.IsCore(part) || !CoreInventory.TryCharging(ship.Id, part.Id, out float countdown) ||
                        countdown >= JumpTransition.LeadSeconds)
                        continue;
                    if (chargingShip != ship.Id || chargingCore != part.Id)
                        Jump.Reset();
                    chargingShip = ship.Id;
                    chargingCore = part.Id;
                    direction = ship.VisualRotation * (NacelleGeometry.Find(ship).FrameRotation * Vector3.forward);
                    remaining = countdown;
                    return true;
                }

                if (local == ship.SpaceId)
                    return false;
                local = ship.SpaceId;
            }

            return false;
        }

        public static void LateUpdate()
        {
            if (!StriderRegistry.WorldReady)
            {
                if (shell != null || Bubbles.Count != 0)
                    Clear();
                return;
            }

            try
            {
                camera = Camera.main;
                var spaces = GetService<ISpacesClientProvider>();
                var ships = GetService<ShipsClientTracker>();
                var local = GetService<ILocalPlayerProvider>()?.LocalPlayerPhysicsStateSpaceId;
                var resolved = 0u;
                var membership = new ClientSpaces
                {
                    Spaces = spaces,
                    Ships = ships
                };
                var warp = spaces != null && ships != null && local.HasValue &&
                           TunnelMembership.TryResolve(membership, local.Value, out resolved)
                    ? spaces.GetSpace(resolved) as WarpSpace
                    : null;
                var eligible = camera != null && warp != null &&
                               ships.TryGetTrackedShip(warp.SourceShipId, out var ship) &&
                               ship.TryGetStatefulPart(warp.SourcePartId, out var core) && Content.IsCore(core);
                var next = eligible ? warp.SourceShipId : 0;
                var remaining = 0f;
                var charging = !eligible && camera != null && spaces != null && ships != null && local.HasValue &&
                               TryCharging(spaces, ships, local.Value, out remaining);
                if (eligible && source != next && (chargingShip != next || chargingCore != warp.SourcePartId))
                    Jump.Reset();
                if ((eligible || charging) && !Initialize())
                {
                    eligible = charging = false;
                }

                Jump.Step(eligible, charging, remaining, Time.unscaledDeltaTime);
                if (!charging)
                {
                    chargingShip = 0;
                    chargingCore = 0;
                }

                if (eligible)
                {
                    ships.TryGetTrackedShip(next, out var owner);
                    if (source != next || warpId != resolved)
                    {
                        Transition.Reset();
                        speed = 0;
                    }

                    source = next;
                    warpId = resolved;
                    direction = owner.VisualRotation * (NacelleGeometry.Find(owner).FrameRotation * Vector3.forward);
                    var magnitude = spaces.GetWarpSpaceVelocity(resolved).Magnitude;
                    if (Tuning.Finite(magnitude) && magnitude >= 0)
                    {
                        var blend = 1 - Mathf.Exp(-Time.unscaledDeltaTime / .2f);
                        speed = Mathf.Lerp(speed, (float)Min(1, magnitude / DriveRuntime.Tuning.MaximumSpeed),
                            blend);
                    }

                    if (!Initialize())
                        eligible = false;
                }

                Transition.Step(eligible, Time.unscaledDeltaTime, speed);
                if (!eligible)
                {
                    source = 0;
                    warpId = 0;
                }

                if (renderer != null)
                {
                    renderer.enabled = (Transition.Intensity > 0 || Jump.Flash > 0) && camera != null;
                    var right = Vector3.Cross(Mathf.Abs(direction.y) > .98f ? Vector3.forward : Vector3.up, direction)
                        .normalized;
                    properties.SetVector(Forward, direction);
                    properties.SetVector(Right, right);
                    properties.SetVector(Up, Vector3.Cross(direction, right));
                    properties.SetVector(Parameters, new Vector4(Transition.Phase, speed, Transition.Intensity, 0));
                    properties.SetVector(JumpParameters, new Vector4(0, Jump.Flash, 0, 0));
                    renderer.SetPropertyBlock(properties);
                }

                Suppress(eligible && renderer != null && renderer.enabled ? next : 0);
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private static bool Initialize()
        {
            if (failed)
                return false;
            if (material != null)
                return true;
            try
            {
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                    throw new NotSupportedException("WarpTen tunnel bundle requires Direct3D 11.");
                using var stream =
                    typeof(WarpTunnel).Assembly.GetManifestResourceStream("WarpTen.Visuals.warpten-tunnel");
                if (stream == null)
                    throw new InvalidOperationException("Compiled tunnel bundle is missing.");
                var bytes = new byte[stream.Length];
                int read = 0, count;
                while (read < bytes.Length && (count = stream.Read(bytes, read, bytes.Length - read)) > 0)
                    read += count;
                if (read != bytes.Length)
                    throw new InvalidOperationException("Truncated tunnel bundle.");
                bundle = AssetBundle.LoadFromMemory(bytes);
                if (bundle == null)
                    throw new InvalidOperationException("Cannot load tunnel AssetBundle.");
                var template = bundle.LoadAsset<Material>("assets/warpten/tunnel.mat");
                if (template == null || template.shader == null || !template.shader.isSupported)
                    throw new InvalidOperationException("Tunnel material/shader unavailable.");
                material = new Material(template)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shell.name = "WarpTen procedural tunnel";
                shell.hideFlags = HideFlags.HideAndDontSave;
                Destroy(shell.GetComponent<Collider>());
                renderer = shell.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                properties = new MaterialPropertyBlock();
                renderer.enabled = false;
                return true;
            }
            catch (Exception error)
            {
                Fail(error);
                return false;
            }
        }

        private static void BeforeCamera(ScriptableRenderContext context, Camera active)
        {
            if (renderer == null)
                return;
            renderer.forceRenderingOff = active != camera;
            if (active != camera)
                return;
            shell.transform.position = active.transform.position;
            shell.transform.localScale = Vector3.one * (active.farClipPlane * .98f * 2);
            for (var layer = 0; layer < 32; layer++)
                if ((active.cullingMask & 1 << layer) != 0)
                {
                    shell.layer = layer;
                    break;
                }
        }

        private static void Fail(Exception error)
        {
            Suppress(0);
            Release();
            Transition.Reset();
            Jump.Reset();
            chargingShip = 0;
            chargingCore = 0;
            source = warpId = 0;
            if (!failed)
                MelonLogger.Error("WarpTen tunnel disabled; native bubble restored: " + error.Message);
            failed = true;
        }

        private static void Release()
        {
            if (shell != null)
                Destroy(shell);
            if (material != null)
                Destroy(material);
            if (bundle != null)
                bundle.Unload(true);
            shell = null;
            material = null;
            renderer = null;
            bundle = null;
            properties = null;
        }

        public static void Clear()
        {
            Suppress(0);
            Bubbles.Clear();
            Release();
            Transition.Reset();
            Jump.Reset();
            chargingShip = 0;
            chargingCore = 0;
            camera = null;
            source = warpId = 0;
            speed = 0;
        }

        public static void Shutdown()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            Clear();
        }
    }
}