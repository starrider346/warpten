using System.Collections.Generic;
using System.Numerics;
using static System.Math;

namespace WarpTen.Simulation
{
    public sealed class NacelleClearance
    {
        public const float MaximumOutboardBlocks = 3;
        public const int CurveSegments = 32;
        public readonly float HalfGap, MinZ, MaxZ;
        private readonly Vector2[] outline;
        public float PeakHeight => HalfGap / 2;
        public float LeftWidthLimit => -HalfGap - NacelleLayout.Width - MaximumOutboardBlocks;
        public float RightWidthLimit => -LeftWidthLimit;

        public NacelleClearance(float centreSeparation, float minZ, float maxZ)
        {
            HalfGap = Max(0, (centreSeparation - NacelleLayout.Width) / 2);
            MinZ = minZ;
            MaxZ = maxZ;
            var points = new List<Vector2>();
            for (var i = 0; i <= CurveSegments; i++)
            {
                var x = CurveX(i);
                points.Add(new Vector2(x, HeightAt(x)));
            }

            for (var i = CurveSegments - 1; i > 0; i--)
            {
                var x = CurveX(i);
                points.Add(new Vector2(x, -HeightAt(x)));
            }

            outline = points.ToArray();
        }

        public float CurveX(int sample) => -HalfGap + 2 * HalfGap * sample / CurveSegments;
        public float HeightAt(float x) => HalfGap <= 0 ? 0 : PeakHeight * Max(0, 1 - x * x / (HalfGap * HalfGap));

        public bool WidthFits(float minX, float maxX) =>
            minX >= LeftWidthLimit - 1e-5f && maxX <= RightWidthLimit + 1e-5f;

        public bool Intersects(SolidBox box)
        {
            if (HalfGap <= 1e-5f || MaxZ - MinZ <= 1e-5f)
                return false;
            var axes = new[]
            {
                Vector3.Transform(Vector3.UnitX, box.Rotation),
                Vector3.Transform(Vector3.UnitY, box.Rotation),
                Vector3.Transform(Vector3.UnitZ, box.Rotation)
            };

            bool Separated(Vector3 axis)
            {
                var length = axis.Length();
                if (length < 1e-6f)
                    return false;
                axis /= length;
                var min = float.PositiveInfinity;
                var max = float.NegativeInfinity;
                foreach (var p in outline)
                {
                    var dot = p.X * axis.X + p.Y * axis.Y;
                    min = Min(min, dot);
                    max = Max(max, dot);
                }

                min += axis.Z >= 0 ? MinZ * axis.Z : MaxZ * axis.Z;
                max += axis.Z >= 0 ? MaxZ * axis.Z : MinZ * axis.Z;
                var centre = Vector3.Dot(box.Center, axis);
                var radius = Abs(Vector3.Dot(axes[0], axis)) * box.HalfSize.X +
                             Abs(Vector3.Dot(axes[1], axis)) * box.HalfSize.Y +
                             Abs(Vector3.Dot(axes[2], axis)) * box.HalfSize.Z;
                return centre + radius <= min + 1e-5f || centre - radius >= max - 1e-5f;
            }

            if (Separated(Vector3.UnitX) || Separated(Vector3.UnitY) || Separated(Vector3.UnitZ))
                return false;
            foreach (var axis in axes)
                if (Separated(axis) || Separated(Vector3.Cross(axis, Vector3.UnitZ)))
                    return false;
            for (var i = 0; i < outline.Length; i++)
            {
                var edge2 = outline[(i + 1) % outline.Length] - outline[i];
                var edge = new Vector3(edge2.X, edge2.Y, 0);
                if (Separated(new Vector3(-edge.Y, edge.X, 0)))
                    return false;
                foreach (var axis in axes)
                    if (Separated(Vector3.Cross(axis, edge)))
                        return false;
            }

            return true;
        }
    }
}