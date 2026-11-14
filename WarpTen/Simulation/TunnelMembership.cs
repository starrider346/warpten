namespace WarpTen.Simulation
{
    internal interface ITunnelSpaces
    {
        bool Describe(uint id, out uint parent, out bool warp, out bool warpTen);
    }

    internal static class TunnelMembership
    {
        public static bool TryResolve<T>(T spaces, uint local, out uint warpId)
            where T : struct, ITunnelSpaces
        {
            warpId = 0;
            for (var depth = 0; depth < 64; depth++)
            {
                if (!spaces.Describe(local, out uint parent, out bool warp, out bool warpTen))
                    return false;
                if (warp)
                {
                    if (warpTen)
                        warpId = local;
                    return warpTen;
                }

                if (parent == local)
                    return false;
                local = parent;
            }

            return false;
        }
    }
}