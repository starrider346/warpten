using System.Collections.Generic;
using static System.Math;

namespace WarpTen.Simulation
{
    public static class SocketLayout
    {
        public static int VisualCapacity(IReadOnlyList<byte> ids)
        {
            var capacity = 0;
            foreach (var id in ids)
                capacity = Max(capacity, id + 1);
            return capacity;
        }
    }
}