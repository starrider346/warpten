using static System.Math;

namespace WarpTen.Simulation
{
    internal static class WarpRotationPolicy
    {
        public const double MaximumRadiansPerSecond = PI / 180;

        public static double Scale(double x, double y, double z)
        {
            if (!Tuning.Finite(x) || !Tuning.Finite(y) || !Tuning.Finite(z))
                return 0;
            var magnitude = Sqrt(x * x + y * y + z * z);
            if (!Tuning.Finite(magnitude))
                return 0;
            return magnitude > MaximumRadiansPerSecond ? MaximumRadiansPerSecond / magnitude : 1;
        }
    }
}