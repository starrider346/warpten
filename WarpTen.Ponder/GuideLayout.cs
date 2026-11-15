using System.Numerics;

namespace WarpTen.Ponder
{
    internal readonly struct PreviewPart
    {
        internal readonly string Id, Part;
        internal readonly Vector3 Position;
        internal readonly float Yaw;
        internal readonly float Appears;
        internal PreviewPart(string id, string part, float x, float y, float z, float yaw, float appears)
        { Id = id; Part = part; Position = new Vector3(x, y, z); Yaw = yaw; Appears = appears; }
    }

    internal static class GuideLayout
    {
        internal static readonly PreviewPart[] Parts =
        {
            new("core", "core", 0, 0, 5.75f, 90, 0),
            new("left_infuser", "infuser", -3, 0, 0, 0, GuideTimeline.Infusers + 1),
            new("right_infuser", "infuser", 3, 0, 0, 0, GuideTimeline.Infusers + 1.5f),
            new("left_coil_1", "coil", -3, 0, -1.5f, 0, GuideTimeline.Coils + 1),
            new("right_coil_1", "coil", 3, 0, -1.5f, 0, GuideTimeline.Coils + 1.5f),
            new("left_coil_2", "coil", -3, 0, -2.5f, 0, GuideTimeline.Coils + 2),
            new("right_coil_2", "coil", 3, 0, -2.5f, 0, GuideTimeline.Coils + 2.5f),
            new("feed", "conduit_straight", 0, 0, 3.5f, 0, GuideTimeline.Plasma),
            new("split", "conduit_tee", 0, 0, 2.5f, 270, GuideTimeline.Plasma + 4.5f),
            new("left_run_1", "conduit_straight", -1, 0, 2.5f, 90, GuideTimeline.Plasma + 5),
            new("right_run_1", "conduit_straight", 1, 0, 2.5f, 90, GuideTimeline.Plasma + 5.25f),
            new("left_run_2", "conduit_straight", -2, 0, 2.5f, 90, GuideTimeline.Plasma + 5.5f),
            new("right_run_2", "conduit_straight", 2, 0, 2.5f, 90, GuideTimeline.Plasma + 5.75f),
            new("left_corner", "conduit_corner", -3, 0, 2.5f, 0, GuideTimeline.Plasma + 6),
            new("right_corner", "conduit_corner", 3, 0, 2.5f, 90, GuideTimeline.Plasma + 6.25f),
            new("left_inlet", "conduit_straight", -3, 0, 1.5f, 0, GuideTimeline.Plasma + 6.5f),
            new("right_inlet", "conduit_straight", 3, 0, 1.5f, 0, GuideTimeline.Plasma + 6.75f),
            new("spacer", "conduit_spacer", 0, .25f, 4.125f, 0, GuideTimeline.Spacer + 1)
        };
    }
}
