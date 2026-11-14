using System.Collections.Generic;
using System.Numerics;
using static System.Math;

namespace WarpTen.Simulation
{
    public readonly struct InfuserFeed
    {
        public readonly int Coils;
        public readonly string Issue;

        private InfuserFeed(int coils, string issue)
        {
            Coils = coils;
            Issue = issue;
        }

        public static InfuserFeed Evaluate(CoilNode infuser, IReadOnlyList<CoilNode> coils)
        {
            var count = NacelleLayout.Chain(infuser, coils).Count;
            if (count > 0)
                return new InfuserFeed(count, null);
            if (coils.Count == 0)
                return new InfuserFeed(0, "No coils installed");
            var expected = infuser.Position -
                           Vector3.Transform(Vector3.UnitZ, infuser.Rotation) * NacelleLayout.FirstCoilOffset;
            var nearest = float.MaxValue;
            var atPosition = 0;
            var sameRotation = false;
            for (var i = 0; i < coils.Count; i++)
            {
                var distance = Vector3.Distance(expected, coils[i].Position);
                nearest = Min(nearest, distance);
                if (distance < .02f)
                {
                    atPosition++;
                    sameRotation |= NacelleLayout.SameRotation(infuser.Rotation, coils[i].Rotation);
                }
            }

            var issue = atPosition > 1 ? "Overlapping first coils" :
                atPosition == 1 && !sameRotation ? "First coil faces the wrong way; match the infuser rotation" :
                $"First coil missing or offset: nearest is {nearest:F2}m from the required position (1.50m behind infuser)";
            return new InfuserFeed(0, issue);
        }
    }
}