using System.Collections.Generic;
using System.Numerics;
using System;
using static System.Math;

namespace WarpTen.Simulation
{
    public enum PlasmaPartKind
    {
        Core,
        Infuser,
        Straight,
        Corner,
        Tee,
        Spacer
    }

    public readonly struct PlasmaPort
    {
        public readonly Vector3 Position, Normal;

        public PlasmaPort(Vector3 position, Vector3 normal)
        {
            Position = position;
            Normal = normal;
        }
    }

    public readonly struct PlasmaNode
    {
        public readonly uint Ship;
        public readonly ushort Id;
        public readonly PlasmaPartKind Kind;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public PlasmaNode(uint ship, ushort id, PlasmaPartKind kind, Vector3 position, Quaternion rotation)
        {
            Ship = ship;
            Id = id;
            Kind = kind;
            Position = position;
            Rotation = rotation;
        }
    }

    public sealed class PlasmaNetwork
    {
        public const float PositionTolerance = .01f;
        public const float NormalDotLimit = -.99999848f;
        private readonly PlasmaNode[] nodes;
        private readonly Dictionary<ushort, int> indices = new();
        private readonly int[] components;
        private readonly HashSet<(ushort, int)> occupied = new();
        public int AmbiguousPorts { get; private set; }

        private static readonly PlasmaPort[] Core =
        {
            new(new Vector3(1.5f, .5f, 0), Vector3.UnitX)
        };

        private static readonly PlasmaPort[] Infuser =
        {
            new(new Vector3(0, .5f, 1), Vector3.UnitZ)
        };

        private static readonly PlasmaPort[] Straight =
        {
            Face(-Vector3.UnitZ),
            Face(Vector3.UnitZ)
        };

        private static readonly PlasmaPort[] Corner =
        {
            Face(-Vector3.UnitZ),
            Face(Vector3.UnitX)
        };

        private static readonly PlasmaPort[] Tee =
        {
            Face(-Vector3.UnitZ),
            Face(Vector3.UnitZ),
            Face(Vector3.UnitX)
        };

        public const float SpacerLength = .25f;

        private static readonly PlasmaPort[] Spacer =
        {
            new(new Vector3(0, .25f, -SpacerLength / 2), -Vector3.UnitZ),
            new(new Vector3(0, .25f, SpacerLength / 2), Vector3.UnitZ)
        };

        public static bool IsConduit(PlasmaPartKind kind) =>
            kind >= PlasmaPartKind.Straight && kind <= PlasmaPartKind.Spacer;

        private static PlasmaPort Face(Vector3 normal) => new(new Vector3(0, .5f, 0) + normal * .5f, normal);

        public static IReadOnlyList<PlasmaPort> Ports(PlasmaPartKind kind) => kind switch
        {
            PlasmaPartKind.Core => Core,
            PlasmaPartKind.Infuser => Infuser,
            PlasmaPartKind.Straight => Straight,
            PlasmaPartKind.Corner => Corner,
            PlasmaPartKind.Tee => Tee,
            PlasmaPartKind.Spacer => Spacer,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        private readonly struct Endpoint
        {
            public readonly int Node, Port;
            public readonly Vector3 Position, Normal;

            public Endpoint(int node, int port, Vector3 position, Vector3 normal)
            {
                Node = node;
                Port = port;
                Position = position;
                Normal = normal;
            }
        }

        private static (int, int, int) Cell(Vector3 p) => ((int)Floor(p.X / PositionTolerance),
            (int)Floor(p.Y / PositionTolerance), (int)Floor(p.Z / PositionTolerance));

        public PlasmaNetwork(PlasmaNode[] nodes)
        {
            this.nodes = nodes;
            components = new int[nodes.Length];
            var endpoints = new List<Endpoint>();
            var buckets = new Dictionary<(int, int, int), List<int>>();
            for (var i = 0; i < nodes.Length; i++)
            {
                indices.Add(nodes[i].Id, i);
                components[i] = i;
                for (var portIndex = 0; portIndex < Ports(nodes[i].Kind).Count; portIndex++)
                {
                    var port = Ports(nodes[i].Kind)[portIndex];
                    var p = nodes[i].Position + Vector3.Transform(port.Position, nodes[i].Rotation);
                    var e = new Endpoint(i, portIndex, p, Vector3.Transform(port.Normal, nodes[i].Rotation));
                    var key = Cell(p);
                    if (!buckets.TryGetValue(key, out var bucket))
                        buckets.Add(key, bucket = new List<int>());
                    bucket.Add(endpoints.Count);
                    endpoints.Add(e);
                }
            }

            var matches = new int[endpoints.Count];
            var counts = new int[endpoints.Count];
            for (var i = 0; i < endpoints.Count; i++)
            {
                var e = endpoints[i];
                var cell = Cell(e.Position);
                for (var x = -1; x <= 1; x++)
                for (var y = -1; y <= 1; y++)
                for (var z = -1; z <= 1; z++)
                {
                    if (!buckets.TryGetValue((cell.Item1 + x, cell.Item2 + y, cell.Item3 + z), out var bucket))
                        continue;
                    foreach (var j in bucket)
                    {
                        var other = endpoints[j];
                        if (e.Node == other.Node || nodes[e.Node].Ship != nodes[other.Node].Ship ||
                            Vector3.DistanceSquared(e.Position, other.Position) >
                            PositionTolerance * PositionTolerance ||
                            Vector3.Dot(e.Normal, other.Normal) > NormalDotLimit)
                            continue;
                        matches[i] = j;
                        counts[i]++;
                    }
                }

                if (counts[i] > 1)
                    AmbiguousPorts++;
                if (counts[i] > 0)
                    occupied.Add((nodes[e.Node].Id, e.Port));
            }

            for (var i = 0; i < endpoints.Count; i++)
            {
                var j = matches[i];
                if (counts[i] != 1 || counts[j] != 1 || matches[j] != i)
                    continue;
                var a = Root(endpoints[i].Node);
                var b = Root(endpoints[j].Node);
                if (a != b)
                    components[a] = b;
            }

            for (var i = 0; i < nodes.Length; i++)
                components[i] = Root(i);
        }

        private int Root(int i)
        {
            while (components[i] != i)
                i = components[i];
            return i;
        }

        public bool IsPortOpen(ushort id, int port) => indices.TryGetValue(id, out int i) && port >= 0 &&
                                                       port < Ports(nodes[i].Kind).Count &&
                                                       !occupied.Contains((id, port));

        public bool Connected(ushort a, ushort b) => indices.TryGetValue(a, out int i) &&
                                                     indices.TryGetValue(b, out int j) &&
                                                     components[i] == components[j];

        public bool Matches(ushort id, PlasmaPartKind kind, Vector3 position, Quaternion rotation) =>
            indices.TryGetValue(id, out int i) && nodes[i].Kind == kind && nodes[i].Position == position &&
            nodes[i].Rotation == rotation;

        public int Count => nodes.Length;
    }

    public static class PlasmaDelivery
    {
        public static double Demand(bool connected, int coils, double perCoil) =>
            connected ? Max(0, coils) * perCoil : 0;

        public static double Share(double output, double demand, double totalDemand) =>
            Tuning.Finite(output) && Tuning.Finite(demand) && Tuning.Finite(totalDemand) && totalDemand > 0
                ? Min(Max(0, demand), Max(0, output) * Max(0, demand) / totalDemand)
                : 0;
    }
}