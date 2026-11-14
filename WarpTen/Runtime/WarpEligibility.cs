using WorldTracking.Interface.Model.Spaces.Common;
using WorldTracking.Interface.Model.Spaces;

namespace WarpTen.Runtime
{
    internal static class WarpEligibility
    {
        internal static string Restriction(ISpace space, bool canWarp, bool inWarp)
        {
            if (inWarp || canWarp)
                return null;
            return space switch
            {
                PlanetSurfaceSpace => "Planet surface: climb into orbital space before engaging warp.",
                PlanetRingSpace or PlanetRingGlobalSpace =>
                    "Inside planetary rings: fly clear of the ring region before engaging warp.",
                ShipRelativeSpace =>
                    "Inside another ship's local space: move clear of that ship into independent space before engaging warp.",
                null => "Navigation location unavailable: waiting for the ship's space to load.",
                _ => "Warp is unavailable in this navigation region. Move into orbital or deep space."
            };
        }
    }
}