using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Services;
using HarmonyLib;
using Ponder;
using Ships.Cables;
using Ships.Cables.Model;
using Ships.Cables.VisualObjects;
using Ships.Interface.Model.Parts;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using Ships.Parts.Constant;
using Ships.Parts.Common.Gauge;
using Ships.Parts.FuelTank;
using Ships.Parts.Lever;
using Ships.Parts.MathGate;
using StellarStriderLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WarpTen.Ponder
{
    internal sealed class DriveGuidePresentation : MonoBehaviour
    {
        private GuideTimeline timeline;
        private readonly List<Action<float>> samples = new();
        private readonly List<Renderer> indicators = new();
        private readonly MaterialPropertyBlock properties = new();
        private static readonly (float At, Bounds Bounds, float Yaw, float Pitch)[] Views =
        {
            (0, new Bounds(new Vector3(0, 1.5f, 5.75f), new Vector3(4, 4, 4)), 145, 32),
            (GuideTimeline.Infusers, new Bounds(new Vector3(0, 1, 0), new Vector3(10, 3, 4)), 115, 45),
            (GuideTimeline.Coils, new Bounds(new Vector3(0, 1, -1.5f), new Vector3(10, 3, 5)), -25, 38),
            (GuideTimeline.Matrix, new Bounds(new Vector3(-.9f, 1.5f, 5.75f), new Vector3(5.3f, 3.4f, 3.5f)), 145, 30),
            (GuideTimeline.Plasma, new Bounds(new Vector3(0, .6f, 3.9f), new Vector3(3.6f, 2, 2.8f)), -25, 30),
            (GuideTimeline.Plasma + 3.5f, new Bounds(new Vector3(0, .7f, 2.7f), new Vector3(8, 2.5f, 5)), 115, 58),
            (GuideTimeline.Spacer, new Bounds(new Vector3(0, .5f, 4.125f), new Vector3(2, 1.5f, 2)), -60, 28),
            (GuideTimeline.Hydrogen, new Bounds(new Vector3(-.5f, 1, 8.75f), new Vector3(6, 4, 10.5f)), 180, 60),
            (GuideTimeline.Controls, new Bounds(new Vector3(.1f, .4f, 11.6f), new Vector3(4.4f, 1.6f, 4.6f)), 180, 70),
            (GuideTimeline.Clearance, new Bounds(new Vector3(0, 1, -1), new Vector3(10, 2.6f, 4.5f)), -25, 28),
            (GuideTimeline.Clearance + 7, new Bounds(new Vector3(0, 1.5f, 0), new Vector3(15.5f, 3.5f, 6.5f)), -40, 20),
            (GuideTimeline.Charge, new Bounds(new Vector3(0, 1, -1), new Vector3(9.5f, 2.4f, 4.2f)), -25, 28)
        };

        internal void Build(PonderScene scene, GuideTimeline timing, double hydrogenRate)
        {
            timeline = timing;
            var end = timing.Duration + 1;
            foreach (var entry in GuideLayout.Parts)
            {
                var position = new Vector3(entry.Position.X, entry.Position.Y, entry.Position.Z);
                var part = scene.AddPart(entry.Id, PartRef.Mod("warpten:" + entry.Part), position, new Vector3(0, entry.Yaw, 0));
                if (entry.Appears > 0)
                {
                    part.SetActive(false);
                    scene.Show(entry.Id, entry.Appears);
                    scene.Move(entry.Id, position + Vector3.up * .5f, position, entry.Appears, .35f);
                }
                if (entry.Part == "infuser" || entry.Part == "coil")
                {
                    scene.Show(entry.Id, GuideTimeline.Matrix, false);
                    scene.Show(entry.Id, GuideTimeline.Plasma);
                }
                scene.Show(entry.Id, GuideTimeline.Controls, false);
                if (entry.Part == "infuser" || entry.Part == "coil")
                    scene.Show(entry.Id, GuideTimeline.Clearance);
                indicators.AddRange(part.GetComponentsInChildren<Renderer>(true).Where(r =>
                    r.gameObject.name.EndsWith("__charge.obj", StringComparison.Ordinal) ||
                    r.gameObject.name.EndsWith("__status.obj", StringComparison.Ordinal)));
            }

            scene.Callout("core", new Vector3(0, 2, 0), "One Warp Core produces plasma for both nacelles.", 0, GuideTimeline.Infusers);
            scene.Highlight("core", 0, GuideTimeline.Infusers);
            scene.Callout("left_infuser", new Vector3(0, 1, 1), "Plasma enters at the front. Face both infusers the same way.", GuideTimeline.Infusers + 1, GuideTimeline.Coils);
            scene.Callout("right_infuser", new Vector3(0, 1, 0), "Keep the infusers level and in line. This example leaves six metres between them.", GuideTimeline.Infusers + 1, GuideTimeline.Coils);
            scene.Callout("left_coil_1", Vector3.up, "Snap the first coil directly behind the infuser.", GuideTimeline.Coils + 2, GuideTimeline.Matrix);
            scene.Callout("right_coil_2", Vector3.up, "Continue both rows with matching coils. Gaps stop the chain.", GuideTimeline.Coils + 2, GuideTimeline.Matrix);
            BuildMatrix(scene);
            scene.Callout("core", new Vector3(1.5f, .5f, 0), "Attach a plasma conduit to this outlet.", GuideTimeline.Plasma, GuideTimeline.Plasma + 3.5f);
            scene.Callout("split", new Vector3(0, .5f, 0), "A T sends plasma to both infusers. The final small gap is still open.", GuideTimeline.Plasma + 5, GuideTimeline.Spacer);
            scene.Callout("spacer", new Vector3(0, .25f, 0), "0.25 m spacer: attach directly to the open collar to finish this route.", GuideTimeline.Spacer + 1.5f, GuideTimeline.Hydrogen);
            scene.Highlight("spacer", GuideTimeline.Spacer + 1.5f, GuideTimeline.Hydrogen);
            BuildFuelControls(scene, hydrogenRate);
            BuildClearance(scene);
            scene.Callout("left_coil_1", Vector3.up, "Keep both infusers supplied while the coils charge.", GuideTimeline.Charge, timing.Complete);
            scene.Callout("left_infuser", new Vector3(0, 1, 1), "Warp engages automatically. The infusers face the direction of travel.", timing.Complete, end);
            scene.Highlight("left_coil_1", timing.Complete, end, Color.cyan);
            scene.Highlight("right_coil_1", timing.Complete, end, Color.cyan);
        }

        private void BuildMatrix(PonderScene scene)
        {
            if (!StriderRegistry.TryGetItem(ItemRef.Mod("warpten:annihilation_matrix_d"), out var item) || item.itemObject == null)
                throw new InvalidOperationException("Grade D Annihilation Matrix preview is unavailable.");
            var root = scene.CreateObject("matrix");
            Object.Instantiate(item.itemObject, root.transform, false).SetActive(true);
            // Enlarge and face the small inventory item toward the viewer, then shrink it on insertion.
            root.transform.localPosition = new Vector3(-2.4f, 1.7f, 5.75f);
            root.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Quaternion.Euler(30, 145, 0) * Vector3.back);
            samples.Add(seconds => root.transform.localScale = Vector3.one *
                Mathf.Lerp(5, 1, Mathf.Clamp01((seconds - GuideTimeline.Matrix - 8) / 1)));
            root.SetActive(false);
            scene.Show("matrix", GuideTimeline.Matrix);
            scene.Move("matrix", root.transform.localPosition, new Vector3(0, 1.5f, 5.75f), GuideTimeline.Matrix + 8, 1);
            scene.Show("matrix", GuideTimeline.Matrix + 9, false);
            scene.Callout("matrix", Vector3.zero, "Grade D: reusable, rated for four coils. Install in Warp Core → Matrix.", GuideTimeline.Matrix, GuideTimeline.Matrix + 8);
            scene.Callout("core", new Vector3(0, 1.5f, 0), "Open the core's Matrix tab and put the matrix in its slot.", GuideTimeline.Matrix + 8, GuideTimeline.Plasma);
        }

        private void BuildFuelControls(PonderScene scene, double hydrogenRate)
        {
            var all = new PartSettingsProvider().GetAllPartSettings();
            GameObject Native(string id, string name, Vector3 position, float appears)
            {
                var settings = all.FirstOrDefault(p => p.name == name) ??
                               throw new InvalidOperationException("Native guide prop unavailable: " + name);
                var yaw = id switch { "valve" => 180, "lever" => 90, "multiply" => 270, "constant" => 270, _ => 0 };
                var part = scene.AddPart(id, PartRef.Native(settings.id), position, new Vector3(0, yaw, 0));
                part.SetActive(false);
                scene.Show(id, appears);
                scene.Move(id, position + Vector3.up * .35f, position, appears, .35f);
                scene.Show(id, GuideTimeline.Clearance, false);
                return part;
            }
            var tank = Native("tank", "FluidTank", new Vector3(-1.125f, 0, 12.75f), GuideTimeline.Hydrogen + 1);
            var valve = Native("valve", "FluidValve", new Vector3(-1.5f, 0, 10.8f), GuideTimeline.Hydrogen + 1.5f);
            var lever = Native("lever", "Lever", new Vector3(1.6f, 0, 10.2f), GuideTimeline.Controls + 1);
            var multiply = Native("multiply", "MathGate", new Vector3(0, 0, 10.8f), GuideTimeline.Controls + 1.5f);
            var constant = Native("constant", "Constant", new Vector3(1.6f, 0, 11.4f), GuideTimeline.Controls + 2);

            if (!StriderRegistry.TryGetItem(ItemRef.Native(106), out var hydrogen) || !hydrogen.isFluid)
                throw new InvalidOperationException("Hydrogen is unavailable for the guide.");
            var tankVisual = tank.GetComponentInChildren<FluidTankVisuals>(true);
            var tankWindow = Field<Renderer>(tankVisual, "windowRenderer");
            tankWindow.sharedMaterial = scene.Own(new Material(Field<Material>(tankVisual, "liquidGlassMat")));
            var gauge = tank.GetComponentInChildren<Gauge>(true) ??
                        throw new InvalidOperationException("Hydrogen buffer dial unavailable.");
            samples.Add(seconds =>
            {
                var full = Mathf.Clamp01((seconds - GuideTimeline.Hydrogen - 2) / 6);
                gauge.UpdateGauge(full, 1);
                Liquid(tankWindow, full * 1.2f, -full * 4, hydrogen.fluidColor);
            });

            FluidPipe(scene, "tank_to_valve", scene.GetSocket("tank", 0), scene.GetSocket("valve", 2), GuideTimeline.Hydrogen + 2);
            FluidPipe(scene, "valve_to_core", scene.GetSocket("valve", 3), scene.GetSocket("core", 2), GuideTimeline.Hydrogen + 2.5f);
            scene.Show("valve_to_core", GuideTimeline.Controls, false);
            scene.Show("tank_to_valve", GuideTimeline.Clearance, false);
            scene.Callout("tank", new Vector3(0, 1, 0), "Fill with hydrogen from your ship's supply. Keep this buffer BEFORE the valve.", GuideTimeline.Hydrogen + 2, GuideTimeline.Controls);
            scene.CalloutSocket("core", 2, "Hydrogen input: socket 2. No electricity or signal input is needed on the core.", GuideTimeline.Hydrogen + 2, GuideTimeline.Controls);
            scene.HighlightSocket("core", 2, GuideTimeline.Hydrogen + 2, GuideTimeline.Controls);
            scene.AddSignalCable("lever_signal", "lever", 0, "multiply", 0, GuideTimeline.Controls + 3, .35f);
            scene.AddSignalCable("constant_signal", "constant", 0, "multiply", 1, GuideTimeline.Controls + 3.5f, .35f);
            scene.AddSignalCable("valve_signal", "multiply", 2, "valve", 0, GuideTimeline.Controls + 4, .35f);
            foreach (var id in new[] { "lever_signal", "constant_signal", "valve_signal" })
                scene.Show(id, GuideTimeline.Clearance, false);
            scene.Callout("lever", Vector3.up * .5f, "Throttle: connect this to the first input.", GuideTimeline.Controls + 1.5f, GuideTimeline.Controls + 4.5f);
            scene.Callout("constant", Vector3.up * .3f, "Fuel rate: set to " + hydrogenRate.ToString("0.###", CultureInfo.InvariantCulture) + " for these four coils and Grade D.", GuideTimeline.Controls + 2.5f, GuideTimeline.Clearance);
            scene.Callout("multiply", Vector3.up * .5f, "Choose Multiply to combine the throttle and fuel rate.", GuideTimeline.Controls + 4.5f, GuideTimeline.Controls + 8);
            scene.CalloutSocket("valve", 0, "Connect the multiplier's output to this valve input.", GuideTimeline.Controls + 8, GuideTimeline.Clearance);
            scene.HighlightSocket("valve", 0, GuideTimeline.Controls + 8, GuideTimeline.Clearance);

            var input = constant.GetComponentInChildren<ConstantVisuals>(true)?.InputField ??
                        throw new InvalidOperationException("Constant preview field unavailable.");
            var text = Field<TMPro.TMP_Text>(multiply.GetComponentInChildren<MathGateVisuals>(true), "textField");
            var leverVisual = lever.GetComponentInChildren<LeverVisuals>(true);
            var leverHandle = Field<Transform>(leverVisual, "leverObject");
            var leverAngle = (float)(AccessTools.Field(typeof(LeverVisuals), "maxAngle")?.GetValue(leverVisual) ??
                                    throw new InvalidOperationException("Throttle preview angle unavailable."));
            var handle = Field<Transform>(valve.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(c => c.GetType().Name == "FluidValveVisual"), "valveHandle");
            var rate = hydrogenRate.ToString("0.###", CultureInfo.InvariantCulture);
            samples.Add(seconds =>
            {
                input.SetTextWithoutNotify(rate);
                input.textComponent.text = rate;
                text.text = "×";
                var open = Mathf.SmoothStep(0, 1, Mathf.Clamp01((seconds - GuideTimeline.Charge) / .5f));
                leverHandle.localRotation = Quaternion.Euler(Mathf.Lerp(-leverAngle, leverAngle, open), 0, 0);
                handle.localRotation = Quaternion.AngleAxis(open * (float)hydrogenRate * 180, Vector3.up);
            });
        }

        private static T Field<T>(Component component, string name) where T : class =>
            component != null && AccessTools.Field(component.GetType(), name)?.GetValue(component) is T value
                ? value : throw new InvalidOperationException("Native preview field unavailable: " + name);

        private void FluidPipe(PonderScene scene, string id, Transform from, Transform to, float appears)
        {
            var provider = ServiceLocator.GetService<ICablesVisualsProvider>() as CablesVisuals;
            var prefab = Field<CableVisualConfig[]>(provider, "cableConfigs").First(c => c.type == CableType.Fluid).visualPrefab;
            var root = scene.CreateObject(id);
            var cable = Object.Instantiate(prefab, root.transform);
            cable.gameObject.SetActive(true);
            Field<Renderer>(cable, "idRenderer").enabled = false;
            var meshes = AccessTools.Field(typeof(CableVisual), "_cableDynamicMesh") ??
                         throw new MissingFieldException(typeof(CableVisual).FullName, "_cableDynamicMesh");
            meshes.SetValue(cable, null);
            cable.SetTransform(from.position, from.rotation, to.position, to.rotation);
            foreach (var mesh in (Mesh[])meshes.GetValue(cable) ?? Array.Empty<Mesh>())
                if (mesh != null) scene.Own(mesh);
            root.SetActive(false);
            scene.Show(id, appears);
            var covers = from.GetComponentsInChildren<Transform>(true).Concat(to.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name == "Cover").ToArray();
            var fluid = Field<Renderer>(cable, "scrollingCord");
            if (!StriderRegistry.TryGetItem(ItemRef.Native(106), out var hydrogen))
                throw new InvalidOperationException("Hydrogen is unavailable for the guide.");
            samples.Add(seconds =>
            {
                if (seconds >= appears)
                    foreach (var cover in covers) cover.gameObject.SetActive(false);
                var start = id == "valve_to_core" ? GuideTimeline.Charge : GuideTimeline.Hydrogen + 2;
                Liquid(fluid, seconds >= start ? 1 : 0, -Mathf.Max(0, seconds - start) * .3f, hydrogen.fluidColor);
            });
        }

        private void Liquid(Renderer renderer, float density, float position, Color color)
        {
            renderer.GetPropertyBlock(properties);
            properties.SetFloat("_LiquidDensity", density);
            properties.SetFloat("_LiquidPos", position);
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
        }

        private void BuildClearance(PonderScene scene)
        {
            var root = scene.CreateObject("clearance");
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ??
                         throw new InvalidOperationException("Guide clearance shader unavailable.");
            var lensMaterial = scene.Own(new Material(shader) { color = new Color(1, .85f, .2f, .16f) });
            var widthMaterial = scene.Own(new Material(shader) { color = new Color(.1f, .85f, 1, .16f) });
            void Surface(string name, Vector3[] vertices, int[] triangles, Material material)
            {
                var child = scene.CreateObject(name);
                child.transform.SetParent(root.transform, false);
                var mesh = scene.Own(new Mesh { name = name });
                mesh.vertices = vertices;
                mesh.triangles = triangles.Concat(triangles.AsEnumerable().Reverse()).ToArray();
                mesh.RecalculateBounds(); mesh.RecalculateNormals();
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                child.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            const int segments = 32;
            foreach (var sign in new[] { -1, 1 })
            {
                var vertices = new Vector3[(segments + 1) * 2];
                var triangles = new List<int>();
                for (var i = 0; i <= segments; i++)
                {
                    var x = -1.5f + 3f * i / segments;
                    var y = 1 + sign * .75f * (1 - x * x / 2.25f);
                    vertices[2 * i] = new Vector3(x, y, -3);
                    vertices[2 * i + 1] = new Vector3(x, y, -1);
                    if (i < segments) triangles.AddRange(new[] { 2 * i, 2 * i + 1, 2 * i + 2, 2 * i + 2, 2 * i + 1, 2 * i + 3 });
                }
                Surface("clearance_lens_" + sign, vertices, triangles.ToArray(), lensMaterial);
                var xLimit = sign * 7.5f;
                var id = "width_limit_" + sign;
                var corners = new[] { new Vector3(xLimit, 0, -3), new Vector3(xLimit, 3, -3),
                    new Vector3(xLimit, 3, 3), new Vector3(xLimit, 0, 3) };
                Surface(id, corners, new[] { 0, 1, 2, 0, 2, 3 }, widthMaterial);
                var plane = scene.GetObject(id);
                var outline = plane.AddComponent<LineRenderer>();
                outline.useWorldSpace = false;
                outline.loop = true;
                outline.widthMultiplier = .035f;
                outline.positionCount = corners.Length;
                outline.SetPositions(corners);
                outline.sharedMaterial = scene.Own(new Material(shader) { color = Color.cyan });
                plane.SetActive(false);
                scene.Show(id, GuideTimeline.Clearance + 7);
            }
            root.SetActive(false);
            scene.Show("clearance", GuideTimeline.Clearance);
            scene.Show("clearance", GuideTimeline.Charge, false);
            scene.Callout("left_coil_1", new Vector3(1.5f, 1, 0), "Yellow lens: keep this space clear of hull and machinery. Pipes and cables are allowed.", GuideTimeline.Clearance, GuideTimeline.Clearance + 7);
            scene.Callout("width_limit_1", new Vector3(7.5f, 1.5f, 0), "Cyan planes: the whole ship must fit between these width limits.", GuideTimeline.Clearance + 7, GuideTimeline.Charge);
        }

        internal void Sample(PonderScene scene, float seconds)
        {
            foreach (var sample in samples) sample(seconds);
            var charge = timeline.ChargeAt(seconds);
            foreach (var renderer in indicators)
            {
                renderer.GetPropertyBlock(properties);
                var color = Color.Lerp(new Color(.4f, .43f, .46f), Color.cyan, charge);
                properties.SetColor("_BaseColor", color);
                properties.SetColor("_Color", color);
                properties.SetColor("_EmissionColor", Color.cyan * (6 * charge));
                renderer.SetPropertyBlock(properties);
            }
            var index = 0;
            while (index + 1 < Views.Length && seconds >= Views[index + 1].At) index++;
            var from = Views[Math.Max(0, index - 1)];
            var to = Views[index];
            var phase = Mathf.SmoothStep(0, 1, Mathf.Clamp01((seconds - to.At) / .9f));
            scene.SetCameraFrame(new Bounds(Vector3.Lerp(from.Bounds.center, to.Bounds.center, phase),
                    Vector3.Lerp(from.Bounds.size, to.Bounds.size, phase)),
                Mathf.LerpAngle(from.Yaw, to.Yaw, phase), Mathf.Lerp(from.Pitch, to.Pitch, phase), 1.25f);
        }
    }
}
