using System.Collections.Generic;
using System.Linq;
using System;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model;
using Ships.Interface.Settings;
using Ships;
using UnityEngine;
using WarpTen.Simulation;
using static System.Math;
using NRotation = System.Numerics.Quaternion;
using NVector = System.Numerics.Vector3;

namespace WarpTen.Runtime
{
    internal sealed class NacelleGeometry
    {
        public string Blocker;
        public readonly List<StatefulPart> LeftCoils = new(), RightCoils = new();
        public readonly List<(StatefulPart Left, StatefulPart Right)> Pairs = new();
        public NacelleClearance Clearance;
        public Vector3 FramePosition, FrameMin, FrameMax;
        public Quaternion FrameRotation;
        public float LeftOverhang, RightOverhang;
        public int ClearanceBlocks;

        public bool WidthValid => LeftOverhang <= NacelleClearance.MaximumOutboardBlocks + 1e-5f &&
                                  RightOverhang <= NacelleClearance.MaximumOutboardBlocks + 1e-5f;

        public double Strength(Tuning t) => Pairs.Count * t.PairStrength;
        private static readonly Dictionary<TrackedShip, Cached> Cache = new();
        private static readonly Dictionary<GameObject, SolidBox[]> Templates = new();

        private sealed class Cached : IDisposable
        {
            public bool Dirty = true;
            public NacelleGeometry Value;
            private readonly ShipState state;
            private readonly Action<HullPart> hullChanged;
            private readonly Action<HullPartUpdateEvent> hullUpdated;
            private readonly Action<StatefulPart> partChanged;

            public Cached(ShipState state)
            {
                this.state = state;
                hullChanged = _ => Dirty = true;
                hullUpdated = _ => Dirty = true;
                partChanged = _ => Dirty = true;
                state.HullPartAdded += hullChanged;
                state.HullPartRemoved += hullChanged;
                state.HullPartUpdated += hullUpdated;
                state.StatefulPartAdded += partChanged;
                state.StatefulPartRemoved += partChanged;
            }

            public void Dispose()
            {
                state.HullPartAdded -= hullChanged;
                state.HullPartRemoved -= hullChanged;
                state.HullPartUpdated -= hullUpdated;
                state.StatefulPartAdded -= partChanged;
                state.StatefulPartRemoved -= partChanged;
            }
        }

        public static NacelleGeometry Find(TrackedShip ship)
        {
            if (!Cache.TryGetValue(ship, out var cached))
                Cache.Add(ship, cached = new Cached(ship.State));
            if (cached.Dirty)
            {
                cached.Value = Calculate(ship);
                cached.Dirty = false;
            }

            return cached.Value;
        }

        public static void Forget(TrackedShip ship)
        {
            if (ship != null && Cache.TryGetValue(ship, out var cached))
            {
                cached.Dispose();
                Cache.Remove(ship);
            }
        }

        public static void Clear()
        {
            foreach (var cached in Cache.Values)
                cached.Dispose();
            Cache.Clear();
            Templates.Clear();
        }

        private static NacelleGeometry Calculate(TrackedShip ship)
        {
            var result = new NacelleGeometry();
            var parts = ship.StatefulParts.ToArray();
            var infusers = parts.Where(p => p.Settings.id == Content.InfuserId).OrderBy(p => p.Id).ToArray();
            if (parts.Count(Content.IsCore) != 1 || infusers.Length != 2)
            {
                result.Blocker = "Requires one core and two infusers";
                return result;
            }

            if (parts.Any(p => !Content.IsCore(p) && p.State is WarpDriveState))
            {
                result.Blocker = "Remove other warp drives";
                return result;
            }

            var a = infusers[0];
            var b = infusers[1];
            if (!NacelleLayout.Aligned(Node(a), Node(b)))
            {
                result.Blocker =
                    "Infusers must face the same way and be side by side at the same height and fore/aft position, at least 3m apart";
                return result;
            }

            var left = Chain(a, parts);
            var right = Chain(b, parts);
            result.LeftCoils.AddRange(left);
            result.RightCoils.AddRange(right);
            for (var i = 0; i < Min(left.Count, right.Count); i++)
                result.Pairs.Add((left[i], right[i]));
            result.FrameRotation = a.LocalRotation;
            result.FramePosition = (a.LocalPosition + b.LocalPosition) / 2 +
                                   a.LocalRotation * new Vector3(0, NacelleLayout.Height / 2, 0);
            var inverse = Quaternion.Inverse(result.FrameRotation);
            var separation = Abs((inverse * (b.LocalPosition - a.LocalPosition)).x);
            var minZ = -NacelleLayout.InfuserLength / 2;
            var maxZ = minZ;
            foreach (var coil in left.Concat(right))
            {
                var z = (inverse * (coil.LocalPosition - result.FramePosition)).z;
                minZ = Min(minZ, z - NacelleLayout.CoilLength / 2);
                maxZ = Max(maxZ, z + NacelleLayout.CoilLength / 2);
            }

            var clearance = result.Clearance = new NacelleClearance(separation, minZ, maxZ);
            var origin = Vec(result.FramePosition);
            var rotation = Rot(result.FrameRotation);
            var nacelles = new HashSet<ushort>(left.Concat(right).Concat(infusers).Select(p => p.Id));
            var min = new NVector(-clearance.HalfGap - NacelleLayout.Width, -clearance.PeakHeight,
                minZ);
            var max = new NVector(clearance.HalfGap + NacelleLayout.Width, clearance.PeakHeight, maxZ);

            void Measure(IEnumerable<SolidBox> solids, bool excludeFromClearance)
            {
                var blocked = false;
                foreach (var worldBox in solids)
                {
                    var box = worldBox.InFrame(origin, rotation);
                    box.Project(NVector.UnitX, out float minX, out float maxX);
                    box.Project(NVector.UnitY, out float minY, out float maxY);
                    box.Project(NVector.UnitZ, out float boxMinZ, out float boxMaxZ);
                    min = NVector.Min(min, new NVector(minX, minY, boxMinZ));
                    max = NVector.Max(max, new NVector(maxX, maxY, boxMaxZ));
                    if (!excludeFromClearance && !blocked && clearance.Intersects(box))
                        blocked = true;
                }

                if (blocked)
                    result.ClearanceBlocks++;
            }

            foreach (var part in parts)
                Measure(Solids(part.Settings, part.LocalPosition, part.LocalRotation), nacelles.Contains(part.Id));
            foreach (var hull in ship.State.GetHullPartObjects())
                Measure(Solids(hull.Settings, hull.HullPlacement.LocalPosition, hull.HullPlacement.Rotation), false);
            result.FrameMin = new Vector3(min.X, min.Y, min.Z);
            result.FrameMax = new Vector3(max.X, max.Y, max.Z);
            result.LeftOverhang = Max(0, -clearance.HalfGap - NacelleLayout.Width - min.X);
            result.RightOverhang = Max(0, max.X - clearance.HalfGap - NacelleLayout.Width);
            var blockers = new List<string>();
            if (result.Pairs.Count == 0)
                blockers.Add("No aligned contiguous coil pairs");
            if (!result.WidthValid)
                blockers.Add(
                    $"Nacelles too far inboard: outboard hull L {result.LeftOverhang:F2}m, R {result.RightOverhang:F2}m (max 3m)");
            if (result.ClearanceBlocks > 0)
                blockers.Add($"Nacelle clearance obstructed by {result.ClearanceBlocks} block(s)");
            result.Blocker = blockers.Count > 0 ? string.Join("; ", blockers) : null;
            return result;
        }

        private static NVector Vec(Vector3 v) => new(v.x, v.y, v.z);
        private static NRotation Rot(Quaternion q) => new(q.x, q.y, q.z, q.w);
        private static CoilNode Node(StatefulPart p) => new(p.Id, Vec(p.LocalPosition), Rot(p.LocalRotation));

        public static InfuserFeed Feed(TrackedShip ship, StatefulPart infuser)
        {
            var coils = ship.StatefulParts.Where(p => p.Settings.id == Content.CoilId).Select(Node).ToArray();
            return InfuserFeed.Evaluate(Node(infuser), coils);
        }

        private static List<StatefulPart> Chain(StatefulPart infuser, StatefulPart[] parts)
        {
            var coils = parts.Where(p => p.Settings.id == Content.CoilId).ToArray();
            return NacelleLayout.Chain(Node(infuser), coils.Select(Node).ToArray())
                .Select(id => coils.Single(p => p.Id == id)).ToList();
        }

        private static IEnumerable<SolidBox> Solids(PartSettings settings, Vector3 position, Quaternion rotation)
        {
            var prefab = settings.part;
            SolidBox[] templates;
            if (settings is HullPartSettings hull && !hull.hasItsOwnMesh)
            {
                var bounds = hull.Bounds;
                templates = new[]
                {
                    new SolidBox(Vec(bounds.center), Vec(bounds.extents), NRotation.Identity)
                };
            }
            else if (prefab == null)
                yield break;
            else if (!Templates.TryGetValue(prefab, out templates))
            {
                templates = Capture(prefab);
                if (templates.Length == 0)
                {
                    var bounds = settings.Bounds;
                    templates = new[]
                    {
                        new SolidBox(Vec(bounds.center), Vec(bounds.extents), NRotation.Identity)
                    };
                }

                Templates.Add(prefab, templates);
            }

            foreach (var box in templates)
                yield return new SolidBox(Vec(position) + NVector.Transform(box.Center, Rot(rotation)),
                    box.HalfSize, Rot(rotation) * box.Rotation);
        }

        private static SolidBox[] Capture(GameObject prefab)
        {
            var solids = new List<SolidBox>();
            foreach (var collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger || !collider.enabled || collider.GetComponentInParent<SocketObject>() != null)
                    continue;
                Bounds bounds;
                if (collider is BoxCollider box)
                    bounds = new Bounds(box.center, box.size);
                else if (collider is MeshCollider mesh && mesh.sharedMesh != null)
                    bounds = mesh.sharedMesh.bounds;
                else if (collider is SphereCollider sphere)
                    bounds = new Bounds(sphere.center, Vector3.one * sphere.radius * 2);
                else if (collider is CapsuleCollider capsule)
                {
                    var size = Vector3.one * capsule.radius * 2;
                    size[capsule.direction] = capsule.height;
                    bounds = new Bounds(capsule.center, size);
                }
                else
                    continue;

                var centre = prefab.transform.InverseTransformPoint(collider.transform.TransformPoint(bounds.center));
                var localRotation = Quaternion.Inverse(prefab.transform.rotation) * collider.transform.rotation;
                var scale = collider.transform.lossyScale;
                var rootScale = prefab.transform.lossyScale;
                var half = Vector3.Scale(bounds.extents,
                    new Vector3(Abs(scale.x / rootScale.x), Abs(scale.y / rootScale.y),
                        Abs(scale.z / rootScale.z)));
                solids.Add(new SolidBox(Vec(centre), Vec(half), Rot(localRotation)));
            }

            return solids.ToArray();
        }
    }
}