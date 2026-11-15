namespace WarpTen
{
    internal static class PartCatalog
    {
        internal static readonly (string Id, string Name, string Donor, float Width, float Height, float Length, float Mass)[] Parts =
        {
            ("core", "Warp Core", "StorageChest", 3, 3, 3, 1000),
            ("infuser", "Warp Coil Infuser", "Constant", 3, 2, 2, 100),
            ("coil", "Warp Coil", "Constant", 3, 2, 1, 100),
            ("conduit_straight", "Warp Plasma Conduit — Straight", "Constant", 1, 1, 1, 5),
            ("conduit_corner", "Warp Plasma Conduit — Corner", "Constant", 1, 1, 1, 5),
            ("conduit_tee", "Warp Plasma Conduit — T", "Constant", 1, 1, 1, 5),
            ("conduit_spacer", "Warp Plasma Conduit — Snap Spacer (0.25 m)", "Constant", .5f, .5f, .25f, 2)
        };
    }
}
