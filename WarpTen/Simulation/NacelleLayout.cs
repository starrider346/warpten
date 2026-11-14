using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using static System.Math;

namespace WarpTen.Simulation
{
    public readonly struct CoilNode
    {
        public readonly ushort Id;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public CoilNode(ushort id, Vector3 position, Quaternion rotation)
        {
            Id = id;
            Position = position;
            Rotation = rotation;
        }
    }

    public static class NacelleLayout
    {
        public const float Width = 3;
        public const float Height = 2;
        public const float InfuserLength = 2;
        public const float CoilLength = 1;
        public const float FirstCoilOffset = (InfuserLength + CoilLength) / 2;
        public static bool SameRotation(Quaternion a, Quaternion b) => Abs(Quaternion.Dot(a, b)) > .9999996f;

        public static bool Aligned(CoilNode a, CoilNode b)
        {
            var offset = Vector3.Transform(b.Position - a.Position, Quaternion.Inverse(a.Rotation));
            return SameRotation(a.Rotation, b.Rotation) && Abs(offset.Y) < .02 && Abs(offset.Z) < .02 &&
                   Abs(offset.X) >= Width;
        }

        public static List<ushort> Chain(CoilNode infuser, IReadOnlyList<CoilNode> coils)
        {
            var chain = new List<ushort>();
            var forward = Vector3.Transform(Vector3.UnitZ, infuser.Rotation);
            for (var i = 1; i <= coils.Count; i++)
            {
                var expected = infuser.Position - forward * (FirstCoilOffset + (i - 1) * CoilLength);
                var matches = coils.Where(c =>
                        Vector3.Distance(c.Position, expected) < .02 && SameRotation(c.Rotation, infuser.Rotation))
                    .ToArray();
                if (matches.Length != 1)
                    break;
                chain.Add(matches[0].Id);
            }

            return chain;
        }
    }

    public readonly struct SolidBox
    {
        public readonly Vector3 Center, HalfSize;
        public readonly Quaternion Rotation;

        public SolidBox(Vector3 center, Vector3 halfSize, Quaternion rotation)
        {
            Center = center;
            HalfSize = halfSize;
            Rotation = rotation;
        }

        public SolidBox InFrame(Vector3 origin, Quaternion rotation)
        {
            var inverse = Quaternion.Inverse(rotation);
            return new SolidBox(Vector3.Transform(Center - origin, inverse), HalfSize,
                Quaternion.Normalize(inverse * Rotation));
        }

        public void Project(Vector3 axis, out float min, out float max)
        {
            var centre = Vector3.Dot(Center, axis);
            var radius = (float)Radius(Axes(Rotation), HalfSize, axis);
            min = centre - radius;
            max = centre + radius;
        }

        public bool Overlaps(SolidBox other)
        {
            var a = Axes(Rotation);
            var b = Axes(other.Rotation);
            var delta = other.Center - Center;
            foreach (var axis in a.Concat(b).Concat(a.SelectMany(x => b.Select(y => Vector3.Cross(x, y)))))
            {
                if (axis.LengthSquared() < 1e-10)
                    continue;
                var ra = Radius(a, HalfSize, axis);
                var rb = Radius(b, other.HalfSize, axis);
                if (Abs(Vector3.Dot(delta, axis)) >= ra + rb - 1e-5)
                    return false;
            }

            return true;
        }

        private static Vector3[] Axes(Quaternion rotation) => new[]
        {
            Vector3.Transform(Vector3.UnitX, rotation),
            Vector3.Transform(Vector3.UnitY, rotation),
            Vector3.Transform(Vector3.UnitZ, rotation)
        };

        private static double Radius(Vector3[] axes, Vector3 half, Vector3 axis) =>
            Abs(Vector3.Dot(axes[0], axis)) * half.X + Abs(Vector3.Dot(axes[1], axis)) * half.Y +
            Abs(Vector3.Dot(axes[2], axis)) * half.Z;
    }
}