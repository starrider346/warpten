using System.Collections.Generic;
using System;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model;
using Ships;
using WarpTen.Simulation;
using NRotation = System.Numerics.Quaternion;
using NVector = System.Numerics.Vector3;

namespace WarpTen.Runtime
{
    internal static class PlasmaConduits
    {
        private sealed class Cached : IDisposable
        {
            public PlasmaNetwork Network;
            private readonly ShipState state;
            private readonly Action<StatefulPart> changed;

            public Cached(ShipState state)
            {
                this.state = state;
                changed = _ => Network = null;
                state.StatefulPartAdded += changed;
                state.StatefulPartRemoved += changed;
            }

            public void Dispose()
            {
                state.StatefulPartAdded -= changed;
                state.StatefulPartRemoved -= changed;
            }
        }

        private static readonly Dictionary<TrackedShip, Cached> Cache = new();

        public static bool Kind(StatefulPart p, out PlasmaPartKind kind)
        {
            var id = p.Settings.id;
            kind = id == Content.CoreId ? PlasmaPartKind.Core :
                id == Content.InfuserId ? PlasmaPartKind.Infuser :
                id == Content.StraightId ? PlasmaPartKind.Straight :
                id == Content.CornerId ? PlasmaPartKind.Corner :
                id == Content.SpacerId ? PlasmaPartKind.Spacer : PlasmaPartKind.Tee;
            return Content.CoreId != 0 && (id == Content.CoreId || id == Content.InfuserId || Content.IsConduit(id));
        }

        private static NVector Position(StatefulPart p) => new(p.LocalPosition.x, p.LocalPosition.y, p.LocalPosition.z);

        private static NRotation Rotation(StatefulPart p) =>
            new(p.LocalRotation.x, p.LocalRotation.y, p.LocalRotation.z, p.LocalRotation.w);

        public static PlasmaNetwork Find(TrackedShip ship)
        {
            if (!Cache.TryGetValue(ship, out var cached))
                Cache.Add(ship, cached = new Cached(ship.State));
            var count = 0;
            var same = cached.Network != null;
            foreach (var p in ship.StatefulParts)
                if (Kind(p, out var kind))
                {
                    count++;
                    if (same && !cached.Network.Matches(p.Id, kind, Position(p), Rotation(p)))
                        same = false;
                }

            if (same && count == cached.Network.Count)
                return cached.Network;
            var nodes = new List<PlasmaNode>(count);
            foreach (var p in ship.StatefulParts)
                if (Kind(p, out var kind))
                    nodes.Add(new PlasmaNode(ship.Id, p.Id, kind, Position(p), Rotation(p)));
            return cached.Network = new PlasmaNetwork(nodes.ToArray());
        }

        public static void Forget(TrackedShip ship)
        {
            if (ship != null && Cache.TryGetValue(ship, out var c))
            {
                c.Dispose();
                Cache.Remove(ship);
            }
        }

        public static void Clear()
        {
            foreach (var c in Cache.Values)
                c.Dispose();
            Cache.Clear();
        }
    }
}