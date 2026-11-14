using System.Numerics;
using static System.Math;

namespace WarpTen.Simulation
{
    public readonly struct ConduitPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public ConduitPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    public static class ConduitPlacement
    {
        private static bool Finite(Vector3 v) => Tuning.Finite(v.X) && Tuning.Finite(v.Y) && Tuning.Finite(v.Z);

        public static bool TryAttach(PlasmaPartKind kind, Vector3 target, Vector3 normal, Vector3 up, int turns,
            out ConduitPose pose)
        {
            pose = default;
            if (!PlasmaNetwork.IsConduit(kind) || !Finite(target) || !Finite(normal) || !Finite(up) ||
                normal.LengthSquared() < 1e-8f)
                return false;
            normal = Vector3.Normalize(normal);
            up -= normal * Vector3.Dot(up, normal);
            if (up.LengthSquared() < 1e-8f)
            {
                up = Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitZ;
                up -= normal * Vector3.Dot(up, normal);
            }

            var right = Vector3.Normalize(Vector3.Cross(up, normal));
            up = Vector3.Cross(normal, right);
            var frame = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, normal.X, normal.Y, normal.Z,
                0, 0, 0, 0, 1);
            var rotation =
                Quaternion.Normalize(
                    Quaternion.CreateFromAxisAngle(normal, (turns % 4 + 4) % 4 * (float)(PI / 2)) *
                    Quaternion.CreateFromRotationMatrix(frame));
            var incoming = PlasmaNetwork.Ports(kind)[0];
            pose = new ConduitPose(target - Vector3.Transform(incoming.Position, rotation), rotation);
            return true;
        }

        public static bool IsAttached(PlasmaPartKind kind, ConduitPose pose, Vector3 target, Vector3 normal)
        {
            if (!PlasmaNetwork.IsConduit(kind) || !Finite(pose.Position) || !Finite(target) || !Finite(normal))
                return false;
            var port = PlasmaNetwork.Ports(kind)[0];
            var position = pose.Position + Vector3.Transform(port.Position, pose.Rotation);
            var facing = Vector3.Transform(port.Normal, pose.Rotation);
            return Vector3.DistanceSquared(position, target) <=
                   PlasmaNetwork.PositionTolerance * PlasmaNetwork.PositionTolerance &&
                   Vector3.Dot(facing, normal) <= PlasmaNetwork.NormalDotLimit;
        }

        public static bool Matches(ConduitPose a, ConduitPose b) =>
            Vector3.DistanceSquared(a.Position, b.Position) <= .0001f &&
            Abs(Quaternion.Dot(a.Rotation, b.Rotation)) > .9999996f;
    }
}